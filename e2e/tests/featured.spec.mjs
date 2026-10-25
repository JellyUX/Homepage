import { test, expect } from '@playwright/test';
import { env, login, watchJuxErrors, adminToken, pluginConfiguration, isPainted } from '../helpers.mjs';

// KTW-01: a featured section, alone on the home page so its place does not depend on the others. The
// configuration is restored after each test. Each movie of the bench has a backdrop (e2e/setup.mjs).
async function withOnlyRow(row, run) {
    const token = await adminToken();
    const saved = await pluginConfiguration(token);
    await pluginConfiguration(token, { ...saved, Widgets: [...saved.Widgets.map((w) => ({ ...w, Enabled: false })), row] });
    try {
        await run();
    } finally {
        await pluginConfiguration(token, saved);
    }
}

const featured = (WidgetType, Key, Value) => ({
    WidgetType,
    Enabled: true,
    Order: 0,
    MinItems: 1,
    MaxItems: 20,
    ViewMode: 'Landscape',
    SortBy: 'SortName',
    SortOrder: 'Ascending',
    Featured: true,
    FeaturedMinRating: 0,
    ExtraParams: [{ Key, Value }]
});

/** Records the backdrop requests of a page: the slides' and the strip's (maxWidth 320). */
function trackBackdrops(page) {
    const widths = [];
    page.on('request', (request) => {
        const url = new URL(request.url());
        if (/\/Items\/[0-9a-f-]+\/Images\/Backdrop/i.test(url.pathname)) {
            widths.push(Number(url.searchParams.get('maxWidth') || url.searchParams.get('maxwidth')));
        }
    });
    return {
        slides: () => widths.filter((w) => w !== 320),
        thumbs: () => widths.filter((w) => w === 320)
    };
}

test('KTW-01.a banner: a featured section shows its movies, loads two backdrops, moves by keyboard and pauses', async ({ page }, testInfo) => {
    await withOnlyRow(featured('jux.local.top-rated', 'itemTypes', 'Movie'), async () => {
        const errors = watchJuxErrors(page);
        const backdrops = trackBackdrops(page);
        await login(page, env.admin);

        const banner = page.locator('.jux-featured');
        await expect(banner).toBeVisible();
        await expect(banner.locator('.jux-featured-slide')).toHaveCount(4);
        await expect(banner.locator('.jux-featured-slide.jux-featured-active .jux-featured-backdrop.jux-featured-loaded')).toHaveCount(1);

        // KTW-01.b image budget: the active slide and the next one only, at a server size, and at most the
        // four strip thumbnails (none on a phone, where the strip is hidden).
        expect(backdrops.slides()).toHaveLength(2);
        expect(backdrops.slides().every((w) => [960, 1280, 1920].includes(w))).toBe(true);
        expect(backdrops.thumbs().length).toBeLessThanOrEqual(testInfo.project.name === 'mobile' ? 0 : 4);

        // KTW-01.c: the arrow keys move the banner, the visible button pauses it, the other slides are inert.
        await banner.locator('.jux-featured-next').focus();
        await page.keyboard.press('ArrowRight');
        await expect(banner.locator('.jux-featured-slide.jux-featured-active')).toHaveAttribute('data-index', '1');
        await expect(banner.locator('.jux-featured-slide[data-index="0"]')).toHaveAttribute('inert', '');
        const toggle = banner.locator('.jux-featured-toggle');
        await toggle.click();
        await expect(toggle).toHaveAttribute('aria-label', 'Play the slides');

        // H-05: More info is a native item action, handled by the container without JellyUX code.
        await banner.locator('.jux-featured-slide.jux-featured-active .itemAction[data-action="link"]').click();
        await expect(page).toHaveURL(/#\/details\?id=/);

        if (testInfo.project.name === 'mobile') {
            // Back on the home page the banner is rebuilt, while Jellyfin may keep the previous home page
            // hidden with its own banner: measure the visible one, once its backdrop is in.
            // Going back while the details page is still opening leaves every page hidden.
            await expect(page.locator('.itemDetailPage:not(.hide)')).toHaveCount(1);
            await page.goBack();
            const shown = page.locator('.jux-featured-slide.jux-featured-active:visible');
            await expect(shown.locator('.jux-featured-backdrop.jux-featured-loaded')).toBeVisible();
            await shown.scrollIntoViewIfNeeded();
            const media = await shown.locator('.jux-featured-media').boundingBox();
            const copy = await shown.locator('.jux-featured-copy').boundingBox();
            expect(Math.abs(media.width / media.height - 16 / 9)).toBeLessThan(0.02);
            expect(copy.y).toBeGreaterThanOrEqual(media.y + media.height - 1);
            expect(await isPainted(shown.locator('.jux-featured-copy'))).toBe(true);
            await testInfo.attach('featured-390', { body: await page.locator('.jux-featured:visible').screenshot(), contentType: 'image/png' });
        }
        expect(errors).toEqual([]);
    });
});

// The year 2001 holds one movie of the bench: one slide, so its card instead of a banner.
test('KTW-01.a: a featured section with fewer than 3 backdrops shows its cards instead', async ({ page }) => {
    await withOnlyRow(featured('jux.admin.year', 'value', '2001'), async () => {
        await login(page, env.admin);

        await expect(page.locator('.jux-featured-host .jux-featured-cards .card').first()).toBeVisible();
        await expect(page.locator('.jux-featured')).toHaveCount(0);
    });
});
