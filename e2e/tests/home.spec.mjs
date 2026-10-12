import { test, expect } from '@playwright/test';
import { env, NATIVE_SECTIONS, TABS, login, loadedSections, apiGet, watchJuxErrors } from '../helpers.mjs';

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
