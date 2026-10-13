import { test, expect } from '@playwright/test';
import { env, TABS, login } from '../helpers.mjs';

// TODO_V4.md 7.2 (F-45): in the native light theme, the outlined JellyUX components keep a visible
// edge: the border (or the unwatched progress chunk, or the spinner track) reaches a contrast of at
// least 1.3 against the page (audit_v3/07-ux-ui-live.md E7-05; hardcoded white outlines gave ~1.0).

const MIN_EDGE_CONTRAST = 1.3;

/**
 * Sets the signed-in user's web client theme. Jellyfin Web keeps it in this browser only
 * (userSettings.theme() stores "<userId>-appTheme" in localStorage, measured on 10.11.10), so the
 * isolated test context leaves nothing to restore on the server.
 */
function setTheme(page, theme) {
    return page.evaluate((value) => {
        localStorage.setItem(window.ApiClient.getCurrentUserId() + '-appTheme', value);
    }, theme);
}

/** Contrast of each selector's border (or background for the progress chunk) against the page behind it. */
function edgeContrasts(pane) {
    return pane.evaluate((root) => {
        const parse = (s) => {
            const m = s.match(/rgba?\(([^)]+)\)/);
            const p = m ? m[1].split(/[ ,/]+/).filter(Boolean).map(Number) : [0, 0, 0, 0];
            return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
        };
        const over = (c, bg) => ({ r: c.r * c.a + bg.r * (1 - c.a), g: c.g * c.a + bg.g * (1 - c.a), b: c.b * c.a + bg.b * (1 - c.a), a: 1 });
        const lum = (c) => {
            const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
        };
        const ratio = (a, b) => (Math.max(lum(a), lum(b)) + 0.05) / (Math.min(lum(a), lum(b)) + 0.05);
        // The page behind the tabs: the root background under the native backdrop veil.
        let page = parse(getComputedStyle(document.documentElement).backgroundColor);
        const veil = document.querySelector('.backgroundContainer');
        if (veil) { page = over(parse(getComputedStyle(veil).backgroundColor), page); }

        // The bench has no series in progress (daily cache): a real progress bar and spinner, built by
        // the shared helpers, are measured under the real styles, then removed.
        const probe = document.createElement('div');
        probe.innerHTML = window.JuxUI.buildProgressBar(1, 3) + '<div class="jux-spinner"></div>';
        root.appendChild(probe);

        const result = {};
        for (const [name, selector, prop] of [
            ['sortButton', '.jux-sort-button', 'borderTopColor'],
            ['statCard', '.jux-stat-card', 'borderTopColor'],
            ['progressChunk', '.jux-progress-chunk.unwatched', 'backgroundColor'],
            ['spinnerTrack', '.jux-spinner', 'borderRightColor']
        ]) {
            const el = root.querySelector(selector);
            if (el) { result[name] = ratio(over(parse(getComputedStyle(el)[prop]), page), page); }
        }
        probe.remove();
        return result;
    });
}

test('the outlined JellyUX components stay visible in the light theme (F-45)', async ({ page }) => {
    await login(page, env.admin);
    await setTheme(page, 'light');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    await page.locator('.jux-widget-section').first().waitFor();

    const seen = new Set();
    for (const tab of TABS) {
        await page.locator(`.emby-tab-button[data-index="${tab.index}"]`).click();
        const pane = page.locator(`#${tab.id}`);
        await pane.locator('.jux-sort-button, .jux-stat-card').first().waitFor();
        await page.waitForTimeout(500);
        const contrasts = await edgeContrasts(pane);
        for (const [name, value] of Object.entries(contrasts)) {
            seen.add(name);
            expect(value, `${tab.id} ${name}`).toBeGreaterThanOrEqual(MIN_EDGE_CONTRAST);
        }
    }
    expect([...seen].sort()).toEqual(['progressChunk', 'sortButton', 'spinnerTrack', 'statCard']);
});
