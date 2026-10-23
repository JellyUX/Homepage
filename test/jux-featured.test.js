import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
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
        container.querySelector('.jux-featured').dispatchEvent(new FocusEvent('focusin'));
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

    it('stops its timer and observer once destroyed or removed from the page', () => {
        render(items(4));
        observers[0].show(1);
        container.remove();
        vi.advanceTimersByTime(INTERVAL_MS * 3);
        expect(container._juxFeatured).toBeNull();
        expect(observers[0].disconnected).toBe(true);
    });
});
