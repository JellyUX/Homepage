import { test, expect } from '@playwright/test';
import { env, NATIVE_SECTIONS, TABS, login, loadedSections, apiGet, watchJuxErrors, trackJuxRequests } from '../helpers.mjs';

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
    expect(requests.since().length).toBe(2 + throughSection.length);
    expect(requests.duplicates()).toEqual([]);

    const longFrames = await page.evaluate(() => window.__juxLongFrames);
    test.info().annotations.push(
        { type: 'JellyUX requests', description: requests.since().map((r) => `${r.method} ${r.path}`).join(', ') },
        { type: 'JellyUX long frames (ms)', description: JSON.stringify(longFrames) });
    expect(longFrames.filter((d) => d > 100)).toEqual([]);
});

test('coming back to the home page within the app issues no JellyUX request (PROC-03.a)', async ({ page }) => {
    const requests = trackJuxRequests(page);
    await login(page, env.admin);
    await loadedSections(page);

    const before = requests.mark();
    await page.evaluate(() => { location.hash = '#/movies'; });
    await expect(page).toHaveURL(/#\/movies/);
    await page.evaluate(() => { location.hash = '#/home'; });
    await expect(page.locator('.homeSectionsContainer.jux-homepage-active .jux-widget-section').first()).toBeVisible();
    await page.waitForTimeout(3000);

    expect(requests.since(before).filter((r) => r.path.includes('/JuxHomepage/'))).toEqual([]);
});

test('an open detail page issues no JellyUX request once loaded, for 30 s (PROC-03.a)', async ({ page }) => {
    test.setTimeout(90000);
    const requests = trackJuxRequests(page);
    await login(page, env.admin);
    await page.locator('.jux-widget-section').first().waitFor();
    const movies = await apiGet(env.admin, `/Items?recursive=true&includeItemTypes=Movie&limit=1&userId=${env.admin.id}`);

    const includedIn = page.waitForRequest((req) => req.url().includes('/JuxHomepage/Collections/IncludedIn/'));
    await page.evaluate((id) => { location.hash = `#/details?id=${id}&serverId=${window.ApiClient.serverId()}`; }, movies.Items[0].Id);
    await includedIn;
    await page.waitForTimeout(2000);

    // JellyUX API requests only: the detail page's own library queries (Users/{id}/Items) can still
    // arrive after the mark on a slow runner, and no JellyUX script issues them there.
    const opened = requests.mark();
    await page.waitForTimeout(30000);
    expect(requests.since(opened).filter((r) => r.path.includes('/JuxHomepage/'))).toEqual([]);
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

test('the four JellyUX tabs open, one pane at a time', async ({ page }) => {
    const errors = watchJuxErrors(page);
    await login(page, env.admin);

    for (const tab of TABS) {
        await page.locator(`.emby-tab-button[data-index="${tab.index}"]`).click();
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

test('the JellyUX tabs arrive with the native ones and the tab bar stays still (F-42)', async ({ page }) => {
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
