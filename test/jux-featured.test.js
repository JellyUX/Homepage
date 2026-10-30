import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import featured from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-featured.js';

const { slidesOf, html, attach, destroy, backdropWidth, decodeBlurhash, INTERVAL_MS, MAX_SLIDES } = featured;

const item = (n, extra = {}) => ({
    Id: 'id' + n,
    ServerId: 'srv',
    Name: 'Movie ' + n,
    Type: 'Movie',
    MediaType: 'Video',
    ProductionYear: 2020 + n,
    RunTimeTicks: 8040 * 10000000,
    CommunityRating: 8.25,
    Overview: 'Overview ' + n,
    BackdropImageTags: ['b' + n],
    ImageTags: { Primary: 'p' + n },
    ...extra
});

const items = (count) => Array.from({ length: count }, (_, i) => item(i));

const apiClient = { getScaledImageUrl: vi.fn((id, o) => `/Items/${id}/Images/${o.type}?tag=${o.tag}&maxWidth=${o.maxWidth}`) };

let container;
let observers;

function render(list, descriptor = { DisplayName: 'Top Rated' }) {
    container = document.createElement('div');
    container.innerHTML = html(list, descriptor) || '';
    document.body.appendChild(container);
    return attach(container, apiClient);
}

beforeEach(() => {
    vi.useFakeTimers();
    observers = [];
    window.IntersectionObserver = class {
        constructor(callback) { this.callback = callback; observers.push(this); }
        observe() {}
        disconnect() { this.disconnected = true; }
        show(ratio) { this.callback([{ intersectionRatio: ratio }]); }
    };
    window.matchMedia = vi.fn(() => ({ matches: false }));
    document.documentElement.lang = 'en';
    apiClient.getScaledImageUrl.mockClear();
});

afterEach(() => {
    destroy(container);
    container?.remove();
    vi.useRealTimers();
    delete window.IntersectionObserver;
});

const active = () => container.querySelector('.jux-featured-slide.jux-featured-active').dataset.index;

describe('KTW-01.a rendering', () => {
    it('shows at most 8 slides, only items with a backdrop, and falls back under 3', () => {
        const mixed = [...items(10), item(99, { BackdropImageTags: [] })];
        expect(slidesOf(mixed)).toHaveLength(MAX_SLIDES);
        expect(slidesOf(mixed).some((i) => i.Id === 'id99')).toBe(false);
        expect(html([...items(2), item(5, { BackdropImageTags: [] })], {})).toBeNull();
        expect(html(items(3), {})).toContain('data-index="2"');
    });

    it('writes the native item attributes the container acts on (H-05), escapes every text', () => {
        render([item(0, { Name: '<b>x</b>', Overview: 'a & b' }), item(1), item(2)]);
        const slide = container.querySelector('.jux-featured-slide');

        expect(slide.dataset).toMatchObject({ id: 'id0', serverid: 'srv', type: 'Movie', mediatype: 'Video', isfolder: 'false', positionticks: '0' });
        expect([...slide.querySelectorAll('.itemAction')].map((b) => b.dataset.action)).toEqual(['resume', 'link']);
        expect(slide.querySelector('.jux-featured-title').innerHTML).toBe('&lt;b&gt;x&lt;/b&gt;');
        expect(slide.querySelector('.jux-featured-text').textContent).toBe('a & b');
        expect(container.querySelector('.jux-featured').getAttribute('aria-label')).toBe('Top Rated');
    });

    it('prefers the logo and the tagline, and writes one line of year, runtime and rating', () => {
        render([item(0, { ImageTags: { Logo: 'L' }, Taglines: ['Tagline'] }), item(1), item(2)]);
        const slide = container.querySelector('.jux-featured-slide');

        expect(slide.querySelector('.jux-featured-logo').alt).toBe('');
        expect(slide.querySelector('.jux-featured-heading').textContent).toBe('Movie 0');
        expect(slide.querySelector('.jux-featured-text').textContent).toBe('Tagline');
        expect(slide.querySelector('.jux-featured-meta').textContent).toBe('20202h 14m8.3');
        expect(slide.querySelector('.jux-featured-rating').getAttribute('aria-label')).toBe('Rated 8.3');
    });

    it('names every control from the language files', () => {
        document.documentElement.lang = 'fr';
        render(items(3));
        const labels = [...container.querySelectorAll('.jux-featured-control')].map((b) => b.getAttribute('aria-label'));

        expect(labels).toEqual(['Diapositive précédente', 'Mettre le défilement en pause', 'Diapositive suivante']);
        expect(container.querySelector('.jux-featured-slide').getAttribute('aria-label')).toBe('1 sur 3');
        expect(container.querySelector('.jux-featured-button').textContent).toBe('Lire');
    });

    it('leaves a container without a banner alone', () => {
        container = document.createElement('div');
        container.innerHTML = '<div class="card"></div>';
        expect(attach(container, apiClient)).toBeNull();
    });
});

describe('KTW-01.b images', () => {
    it('rounds the backdrop up to 960, 1280 or 1920 pixels', () => {
        expect(backdropWidth(500, 1)).toBe(960);
        expect(backdropWidth(1000, 1)).toBe(1280);
        expect(backdropWidth(1000, 2)).toBe(1920);
        expect(backdropWidth(1600, 1)).toBe(1920);
    });

    it('loads the backdrops of the active slide and the next one only', () => {
        render(items(6));
        const loaded = () => [...container.querySelectorAll('.jux-featured-backdrop')].filter((img) => img.getAttribute('src')).map((img) => img.closest('.jux-featured-slide').dataset.index);

        expect(loaded()).toEqual(['0', '1']);
        container.querySelector('.jux-featured-next').click();
        expect(loaded()).toEqual(['0', '1', '2']);
    });

    // R11 constat 4: a logo hidden by the phone layout loads once its slide comes up wider.
    it('loads a logo only outside the phone layout, on any later render', () => {
        let phone = true;
        window.matchMedia = vi.fn((q) => ({ matches: q.includes('max-width') ? phone : false }));
        render(items(3).map((i) => ({ ...i, ImageTags: { Logo: 'L' + i.Id } })));
        const logos = () => [...container.querySelectorAll('.jux-featured-logo')].map((l) => !!l.getAttribute('src'));
        expect(logos()).toEqual([false, false, false]);

        phone = false;
        container.querySelector('.jux-featured-next').click();
        expect(logos()).toEqual([false, true, true]);
    });

    // FIAB-03: a logo the server cannot send gives its place back to the text title, and is not asked again.
    it('shows the title again when its logo fails to load', () => {
        window.matchMedia = vi.fn(() => ({ matches: false }));
        render(items(3).map((i) => ({ ...i, ImageTags: { Logo: 'L' + i.Id } })));
        const heading = container.querySelector('.jux-featured-slide .jux-featured-heading');
        expect(heading.classList.contains('jux-featured-has-logo')).toBe(true);

        heading.querySelector('.jux-featured-logo').dispatchEvent(new Event('error'));
        expect(heading.querySelector('.jux-featured-logo')).toBeNull();
        expect(heading.classList.contains('jux-featured-has-logo')).toBe(false);
        expect(heading.querySelector('.jux-featured-title').textContent).not.toBe('');
    });

    it('decodes a blurhash into opaque pixels and refuses a malformed one', () => {
        const pixels = decodeBlurhash('LEHV6nWB2yk8pyo0adR*.7kCMdnj', 4, 3);
        expect(pixels).toHaveLength(4 * 3 * 4);
        expect(pixels[3]).toBe(255);
        expect(decodeBlurhash('LEHV6n', 4, 3)).toBeNull();
    });
});

describe('KTW-01.c motion and accessibility', () => {
    it('rotates on its own once half visible, never while the tab is hidden', () => {
        render(items(4));
        vi.advanceTimersByTime(INTERVAL_MS * 2);
        expect(active()).toBe('0');

        observers[0].show(1);
        vi.advanceTimersByTime(INTERVAL_MS);
        expect(active()).toBe('1');

        Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });
        document.dispatchEvent(new Event('visibilitychange'));
        vi.advanceTimersByTime(INTERVAL_MS * 3);
        expect(active()).toBe('1');
        delete document.hidden;

        observers[0].show(0.2);
        vi.advanceTimersByTime(INTERVAL_MS * 3);
        expect(active()).toBe('1');
    });

    it('stops under prefers-reduced-motion until the viewer presses play', () => {
        window.matchMedia = vi.fn(() => ({ matches: true }));
        render(items(4));
        observers[0].show(1);
        vi.advanceTimersByTime(INTERVAL_MS * 3);
        expect(active()).toBe('0');

        const toggle = container.querySelector('.jux-featured-toggle');
        expect(toggle.getAttribute('aria-label')).toBe('Play the slides');
        toggle.click();
        vi.advanceTimersByTime(INTERVAL_MS);
        expect(active()).toBe('1');
    });

    it('pauses on the visible button and while the focus is inside', () => {
        render(items(4));
        observers[0].show(1);
        container.querySelector('.jux-featured-toggle').click();
        vi.advanceTimersByTime(INTERVAL_MS * 2);
        expect(active()).toBe('0');
        expect(container.querySelector('.jux-featured-stage').getAttribute('aria-live')).toBe('polite');

        container.querySelector('.jux-featured-toggle').click();
        container.querySelector('.jux-featured-next').focus();
        vi.advanceTimersByTime(INTERVAL_MS * 2);
        expect(active()).toBe('0');
    });

    it('moves with the arrow keys and the strip, hides and inerts the other slides', () => {
        render(items(5));
        const banner = container.querySelector('.jux-featured');
        banner.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
        expect(active()).toBe('4');
        banner.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        expect(active()).toBe('0');

        container.querySelector('.jux-featured-thumb[data-index="2"]').click();
        expect(active()).toBe('2');
        const hidden = container.querySelector('.jux-featured-slide[data-index="0"]');
        expect(hidden.getAttribute('aria-hidden')).toBe('true');
        expect(hidden.hasAttribute('inert')).toBe(true);
        expect(container.querySelector('.jux-featured-thumb.jux-featured-active').getAttribute('aria-current')).toBe('true');
    });

    // R11 constat 1: a banner that never rotates (reduced motion) is released once its home is gone.
    it('releases a stopped banner left by its home page on the next attach', () => {
        window.matchMedia = vi.fn((q) => ({ matches: q.includes('reduced-motion') }));
        render(items(4));
        const first = container;
        first.remove();

        render(items(3));

        expect(first._juxFeatured).toBeNull();
        expect(observers[0].disconnected).toBe(true);
    });

    // R11 constat 2: the focus follows to the same control of the new slide or strip.
    it('keeps the keyboard focus in the banner when the slide changes', () => {
        render(items(4));
        const banner = container.querySelector('.jux-featured');
        container.querySelector('.jux-featured-slide[data-index="0"] .jux-featured-button-play').focus();
        banner.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        expect(document.activeElement).toBe(container.querySelector('.jux-featured-slide[data-index="1"] .jux-featured-button-play'));

        container.querySelector('.jux-featured-thumb').focus();
        banner.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        expect(document.activeElement).toBe(container.querySelector('.jux-featured-thumb.jux-featured-active'));
        expect(document.activeElement.dataset.index).toBe('2');
    });

    // R11 constat 6: a click leaves a focused button behind; only keyboard focus keeps the banner still.
    it('rotates again once the pointer leaves after a click', () => {
        render(items(4));
        observers[0].show(1);
        const banner = container.querySelector('.jux-featured');
        const matches = Element.prototype.matches;
        const spy = vi.spyOn(Element.prototype, 'matches').mockImplementation(function (selector) {
            return selector === ':focus-visible' || selector === ':hover' ? false : matches.call(this, selector);
        });
        banner.dispatchEvent(new MouseEvent('mouseenter'));
        container.querySelector('.jux-featured-next').focus();
        container.querySelector('.jux-featured-next').click();
        banner.dispatchEvent(new MouseEvent('mouseleave'));
        vi.advanceTimersByTime(INTERVAL_MS);
        spy.mockRestore();

        expect(active()).toBe('2');
    });

    it('stops its timer and observer once destroyed or removed from the page', () => {
        render(items(4));
        observers[0].show(1);
        container.remove();
        vi.advanceTimersByTime(INTERVAL_MS * 3);
        expect(container._juxFeatured).toBeNull();
        expect(observers[0].disconnected).toBe(true);
    });
});

// d119d74: over the image the banner writes in white whatever the theme; the theme's text colors only
// apply in the phone layout, where the text sits on the page.
describe('theme inks', () => {
    it('reads no theme text color outside the phone layout', () => {
        const css = readFileSync(resolve(__dirname, '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-homepage.css'), 'utf8');
        const featuredCss = css.slice(css.indexOf('/* Featured section (KTW-01'));
        const phone = featuredCss.indexOf('@media (max-width: 50em)');
        const outside = featuredCss.slice(0, phone) + featuredCss.slice(featuredCss.indexOf('@media (prefers-reduced-motion', phone));

        expect(outside).not.toMatch(/--jf-palette-text-/);
        expect(featuredCss.slice(phone)).toMatch(/--jux-featured-ink: var\(--jf-palette-text-primary\)/);
    });
});
