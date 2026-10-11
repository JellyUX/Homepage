import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import juxHomepage from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-homepage.js';

const { _escHtml, _buildSeeAllHref, _fallback, _isHomePage, _hasMorePages, _setupLazyLoader } = juxHomepage;

describe('_escHtml', () => {
    it('escapes ampersands, angle brackets, and quotes', () => {
        expect(_escHtml('<b>Tom & "Jerry"</b>')).toBe('&lt;b&gt;Tom &amp; &quot;Jerry&quot;&lt;/b&gt;');
    });

    it('returns an empty string for empty or nullish input', () => {
        expect(_escHtml('')).toBe('');
        expect(_escHtml(null)).toBe('');
        expect(_escHtml(undefined)).toBe('');
    });
});

describe('_buildSeeAllHref', () => {
    it('resolves a known route to its Jellyfin hash URL', () => {
        expect(_buildSeeAllHref('movies')).toBe('#/movies.html');
        expect(_buildSeeAllHref('tvshows')).toBe('#/tv.html');
    });

    it('returns null for an unknown route', () => {
        expect(_buildSeeAllHref('not-a-real-route')).toBeNull();
    });
});

describe('_fallback', () => {
    // _fallback reads window.JUXHomepage.originalLoadSections (set once at module load, and later
    // overwritten by config.js's own hook), not a property on the `self` argument -- `self` is only
    // forwarded as the `this` context via .call(self, ...).
    const originalHook = window.JUXHomepage.originalLoadSections;

    afterEach(() => {
        window.JUXHomepage.originalLoadSections = originalHook;
    });

    it('delegates to originalLoadSections when it is a function', () => {
        const calls = [];
        window.JUXHomepage.originalLoadSections = function (elem, apiClient, user, userSettings) {
            calls.push([this, elem, apiClient, user, userSettings]);
            return 'delegated';
        };
        const self = { marker: 'self' };

        const result = _fallback(self, 'elem', 'apiClient', 'user', 'userSettings');

        expect(result).toBe('delegated');
        expect(calls).toEqual([[self, 'elem', 'apiClient', 'user', 'userSettings']]);
    });

    it('does nothing and does not throw when originalLoadSections is not a function', () => {
        window.JUXHomepage.originalLoadSections = null;

        expect(() => _fallback({}, 'elem', 'apiClient', 'user', 'userSettings')).not.toThrow();
        expect(_fallback({}, 'elem', 'apiClient', 'user', 'userSettings')).toBeUndefined();
    });
});

describe('_isHomePage', () => {
    beforeEach(() => {
        document.body.innerHTML = '';
    });

    it('recognizes the home route via the URL hash', () => {
        window.location.hash = '#/home.html';

        expect(_isHomePage()).toBe(true);
    });

    it('returns false for an unrelated route with no home-page DOM markers', () => {
        window.location.hash = '#/movies.html';

        expect(_isHomePage()).toBe(false);
    });

    it('recognizes the home page via DOM markers even without a home-like hash', () => {
        window.location.hash = '#/';
        document.body.innerHTML = '<div class="sections"></div><div class="homePage"></div>';

        expect(_isHomePage()).toBe(true);
    });
});

describe('_hasMorePages', () => {
    it('returns true only when the response announces a later page', () => {
        expect(_hasMorePages({ Sections: [{}], HasMore: true })).toBe(true);
        expect(_hasMorePages({ Sections: [{}], HasMore: false })).toBe(false);
        expect(_hasMorePages(null)).toBe(false);
    });
});

describe('_setupLazyLoader (F-10)', () => {
    let intersect;
    const originalObserver = globalThis.IntersectionObserver;

    beforeEach(() => {
        globalThis.IntersectionObserver = class {
            constructor(callback) { intersect = () => callback([{ isIntersecting: true }]); }
            observe() {}
            disconnect() { intersect = () => {}; }
        };
    });

    afterEach(() => {
        globalThis.IntersectionObserver = originalObserver;
    });

    const flush = () => new Promise(resolve => setTimeout(resolve, 0));

    it('sends no further page request once a page reports HasMore false', async () => {
        const elem = document.createElement('div');
        elem.innerHTML = '<div class="jux-widget-section"></div>';
        elem.dataset.juxHasMore = 'true';
        const apiClient = {
            getUrl: (path, query) => path + '?page=' + query.page,
            getJSON: vi.fn().mockResolvedValue({ Sections: [], HasMore: false })
        };

        _setupLazyLoader(elem, apiClient, 'user1', {});
        for (let i = 0; i < 3; i++) {
            intersect();
            await flush();
        }

        expect(apiClient.getJSON).toHaveBeenCalledTimes(1);
        expect(apiClient.getJSON.mock.calls[0][0]).toContain('page=1');
        expect(elem.querySelector('.jux-lazy-sentinel')).toBeNull();
    });
});
