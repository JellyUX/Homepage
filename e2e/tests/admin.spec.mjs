import { test, expect } from '@playwright/test';
import { env, login, loadedSections, watchJuxErrors } from '../helpers.mjs';

const PLUGIN_ID = '3adf1f1f-1541-4e47-b9e3-34d2d2968af6';
const AUTH = 'MediaBrowser Client="jux-e2e", Device="ci", DeviceId="jux-e2e-admin", Version="1.0.0"';

async function adminToken() {
    const res = await fetch(`${env.serverUrl}/Users/AuthenticateByName`, {
        method: 'POST',
        headers: { Authorization: AUTH, 'Content-Type': 'application/json' },
        body: JSON.stringify({ Username: env.admin.name, Pw: env.admin.password })
    });
    expect(res.ok).toBe(true);
    return (await res.json()).AccessToken;
}

async function pluginConfiguration(token, body) {
    const res = await fetch(`${env.serverUrl}/Plugins/${PLUGIN_ID}/Configuration`, {
        method: body ? 'POST' : 'GET',
        headers: { Authorization: `${AUTH}, Token="${token}"`, 'Content-Type': 'application/json' },
        body: body ? JSON.stringify(body) : undefined
    });
    expect(res.ok).toBe(true);
    return body ? null : res.json();
}

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
    test.fixme(true, 'FIAB-02, phase 05: an unchanged Save drops the Rewards ceremony and the Discover person and genre filters.');
    // Rows whose filters are stored as ids (a Wikidata ceremony, TMDb person and genre) the page shows
    // through labels it looks up: a Save without any change must write them back as they were.
    const token = await adminToken();
    const saved = await pluginConfiguration(token);
    const seeded = structuredClone(saved);
    seeded.Widgets.push(
        { WidgetType: 'jux.connected.rewards', Enabled: true, Order: 900, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait',
            ExtraParams: [{ Key: 'value', Value: '11111111-2222-3333-4444-555555555555' }, { Key: 'ceremonyQid', Value: 'Q19020' }, { Key: 'year', Value: '2024' }] },
        { WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 910, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait',
            ExtraParams: [{ Key: 'value', Value: '66666666-7777-8888-9999-000000000000' }, { Key: 'personIds', Value: '525' }, { Key: 'genreIds', Value: '18' }] });
    const filters = (config) => config.Widgets
        .filter((w) => w.WidgetType === 'jux.connected.rewards' || w.WidgetType === 'jux.connected.discover-movies')
        .map((w) => ({ type: w.WidgetType, params: w.ExtraParams.filter((p) => p.Key !== 'value') }));
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
