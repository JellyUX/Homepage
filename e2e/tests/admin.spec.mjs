import { test, expect } from '@playwright/test';
import { env, login, loadedSections, watchJuxErrors, adminToken, pluginConfiguration } from '../helpers.mjs';

function cardNames(page) {
    return page.locator('#juxHomepageConfigPage:visible .jux-admin-widget-card .checkboxLabel').allInnerTexts();
}

test('the admin page keeps Save in view and moves a section by drag and drop or by position (F-50)', async ({ page }) => {
    const token = await adminToken();
    const saved = await pluginConfiguration(token);
    const errors = watchJuxErrors(page);
    try {
        await login(page, env.admin);
        await page.goto(`${env.serverUrl}/web/#/configurationpage?name=JellyUX%20Homepage`);
        const rows = saved.Widgets.slice().sort((a, b) => a.Order - b.Order).map((w) => w.WidgetType);
        const cards = page.locator('#juxHomepageConfigPage:visible .jux-admin-widget-card');
        await expect(cards).toHaveCount(rows.length);

        // Folded cards and a Save button reachable from the top of the page.
        await expect(page.locator('#juxHomepageConfigPage:visible .jux-admin-widget-card-collapsed')).toHaveCount(rows.length);
        await expect(page.locator('#juxHomepageConfigPage:visible .jux-admin-actions .button-submit')).toBeInViewport();
        await expect(page.locator('#txtTMDb')).toHaveAttribute('type', 'password');

        // The sections live in their own tab (ADM-01.a), which the page does not open first.
        await expect(cards.first()).toBeHidden();
        await page.locator('#juxTab_sections').click();
        await expect(cards.first()).toBeVisible();

        // Drag the Watchlist section onto the top half of the first card. Both must be on screen,
        // clear of the dashboard app bar and of the sticky Save bar, for the pointer to reach them.
        await page.setViewportSize({ width: 1920, height: 1700 });
        await cards.first().evaluate((c) => c.scrollIntoView({ block: 'center' }));
        const before = await cardNames(page);
        const watchlist = rows.indexOf('jux.native.watchlist');
        const myMedia = rows.indexOf('jux.native.my-media');
        expect(myMedia).toBeLessThan(watchlist);
        await cards.nth(watchlist).locator('.jux-admin-drag-handle').dragTo(cards.first(), { targetPosition: { x: 40, y: 5 } });
        await expect.poll(async () => (await cardNames(page))[0]).toBe(before[watchlist]);

        // Type a position: My Media, one row lower since the move, goes first.
        const position = cards.nth(myMedia + 1).locator('.jux-admin-position');
        await position.fill('1');
        await position.press('Enter');
        await expect.poll(async () => (await cardNames(page)).slice(0, 2)).toEqual([before[myMedia], before[watchlist]]);

        await page.locator('#juxHomepageConfigPage:visible .jux-admin-actions .button-submit').click();
        await expect.poll(async () => {
            const widgets = (await pluginConfiguration(token)).Widgets.slice().sort((a, b) => a.Order - b.Order);
            return widgets.slice(0, 2).map((w) => w.WidgetType);
        }).toEqual(['jux.native.my-media', 'jux.native.watchlist']);

        await page.goto(`${env.serverUrl}/web/#/home`);
        await page.reload();
        expect((await loadedSections(page)).slice(0, 2)).toEqual(['jux-native-my-media', 'jux-native-watchlist']);
        expect(errors).toEqual([]);
    } finally {
        await pluginConfiguration(token, saved);
    }
});

test('saving the admin page unchanged keeps every row as loaded (FIAB-02)', async ({ page }) => {
    // Rows whose filters are stored as ids (a Wikidata ceremony, TMDb person and genre) the page shows
    // through labels it looks up: a Save without any change must write them back as they were.
    const token = await adminToken();
    const saved = await pluginConfiguration(token);
    const seeded = structuredClone(saved);
    seeded.Widgets.push(
        { WidgetType: 'jux.connected.rewards', Enabled: true, Order: 900, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait',
            ExtraParams: [{ Key: 'value', Value: '11111111-2222-3333-4444-555555555555' }, { Key: 'ceremonyQid', Value: 'Q19020' }, { Key: 'year', Value: '2024' }] },
        { WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 910, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait',
            ExtraParams: [{ Key: 'value', Value: '66666666-7777-8888-9999-000000000000' }, { Key: 'personIds', Value: '525' }, { Key: 'genreIds', Value: '18' },
                { Key: 'sortBy', Value: 'popularity.desc' }, { Key: 'minVotes', Value: '50' }, { Key: 'pages', Value: '1' }] });
    const filters = (config) => config.Widgets
        .filter((w) => w.WidgetType === 'jux.connected.rewards' || w.WidgetType === 'jux.connected.discover-movies')
        .map((w) => ({ type: w.WidgetType, params: Object.fromEntries(w.ExtraParams.filter((p) => p.Key !== 'value').map((p) => [p.Key, p.Value])) }));
    try {
        await pluginConfiguration(token, seeded);
        const loaded = filters(await pluginConfiguration(token));
        await login(page, env.admin);
        await page.goto(`${env.serverUrl}/web/#/configurationpage?name=JellyUX%20Homepage`);
        await expect(page.locator('#juxHomepageConfigPage:visible .jux-admin-widget-card')).toHaveCount(seeded.Widgets.length);

        const save = page.waitForRequest((r) => r.method() === 'POST' && /Plugins\/.*Configuration/.test(r.url()));
        await page.locator('#juxHomepageConfigPage:visible .jux-admin-actions .button-submit').click();
        await (await save).response();

        expect(filters(await pluginConfiguration(token))).toEqual(loaded);
    } finally {
        await pluginConfiguration(token, saved);
    }
});

// FIAB-02.a: a field the browser would refuse, out of sight in another tab or in a folded card, used to
// stop Save without a word (no request, no message). Save now opens the field's tab and card and puts the
// focus on it, still sending nothing.
test('FIAB-02.a: Save shows an invalid field hidden in another tab or a folded card', async ({ page }) => {
    const token = await adminToken();
    const saved = await pluginConfiguration(token);
    const seeded = structuredClone(saved);
    seeded.Widgets.push({ WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 900, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait',
        ExtraParams: [{ Key: 'value', Value: '66666666-7777-8888-9999-000000000000' }, { Key: 'pages', Value: '1' }] });
    const posts = [];
    page.on('request', (r) => {
        if (r.method() === 'POST' && /Plugins\/.*Configuration/.test(r.url())) { posts.push(r.url()); }
    });
    try {
        await pluginConfiguration(token, seeded);
        await login(page, env.admin);
        await page.goto(`${env.serverUrl}/web/#/configurationpage?name=JellyUX%20Homepage`);
        const pageRoot = page.locator('#juxHomepageConfigPage:visible');
        const save = pageRoot.locator('.jux-admin-actions .button-submit');

        // An interval of 0 in Sources, saved from Settings.
        await pageRoot.locator('#juxTab_sources').click();
        const refresh = pageRoot.locator('#numTMDbRefresh');
        await refresh.fill('0');
        await pageRoot.locator('#juxTab_settings').click();
        await expect(refresh).toBeHidden();
        await save.click();
        await expect(refresh).toBeVisible();
        await expect(refresh).toBeFocused();
        await refresh.fill('24');

        // 9 pages (at most 5) in a Discover card folded again, saved from Settings.
        await pageRoot.locator('#juxTab_sections').click();
        const card = () => pageRoot.locator('.jux-admin-widget-card', { has: page.locator('.jux-discover-pages') });
        await card().locator('.jux-admin-btn-toggle').click();
        await card().locator('.jux-discover-pages').fill('9');
        await card().locator('.jux-admin-btn-toggle').click();
        await expect(card()).toHaveClass(/jux-admin-widget-card-collapsed/);
        await pageRoot.locator('#juxTab_settings').click();
        await save.click();
        const pages = card().locator('.jux-discover-pages');
        await expect(pages).toBeVisible();
        await expect(pages).toBeFocused();
        await expect(pages).toHaveValue('9');

        expect(posts).toEqual([]);
    } finally {
        await pluginConfiguration(token, saved);
    }
});

// A-13: a second URL of the admin page (here with a query the router keeps) makes jellyfin-web build a second
// view while the first stays hidden in the document. The page must fill and save the view on screen, not the
// first one, whose element ids it shares.
test('A-13: a second URL of the admin page fills and saves its own view', async ({ page }) => {
    const token = await adminToken();
    const saved = await pluginConfiguration(token);
    const errors = watchJuxErrors(page);
    try {
        await login(page, env.admin);
        await page.goto(`${env.serverUrl}/web/#/configurationpage?name=JellyUX%20Homepage`);
        await expect(page.locator('#juxHomepageConfigPage:visible .jux-admin-widget-card')).toHaveCount(saved.Widgets.length);

        await page.goto(`${env.serverUrl}/web/#/configurationpage?name=JellyUX%20Homepage&tab=sections`);
        await expect(page.locator('#juxHomepageConfigPage')).toHaveCount(2);
        const pageRoot = page.locator('#juxHomepageConfigPage:visible');
        await expect(pageRoot).toHaveCount(1);
        await expect(pageRoot.locator('.jux-admin-widget-card')).toHaveCount(saved.Widgets.length);

        // Turn the first section off in the view on screen and save from it.
        await pageRoot.locator('#juxTab_sections').click();
        const first = pageRoot.locator('.jux-admin-widget-card').first();
        const toggle = first.locator('.jux-admin-enabled');
        const wasEnabled = await toggle.isChecked();
        await toggle.setChecked(!wasEnabled, { force: true });
        await pageRoot.locator('.jux-admin-actions .button-submit').click();
        const firstType = saved.Widgets.slice().sort((a, b) => a.Order - b.Order)[0].WidgetType;
        await expect.poll(async () => (await pluginConfiguration(token)).Widgets.find((w) => w.WidgetType === firstType).Enabled)
            .toBe(!wasEnabled);
        expect(errors).toEqual([]);
    } finally {
        await pluginConfiguration(token, saved);
    }
});
