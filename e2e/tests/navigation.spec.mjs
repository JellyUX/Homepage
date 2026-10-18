import { test, expect } from '@playwright/test';
import { env, login, apiGet, trackJuxRequests } from '../helpers.mjs';

async function go(page, hash) {
    await page.evaluate((h) => { location.hash = h; }, hash);
}

test('a library shorter than one page stops querying once it is complete (FIAB-01)', async ({ page }) => {
    test.fixme(true, 'FIAB-01, phase 04: the infinite scroll re-arms at the end of the list, 280 library queries in 5 s on HEAD.');
    // The bench library holds 4 movies, all on the first native page: once the list is shown, the
    // infinite scroll has nothing left to read.
    const requests = trackJuxRequests(page);
    await login(page, env.admin);
    await page.locator('.jux-widget-jux-native-recently-added-movies .sectionTitleContainer a').click();
    await expect(page).toHaveURL(/collectionType=movies/);
    await page.evaluate(() => { location.hash = location.hash.replace('tab=1', 'tab=0'); });
    await expect(page.locator('.libraryPage:not(.hide) #moviesTab .card')).toHaveCount(env.movies.length);
    await page.waitForTimeout(2000);

    const listed = requests.mark();
    await page.waitForTimeout(5000);

    expect(requests.count(/\/Users\/[0-9a-f-]+\/Items$/i, listed)).toBe(0);
});

test('the breadcrumb shows again on a detail page reopened from the home page (FIAB-09)', async ({ page }) => {
    test.fixme(true, 'FIAB-09, phase 04: the breadcrumb keeps the item id when hidden, so it stays hidden on X -> home -> X.');
    await login(page, env.admin);
    await page.locator('.jux-widget-section').first().waitFor();
    const movies = await apiGet(env.admin, `/Items?recursive=true&includeItemTypes=Movie&userId=${env.admin.id}`);
    const detail = `#/details?id=${movies.Items[0].Id}`;
    const breadcrumb = page.locator('.jux-breadcrumb');

    await go(page, detail);
    await expect(breadcrumb).toBeVisible();
    await go(page, '#/home');
    await expect(breadcrumb).toBeHidden();
    await go(page, detail);

    await expect(breadcrumb).toBeVisible();
});
