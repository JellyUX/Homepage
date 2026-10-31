import { test, expect } from '@playwright/test';
import { env, NATIVE_SECTIONS, login, loadedSections, watchJuxErrors } from '../helpers.mjs';

test('the home page renders under a BaseUrl, assets included (F-14)', async ({ page }) => {
    expect(new URL(env.serverUrl).pathname).not.toBe('/');
    const assets = [];
    page.on('response', (res) => {
        if (res.url().includes('/JuxHomepage/')) {
            assets.push(new URL(res.url()).pathname);
        }
    });
    const errors = watchJuxErrors(page);

    await login(page, env.admin);

    expect(await loadedSections(page)).toEqual(NATIVE_SECTIONS);
    const basePath = new URL(env.serverUrl).pathname;
    expect(assets.filter((p) => p.endsWith('.js')).length).toBeGreaterThan(0);
    expect(assets.every((p) => p.startsWith(`${basePath}/JuxHomepage/`))).toBe(true);
    expect(errors).toEqual([]);
});

// The admin page modules are imported by a path relative to config.js (./configurationpage?name=..., DR-019):
// under a BaseUrl they must load from it, and the page must render its sections.
test('the admin page loads its modules under a BaseUrl (DR-019)', async ({ page }) => {
    const basePath = new URL(env.serverUrl).pathname;
    const modules = [];
    page.on('response', (res) => {
        if (/configurationpage\?name=juxhomepageconfig/.test(res.url())) {
            modules.push({ path: new URL(res.url()).pathname, status: res.status() });
        }
    });
    const errors = watchJuxErrors(page);

    await login(page, env.admin);
    await page.goto(`${env.serverUrl}/web/#/configurationpage?name=JellyUX%20Homepage`);
    await expect(page.locator('#juxHomepageConfigPage:visible .jux-admin-widget-card').first()).toBeAttached();

    expect(modules.length).toBeGreaterThan(1);
    expect(modules.every((m) => m.status === 200 && m.path === `${basePath}/web/configurationpage`)).toBe(true);
    expect(errors).toEqual([]);
});
