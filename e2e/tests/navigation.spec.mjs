import { test, expect } from '@playwright/test';
import { env, login, apiGet, trackJuxRequests, isPainted } from '../helpers.mjs';

async function go(page, hash) {
    await page.evaluate((h) => { location.hash = h; }, hash);
}

test('a library shorter than one page stops querying once it is complete (FIAB-01)', async ({ page }) => {
    // The bench library holds 12 movies, all on the first native page: once the list is shown, the
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

test('the breadcrumb leaves the header buttons uncovered, at every width (A-10)', async ({ page }) => {
    await login(page, env.admin);
    await page.locator('.jux-widget-section').first().waitFor();
    const movies = await apiGet(env.admin, `/Items?recursive=true&includeItemTypes=Movie&userId=${env.admin.id}`);
    await go(page, `#/details?id=${movies.Items[0].Id}`);
    await expect(page.locator('.jux-breadcrumb')).toBeVisible();

    const covered = await page.evaluate(() => {
        const trail = document.querySelector('.jux-breadcrumb').getBoundingClientRect();
        return [...document.querySelectorAll('.skinHeader .headerRight > *')]
            .filter((button) => button.offsetParent !== null)
            .map((button) => ({ name: button.className, box: button.getBoundingClientRect() }))
            .filter(({ box }) => box.left < trail.right && trail.left < box.right && box.top < trail.bottom && trail.top < box.bottom)
            .map(({ name }) => name);
    });
    // Painted, not only laid out: the native header's paint containment once clipped it.
    expect({ painted: await isPainted(page.locator('.jux-breadcrumb')), covered }).toEqual({ painted: true, covered: [] });
});

// BKL-51: between 800 and 900 px the trail sits in the header bar beside its buttons; a very long title
// must neither cover them nor be clipped. The item's answer is given a long name.
test('the breadcrumb of a very long title leaves the header buttons uncovered at 820 px (BKL-51)', async ({ page }) => {
    await page.setViewportSize({ width: 820, height: 1000 });
    await login(page, env.admin);
    await page.locator('.jux-widget-section').first().waitFor();
    const movies = await apiGet(env.admin, `/Items?recursive=true&includeItemTypes=Movie&userId=${env.admin.id}`);
    const id = movies.Items[0].Id;
    await page.route(new RegExp(`/Items/${id}([?]|$)`), async (route) => {
        const response = await route.fetch();
        const item = await response.json();
        item.Name = 'An Extraordinarily Long Feature Title That Goes On And On Well Past The Width Of A Small Tablet Header';
        await route.fulfill({ response, json: item });
    });
    await go(page, `#/details?id=${id}`);
    await expect(page.locator('.jux-breadcrumb .jux-breadcrumb-current')).toContainText('Extraordinarily');

    const covered = await page.evaluate(() => {
        const trail = document.querySelector('.jux-breadcrumb').getBoundingClientRect();
        return [...document.querySelectorAll('.skinHeader .headerRight > *')]
            .filter((button) => button.offsetParent !== null)
            .map((button) => ({ name: button.className, box: button.getBoundingClientRect() }))
            .filter(({ box }) => box.left < trail.right && trail.left < box.right && box.top < trail.bottom && trail.top < box.bottom)
            .map(({ name }) => name);
    });
    expect({ painted: await isPainted(page.locator('.jux-breadcrumb')), covered }).toEqual({ painted: true, covered: [] });
    expect(await page.evaluate(() => window.scrollX)).toBe(0);
});
