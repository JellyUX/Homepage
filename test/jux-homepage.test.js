import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import juxHomepage from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-homepage.js';

const { _escHtml, _buildSeeAllHref, _fallback, _isHomePage, _hasMorePages, _setupLazyLoader, _chunkState, _repairStaleChunks, _armChunkGuard } = juxHomepage;

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

describe('_buildSection (F-11)', () => {
    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('places sections in the order they are built, without a CSS order the container ignores', async () => {
        // The server returns descriptors sorted by Order (WidgetLayoutResolver); the native
        // .homeSectionsContainer is not a flex container in 10.11.10, so style.order had no effect.
        const elem = document.createElement('div');
        const descriptor = (type, order) => ({ WidgetType: type, Order: order, DisplayName: type, ViewMode: 'Backdrop' });

        await juxHomepage._buildSection(elem, {}, 'u', null, descriptor('jux.native.next-up', 10));
        await juxHomepage._buildSection(elem, {}, 'u', null, descriptor('jux.native.my-media', 40));

        const sections = [...elem.querySelectorAll('.jux-widget-section')];
        expect(sections.map((s) => s.classList.contains('jux-widget-jux-native-next-up'))).toEqual([true, false]);
        expect(sections.map((s) => s.style.order)).toEqual(['', '']);
    });
});

// A window stand-in for the stale chunk guard: session storage, resource timing, fetch and reload
// are the only browser services it touches.
function fakeWindow({ storage = new Map(), noStorage = false, resources = [], patched = false, tabs = false } = {}) {
    const sessionStorage = noStorage
        ? { getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); }, removeItem() { throw new Error('denied'); } }
        : {
            getItem: (k) => (storage.has(k) ? storage.get(k) : null),
            setItem: (k, v) => storage.set(k, v),
            removeItem: (k) => storage.delete(k)
        };
    return {
        storage,
        sessionStorage,
        performance: { getEntriesByType: () => resources },
        fetch: vi.fn(() => Promise.resolve({ ok: true })),
        location: { reload: vi.fn() },
        setInterval: (fn, ms) => setInterval(fn, ms),
        clearInterval: (id) => clearInterval(id),
        JuxLoadSectionsPatched: patched || undefined,
        juxTabInjector: tabs ? {} : undefined
    };
}

function renderHomeView({ sections = 0, juxPanes = true } = {}) {
    const items = '<div class="verticalSection"></div>'.repeat(sections);
    document.body.innerHTML = '<div class="page homePage" id="indexPage">'
        + `<div class="tabContent"><div class="sections">${items}</div></div>`
        + '<div class="tabContent" id="favoritesTab"><div class="sections"></div></div>'
        + (juxPanes ? '<div class="tabContent" id="jux-tab-watchlist"></div>' : '')
        + '</div>';
}

describe('_chunkState', () => {
    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('is undecided away from the home page', () => {
        document.body.innerHTML = '<div class="page" id="moviesPage"></div>';

        expect(_chunkState(fakeWindow(), document)).toBeNull();
    });

    it('is undecided while the home sections are not rendered yet', () => {
        renderHomeView();

        expect(_chunkState(fakeWindow(), document)).toBeNull();
    });

    it('is ok once the patched loadSections has run', () => {
        renderHomeView({ sections: 3 });

        expect(_chunkState(fakeWindow({ patched: true }), document)).toBe('ok');
    });

    it('is stale when the home sections were rendered without the patched loadSections', () => {
        renderHomeView({ sections: 3 });

        expect(_chunkState(fakeWindow(), document)).toBe('stale');
    });

    it('is stale when the Tabs module is on and the home view has no JellyUX pane', () => {
        renderHomeView({ juxPanes: false });

        expect(_chunkState(fakeWindow({ patched: true, tabs: true }), document)).toBe('stale');
    });

    it('ignores the missing JellyUX panes when the Tabs module is off', () => {
        renderHomeView({ sections: 3, juxPanes: false });

        expect(_chunkState(fakeWindow({ patched: true }), document)).toBe('ok');
    });
});

describe('_repairStaleChunks', () => {
    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('refetches the chunks served from cache, then reloads once', async () => {
        const win = fakeWindow({
            resources: [
                { name: 'http://h/web/home-html.abc.chunk.js', transferSize: 0 },
                { name: 'http://h/web/42.def.chunk.js?x=1', transferSize: 0 },
                { name: 'http://h/web/7.aaa.chunk.js', transferSize: 1800 },
                { name: 'http://h/JuxHomepage/jux-ui.js?v=1', transferSize: 0 }
            ]
        });

        const outcome = await _repairStaleChunks(win, document);

        expect(outcome).toBe('reload');
        expect(win.fetch.mock.calls).toEqual([
            ['http://h/web/home-html.abc.chunk.js', { cache: 'reload' }],
            ['http://h/web/42.def.chunk.js?x=1', { cache: 'reload' }]
        ]);
        expect(win.location.reload).toHaveBeenCalledTimes(1);
        expect(document.getElementById('jux-chunk-notice').querySelector('button')).toBeNull();
    });

    it('does not reload a second time in the same session and shows a persistent message', async () => {
        const win = fakeWindow({ resources: [{ name: 'http://h/web/home-html.abc.chunk.js', transferSize: 0 }] });
        await _repairStaleChunks(win, document);

        const outcome = await _repairStaleChunks(win, document);

        expect(outcome).toBe('notice');
        expect(win.location.reload).toHaveBeenCalledTimes(1);
        expect(win.fetch).toHaveBeenCalledTimes(1);
        const notice = document.getElementById('jux-chunk-notice');
        expect(notice.textContent).toContain('CDN');
        notice.querySelector('button').click();
        expect(document.getElementById('jux-chunk-notice')).toBeNull();
    });

    it('never reloads without session storage to guard the loop', async () => {
        const win = fakeWindow({ noStorage: true });

        const outcome = await _repairStaleChunks(win, document);

        expect(outcome).toBe('notice');
        expect(win.location.reload).not.toHaveBeenCalled();
    });
});

describe('_armChunkGuard', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
        document.body.innerHTML = '';
    });

    it('clears the reload flag once the patched chunks render the home page', () => {
        const win = fakeWindow({ patched: true });
        win.storage.set('jux-chunk-reload', '1');
        renderHomeView({ sections: 2 });

        _armChunkGuard(win, document);
        vi.advanceTimersByTime(1000);

        expect(win.storage.has('jux-chunk-reload')).toBe(false);
        expect(win.location.reload).not.toHaveBeenCalled();
    });

    it('repairs only once when the home page stays native', async () => {
        const win = fakeWindow();
        renderHomeView({ sections: 2 });

        _armChunkGuard(win, document);
        await vi.advanceTimersByTimeAsync(5000);

        expect(win.location.reload).toHaveBeenCalledTimes(1);
    });

    it('waits for the home page, then stops after its first decision', async () => {
        const win = fakeWindow();
        document.body.innerHTML = '<div class="page" id="moviesPage"></div>';

        _armChunkGuard(win, document);
        await vi.advanceTimersByTimeAsync(3000);
        expect(win.location.reload).not.toHaveBeenCalled();

        renderHomeView({ sections: 2 });
        await vi.advanceTimersByTimeAsync(5000);

        expect(win.location.reload).toHaveBeenCalledTimes(1);
    });
});
