import { test, expect } from '@playwright/test';
import { env, JELLYFIN_12, NATIVE_SECTIONS, TABS, login, loadedSections, apiGet, watchJuxErrors, trackJuxRequests, isPainted, openTab } from '../helpers.mjs';

// The native sections the client reads straight from Jellyfin (NATIVE_ROUTES in jux-homepage.js); every
// other section, native ones included, is read through JuxHomepage/Section/{type}.
const NATIVE_ROUTED = [
    'jux-native-continue-watching',
    'jux-native-next-up',
    'jux-native-recently-added-movies',
    'jux-native-recently-added-shows',
    'jux-native-recently-added-episodes'
];

/** Records the long animation frames over 50 ms whose scripts include a JellyUX asset. */
async function watchJuxLongFrames(page) {
    await page.addInitScript(() => {
        window.__juxLongFrames = [];
        try {
            new PerformanceObserver((list) => {
                for (const entry of list.getEntries()) {
                    if (entry.duration > 50 && (entry.scripts || []).some((s) => (s.sourceURL || '').includes('/JuxHomepage/'))) {
                        window.__juxLongFrames.push(Math.round(entry.duration));
                    }
                }
            }).observe({ type: 'long-animation-frame', buffered: true });
        } catch {
            // Browsers without the Long Animation Frames API record nothing.
        }
    });
}

test('the home page reads each JellyUX section once, plus the layout and the watchlist ids (PROC-03.a)', async ({ page }) => {
    // Budget relative to the configuration: 1 Sections page, 1 Section/{type} per section the client
    // does not read from a native route, 1 Watchlist/Ids, no request twice. Long frames attributed to a
    // JellyUX script are reported, and fail the test above 100 ms.
    await watchJuxLongFrames(page);
    const requests = trackJuxRequests(page);
    await login(page, env.admin);

    const sections = await loadedSections(page);
    const throughSection = sections.filter((s) => !NATIVE_ROUTED.includes(s)).map((s) => s.replace(/^jux-([a-z]+)-/, 'jux.$1.'));
    const sectionPaths = requests.since().filter((r) => /\/JuxHomepage\/Section\//.test(r.path)).map((r) => r.path.split('/').pop());

    expect(requests.count(/\/JuxHomepage\/Sections$/)).toBe(1);
    expect(sectionPaths.sort()).toEqual(throughSection.sort());
    expect(requests.count(/\/JuxHomepage\/Watchlist\/Ids$/)).toBe(1);
    expect(requests.since().filter((r) => r.kind === 'jux').length).toBe(2 + throughSection.length);
    // The native section queries (A-08): one library views read, one per native-routed section.
    const recentlyAdded = sections.filter((s) => s.startsWith('jux-native-recently-added-')).length;
    expect(requests.count(/\/(UserViews|Views)$/)).toBe(1);
    expect(requests.count(/\/Resume$/)).toBe(sections.includes('jux-native-continue-watching') ? 1 : 0);
    expect(requests.count(/\/Shows\/NextUp$/)).toBe(sections.includes('jux-native-next-up') ? 1 : 0);
    expect(requests.count(/\/Items\/Latest$/)).toBe(recentlyAdded);
    expect(requests.duplicates()).toEqual([]);

    const longFrames = await page.evaluate(() => window.__juxLongFrames);
    test.info().annotations.push(
        { type: 'JellyUX requests', description: requests.since().map((r) => `${r.method} ${r.path}`).join(', ') },
        { type: 'JellyUX long frames (ms)', description: JSON.stringify(longFrames) });
    expect(longFrames.filter((d) => d > 100)).toEqual([]);
});

test('an unscrolled home page fetches only the rows within one screen below the viewport (PERF-09.b)', async ({ page }) => {
    // A short screen, so that rows of the small test library wait below it.
    await page.setViewportSize({ width: 1280, height: 400 });
    const requests = trackJuxRequests(page);
    await login(page, env.admin);
    await page.locator('.homeSectionsContainer.jux-homepage-active .jux-widget-section').first().waitFor();
    await page.waitForTimeout(3000);

    // A fetched row has lost its grey cards; a waiting one still lies more than a screen below.
    const rows = await page.locator('.jux-items-container').evaluateAll((all) => all.map((row) => ({
        waiting: !!row.querySelector('.jux-skeleton-card'),
        top: row.getBoundingClientRect().top,
        screen: innerHeight
    })));
    const rowRequests = requests.since().filter((r) => /\/JuxHomepage\/Section\/|\/Resume$|\/Shows\/NextUp$|\/Items\/Latest$/.test(r.path));

    test.info().annotations.push({ type: 'rows fetched / built', description: `${rowRequests.length} / ${rows.length}` });
    expect(rows[0].waiting).toBe(false);
    expect(rows.filter((r) => r.waiting).length).toBeGreaterThan(0);
    expect(rowRequests).toHaveLength(rows.filter((r) => !r.waiting).length);
    expect(rows.filter((r) => r.waiting && r.top < 2 * r.screen)).toEqual([]);
    expect(requests.duplicates()).toEqual([]);
    // A waiting row stays still: its grey cards run no animation (each frame restyled them).
    expect(await page.evaluate(() => document.getAnimations()
        .filter((a) => a.effect && a.effect.target && a.effect.target.closest('.jux-items-container')).length)).toBe(0);
});

test('coming back to the home page within the app issues no JellyUX or home section request (PROC-03.a, A-08)', async ({ page }) => {
    const requests = trackJuxRequests(page);
    await login(page, env.admin);
    await loadedSections(page);

    const before = requests.mark();
    await page.evaluate(() => { location.hash = '#/movies'; });
    await expect(page).toHaveURL(/#\/movies/);
    await page.evaluate(() => { location.hash = '#/home'; });
    await expect(page.locator('.homeSectionsContainer.jux-homepage-active .jux-widget-section').first()).toBeVisible();
    await page.waitForTimeout(3000);

    // The library page visited on the way sends its own native first-page query (Users/{id}/Items,
    // StartIndex=0), which can arrive after the mark: library queries are left out here, the infinite
    // scroll is budgeted by navigation.spec.mjs (FIAB-01).
    // Jellyfin 12 mounts a new home view on the way back: JellyUX paints the layout and the rows it read
    // in the last 5 min again with no request, and reads the live rows only, once each (COMPAT-11,
    // DR-026: Continue Watching and Next Up for V17, the watchlist the page left could change).
    const live = JELLYFIN_12
        ? ['GET /JuxHomepage/Section/jux.native.watchlist', 'GET /Shows/NextUp', 'GET /UserItems/Resume']
        : [];
    const seen = requests.since(before).filter((r) => r.kind !== 'library');
    expect(seen.map((r) => `${r.method} ${r.path}`).sort()).toEqual(live);
    expect(requests.duplicates(before)).toEqual([]);
});

test('an open detail page issues no JellyUX request once loaded, for 30 s (PROC-03.a)', async ({ page }) => {
    test.setTimeout(90000);
    const requests = trackJuxRequests(page);
    await login(page, env.admin);
    await page.locator('.jux-widget-section').first().waitFor();
    const movies = await apiGet(env.admin, `/Items?recursive=true&includeItemTypes=Movie&limit=1&userId=${env.admin.id}`);

    // Jellyfin 12 lists the collections itself (#collectionsCollapsible): JellyUX adds no section there
    // (COMPAT-07); the breadcrumb still reads IncludedIn for the collection of its trail.
    const detailReady = JELLYFIN_12
        ? null
        : page.waitForRequest((req) => req.url().includes('/JuxHomepage/Collections/IncludedIn/'));
    await page.evaluate((id) => { location.hash = `#/details?id=${id}&serverId=${window.ApiClient.serverId()}`; }, movies.Items[0].Id);
    await (detailReady ?? page.locator('.libraryPage:not(.hide) #collectionsCollapsible').waitFor({ state: 'attached' }));
    await page.waitForTimeout(2000);
    if (JELLYFIN_12) {
        await expect(page.locator('.jux-collections-section')).toHaveCount(0);
    }

    // The detail page's own playable-items query (Users/{id}/Items with Filters=IsNotFolder, a filter
    // no JellyUX script sends) can still arrive after the mark on a slow runner: it alone is left out.
    const opened = requests.mark();
    await page.waitForTimeout(30000);
    expect(requests.since(opened).filter((r) => !r.url.includes('Filters=IsNotFolder'))).toEqual([]);
});

test('a fresh install renders the seven native sections, each with cards (F-25)', async ({ page }) => {
    const errors = watchJuxErrors(page);
    await login(page, env.admin);

    expect(await loadedSections(page)).toEqual(NATIVE_SECTIONS);
    for (const section of NATIVE_SECTIONS) {
        await expect(page.locator(`.jux-widget-${section} .card`).first()).toBeVisible();
    }
    expect(errors).toEqual([]);
});

test('a long section title wraps left of the native scroll buttons at 390 px (KTW-04)', async ({ page }) => {
    // Run at 390 px by both projects: the scroll buttons show with a mouse, and with touch on Jellyfin
    // 10.11. Jellyfin 12 shows none in its touch layout, on its own sections too (COMPAT-12): the JellyUX
    // sections follow it there, and their titles have nothing to wrap around.
    await page.setViewportSize({ width: 390, height: 844 });
    await login(page, env.admin);
    if (JELLYFIN_12 && test.info().project.name === 'mobile') {
        await page.locator('.jux-widget-section .sectionTitle').first().waitFor();
        expect(await page.locator('.jux-widget-section .emby-scrollbuttons').count()).toBe(0);
        expect(await isPainted(page.locator('.jux-widget-section .sectionTitle').first())).toBe(true);
        return;
    }
    await page.locator('.jux-widget-section .emby-scrollbuttons').first().waitFor();
    await page.locator('.jux-widget-section .sectionTitle').first()
        .evaluate((title) => { title.textContent = 'Because you watched House of the Dragon'; });

    const overlaps = await page.locator('.jux-widget-section').evaluateAll((sections) => sections
        .map((s) => [s.querySelector('.sectionTitle'), s.querySelector('.emby-scrollbuttons')])
        .filter(([title, buttons]) => title && buttons)
        .map(([title, buttons]) => [title.getBoundingClientRect(), buttons.getBoundingClientRect(), title.textContent])
        .filter(([t, b]) => t.right > b.left && t.left < b.right && t.bottom > b.top && t.top < b.bottom)
        .map(([, , text]) => text));

    await test.info().attach('titles at 390 px', { body: await page.screenshot(), contentType: 'image/png' });
    expect(overlaps).toEqual([]);
    expect(await isPainted(page.locator('.jux-widget-section .sectionTitle').first())).toBe(true);
});

test('the three JellyUX tabs open, one pane at a time', async ({ page }) => {
    const errors = watchJuxErrors(page);
    await login(page, env.admin);

    for (const tab of TABS) {
        await openTab(page, tab);
        const pane = page.locator(`#${tab.id}`);
        await expect(pane).toBeVisible();
        await expect(pane).toHaveClass(/is-active/);
        await expect(pane).not.toHaveText(/^\s*$/);
        for (const other of TABS.filter((t) => t !== tab)) {
            await expect(page.locator(`#${other.id}`)).toBeHidden();
        }
    }
    expect(errors).toEqual([]);
});

// FEAT-03.b: History opens on its Movies sub-tab; the Series progress is read only once its sub-tab is
// opened, and the sub-tab shown is kept in the address.
test('the History tab reads the series progress only when its Series sub-tab opens (FEAT-03.b)', async ({ page }) => {
    const requests = trackJuxRequests(page);
    await login(page, env.admin);
    const history = TABS.find((tab) => tab.subs);

    await openTab(page, history);
    await expect(page.locator('#jux-subtab-movies')).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator(`#${history.id}-movies .jux-sort-button`)).toBeVisible();
    await page.waitForTimeout(1000);
    expect(requests.count(/\/JuxHomepage\/SeriesProgress\/Items$/i)).toBe(0);

    await page.locator('#jux-subtab-series').click();
    await expect(page.locator(`#${history.id}-series .jux-sort-button`)).toBeVisible();
    await expect.poll(() => requests.count(/\/JuxHomepage\/SeriesProgress\/Items$/i)).toBe(1);
    await expect(page.locator(`#${history.id}-movies`)).toBeHidden();
    await expect(page).toHaveURL(/[?&]sub=series/);
});

test('an old link to the Statistics tab (tab 5) opens Statistics (FEAT-03.b)', async ({ page }) => {
    await login(page, env.admin);
    await page.locator('#jux-tab-statistics').waitFor({ state: 'attached' });

    await page.evaluate(() => { location.hash = '#/home?tab=5'; });

    await expect(page).toHaveURL(/#\/home\?tab=4$/);
    // Jellyfin 10.11 loads a new home view for the new address and keeps the old one hidden, panes included
    // (E2-04): the visible pane is the one that counts.
    await expect(page.locator('#jux-tab-statistics:visible .jux-stat-card').first()).toBeVisible();
});

// A JellyUX tab opened by its address (a bookmark, a reload) is served like one opened from the home page:
// the Watchlist shows its card, which needs the native card builder the home tab provides.
for (const tab of TABS) {
    test(`the address #/home?tab=${tab.index} opens ${tab.id} with its content`, async ({ page }) => {
        const errors = watchJuxErrors(page);
        await login(page, env.admin);
        await page.goto(`${env.serverUrl}/web/#/home?tab=${tab.index}`);
        await page.reload();

        const pane = page.locator(`#${tab.id}:visible`);
        if (tab.index === 2) {
            await expect(pane.locator('.card').first()).toBeVisible();
        } else if (tab.subs) {
            await expect(pane.locator('.jux-sort-button').first()).toBeVisible();
        } else {
            await expect(pane.locator('.jux-stat-card').first()).toBeVisible();
        }
        await expect(page).toHaveURL(new RegExp(`[?&]tab=${tab.index}(&|$)`));
        expect(errors).toEqual([]);
    });
}

// PERF-10.b (DR-012): the tab scripts arrive after the window load event; a tab clicked before them is
// served once they do. The rest bundle is held until after the click (routing also bypasses the cache).
test('a tab clicked before the tab scripts arrive shows its content once they do (PERF-10.b)', async ({ page }) => {
    const errors = watchJuxErrors(page);
    await login(page, env.admin);
    let release;
    const held = new Promise((resolve) => { release = resolve; });
    await page.route(/\/JuxHomepage\/jux-rest\.js/, async (route) => {
        await held;
        await route.continue();
    });
    await page.reload();

    await openTab(page, TABS[0]);
    expect(await page.evaluate(() => typeof window.juxWatchlist)).toBe('undefined');
    release();

    await expect(page.locator(`#${TABS[0].id}:visible .card`).first()).toBeVisible();
    expect(errors).toEqual([]);
});

// Jellyfin 12 Modern layout: the entries arrive with the native header links, after them, so Favorites
// never moves. Polled every frame from a reload, by a script that runs before the page's own (D-13).
test('the JellyUX entries arrive with the native header links and the bar stays still (F-42, Jellyfin 12)', async ({ page }) => {
    test.skip(!JELLYFIN_12, 'Jellyfin 10.11 has no Modern header: the tab bar case below covers it');
    test.skip(test.info().project.name === 'mobile', 'no header links at 390 px in the Modern layout: the tabs are in the menu drawer');
    await login(page, env.admin);
    await page.locator('.jux-modern-tab').first().waitFor();
    await page.addInitScript(() => {
        const t0 = performance.now();
        const out = window.__f42 = { native: null, jux: null, favoritesX: [] };
        const tick = () => {
            const t = performance.now() - t0;
            const favorites = document.querySelector('header.MuiAppBar-root a[href="#/home?tab=1"]');
            if (favorites && out.native === null) { out.native = t; }
            if (document.querySelector('.jux-modern-tab') && out.jux === null) { out.jux = t; }
            if (favorites) { out.favoritesX.push(Math.round(favorites.getBoundingClientRect().x)); }
            if (t < 6000) { requestAnimationFrame(tick); }
        };
        requestAnimationFrame(tick);
    });

    await page.reload();
    await page.waitForTimeout(6500);
    const run = await page.evaluate(() => window.__f42);

    expect(run.native).not.toBeNull();
    expect(run.jux).not.toBeNull();
    expect(run.jux - run.native).toBeLessThan(100);
    expect(new Set(run.favoritesX).size).toBe(1);
});

test('the JellyUX tabs arrive with the native ones and the tab bar stays still (F-42)', async ({ page }) => {
    test.skip(JELLYFIN_12, 'Jellyfin 12 Modern layout: no tab bar, the header case above covers it');
    await login(page, env.admin);
    await page.locator('#jux-tabbtn-watchlist').waitFor();
    await page.evaluate(() => { location.hash = '#/movies.html'; });
    await page.waitForTimeout(2000);

    // Polled every frame from the navigation back home: when the native and the JellyUX tab buttons
    // first exist, and every position of the Home button.
    const run = await page.evaluate(() => new Promise((resolve) => {
        const t0 = performance.now();
        const out = { native: null, jux: null, homeX: [] };
        location.hash = '#/home';
        const tick = () => {
            const t = performance.now() - t0;
            const home = document.querySelector('.emby-tabs-slider button');
            if (home && out.native === null) { out.native = t; }
            if (document.getElementById('jux-tabbtn-watchlist') && out.jux === null) { out.jux = t; }
            if (home && t >= 300) { out.homeX.push(Math.round(home.getBoundingClientRect().x)); }
            if (t < 1500) { requestAnimationFrame(tick); } else { resolve(out); }
        };
        requestAnimationFrame(tick);
    }));

    expect(run.native).not.toBeNull();
    expect(run.jux).not.toBeNull();
    expect(run.jux - run.native).toBeLessThan(100);
    expect(new Set(run.homeX).size).toBe(1);
});

test('a user restricted to the movie library sees no show (F-19)', async ({ page }) => {
    await login(page, env.restricted);

    const sections = await loadedSections(page);
    expect(sections).toContain('jux-native-recently-added-movies');
    expect(sections).not.toContain('jux-native-recently-added-shows');
    expect(sections).not.toContain('jux-native-recently-added-episodes');

    const titles = await page.locator('.jux-widget-section .cardText').allInnerTexts();
    for (const show of env.shows) {
        expect(titles.join('\n')).not.toContain(show);
    }

    const episodes = await apiGet(env.restricted, `/JuxHomepage/Section/jux.native.recently-added-episodes?userId=${env.restricted.id}`);
    expect(episodes.Items ?? []).toEqual([]);
});

test('"see all" opens the native list of the section, never a missing page (F-56)', async ({ page }) => {
    const errors = watchJuxErrors(page);
    await login(page, env.admin);

    await page.locator('.jux-widget-jux-native-next-up .sectionTitleContainer a').click();
    await expect(page).toHaveURL(/#\/list\?type=nextup/);
    await expect(page.locator('.page:not(.hide) .card').first()).toBeVisible();

    await page.goBack();
    await page.locator('.jux-widget-jux-native-recently-added-movies .sectionTitleContainer a').click();
    await expect(page).toHaveURL(/#\/movies\?topParentId=[0-9a-f]+&collectionType=movies&tab=1/);
    await expect(page.locator('.page:not(.hide) .card').first()).toBeVisible();
    expect(errors).toEqual([]);
});
