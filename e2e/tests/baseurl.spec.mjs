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
