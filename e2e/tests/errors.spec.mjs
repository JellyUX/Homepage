import { test, expect } from '@playwright/test';
import { env, TABS, login, openTab } from '../helpers.mjs';

// TODO_V4.md 5.2 (F-40): under an outage a tab or a home section says it failed and offers a retry,
// never "empty", and no promise rejection is left unhandled. The outage is simulated by aborting
// requests, so the shared server of the job stays up.

/**
 * Records unhandled promise rejections of the page in window.__juxRejections. Jellyfin's own tab
 * switch rejects once per click on a tab index it has no controller for ("Cannot find module './'",
 * main bundle), with or without an outage (measured on 10.11.10): that one native rejection is left
 * out, every other one fails the test.
 */
async function watchRejections(page) {
    await page.addInitScript(() => {
        window.__juxRejections = [];
        window.addEventListener('unhandledrejection', (event) => {
            const reason = String(event.reason && (event.reason.stack || event.reason));
            // The native tabs controller choking on a JellyUX tab index, a native rejection with no effect:
            // Jellyfin 10.11 finds no tab module for it, the hidden legacy tab bar of the Jellyfin 12 Modern
            // home has no button at that index when the address names it (D-13, DR-025).
            const nativeTabIndex = reason.startsWith("Error: Cannot find module './'") ||
                (reason.startsWith("TypeError: Cannot read properties of undefined (reading 'classList')") &&
                    reason.includes('parentWithClass') && reason.includes('selectedIndex'));
            if (!nativeTabIndex) {
                window.__juxRejections.push(reason);
            }
        });
    });
}

// JellyUX API calls only: the client assets stay loadable.
const juxApi = (url) => url.pathname.includes('/JuxHomepage/') && !/\.(js|css)$/.test(url.pathname);

test('every JellyUX tab shows an error with a retry when the server stops answering (F-40)', async ({ page }) => {
    await watchRejections(page);
    await login(page, env.admin);
    await page.locator('.jux-widget-section').first().waitFor();

    await page.route(juxApi, (route) => route.abort('connectionrefused'));
    for (const tab of TABS) {
        await openTab(page, tab);
        const pane = page.locator(`#${tab.id}`);
        await expect(pane.locator('.jux-error-state')).toBeVisible();
        await expect(pane.locator('.jux-error-retry')).toBeVisible();
        await expect(pane.locator('.jux-empty-state')).toHaveCount(0);
    }

    await page.unroute(juxApi);
    const statistics = page.locator('#jux-tab-statistics');
    await statistics.locator('.jux-error-retry').click();
    await expect(statistics.locator('.jux-stat-card').first()).toBeVisible();

    expect(await page.evaluate(() => window.__juxRejections)).toEqual([]);
});

test('a home section whose request fails keeps its title and shows an error with a retry (F-40)', async ({ page }) => {
    await watchRejections(page);
    const nextUp = (url) => url.pathname.endsWith('/Shows/NextUp');
    await page.route(nextUp, (route) => route.abort('connectionrefused'));
    await login(page, env.admin);

    const section = page.locator('.jux-widget-jux-native-next-up');
    await expect(section).toBeVisible();
    await expect(section.locator('.sectionTitle')).toBeVisible();
    await expect(section.locator('.jux-error-state-compact .jux-error-retry')).toBeVisible();

    await page.unroute(nextUp);
    await section.locator('.jux-error-retry').click();
    await expect(section.locator('.jux-error-state')).toHaveCount(0);

    expect(await page.evaluate(() => window.__juxRejections)).toEqual([]);
});
