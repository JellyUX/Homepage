import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import juxHomepage from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-homepage.js';

const { _seeAllTarget, _fallback, _isHomePage, _hasMorePages, _setupLazyLoader, _chunkState, _repairStaleChunks, _armChunkGuard, _guardFetchData, _guardGetItemsHtml, _renderHome } = juxHomepage;

describe('_seeAllTarget (F-56)', () => {
    const replies = {
        UserViews: { Items: [{ Id: 'lib-shows', CollectionType: 'tvshows' }, { Id: 'lib-movies', CollectionType: 'movies' }] },
        'Genres/Science%20Fiction': { Id: 'g1' },
        'Studios/Pixar': { Id: 's1' },
        'Persons/Tom%20Hanks': { Id: 'p1' }
    };
    const client = {
        serverId: () => 'srv',
        getUrl: (path) => path,
        getJSON: vi.fn((path) => Promise.resolve(replies[path]))
    };
    const target = (Route, AdditionalData) => _seeAllTarget(client, 'u1', { WidgetType: 'jux.test', Route, AdditionalData });

    beforeEach(() => client.getJSON.mockClear());

    it('links Next Up, a tag and a collection directly, never to the missing #/nextup.html', () => {
        expect(target('nextup')).toEqual({ href: '#/list?type=nextup&serverId=srv' });
        expect(target('tag', 'christmas')).toEqual({ href: '#/list?tag=christmas&serverId=srv' });
        expect(target('item', 'c1')).toEqual({ href: '#/details?id=c1&serverId=srv' });
        expect(client.getJSON).not.toHaveBeenCalled();
    });

    it.each([
        ['movies', undefined, '#/movies?topParentId=lib-movies&collectionType=movies&tab=1'],
        ['tvshows', undefined, '#/tv?topParentId=lib-shows&collectionType=tvshows&tab=1'],
        ['genre', 'Science Fiction', '#/list?genreId=g1&serverId=srv'],
        ['studio', 'Pixar', '#/list?studioId=s1&serverId=srv'],
        ['person', 'Tom Hanks', '#/details?id=p1&serverId=srv']
    ])('resolves %s by id only when the link is clicked', async (route, value, href) => {
        const seeAll = target(route, value);
        expect(client.getJSON).not.toHaveBeenCalled();

        await expect(seeAll.resolve()).resolves.toBe(href);
    });

    it('opens the JellyUX Watchlist tab', async () => {
        const tab = document.createElement('button');
        tab.id = 'jux-tabbtn-watchlist';
        tab.click = vi.fn();
        document.body.appendChild(tab);

        await expect(target('watchlist').resolve()).resolves.toBeNull();
        expect(tab.click).toHaveBeenCalledOnce();
        tab.remove();
    });

    it('gives no link without a value, for a year or for an unknown route', () => {
        expect(target('genre', null)).toBeNull();
        expect(target('year', '2020')).toBeNull();
        expect(target(null)).toBeNull();
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

    it('disconnects the previous loader when Jellyfin reuses the container for a new render (Annexe.a)', () => {
        const disconnected = [];
        globalThis.IntersectionObserver = class {
            constructor() { this.id = disconnected.length; disconnected.push(false); }
            observe() {}
            disconnect() { disconnected[this.id] = true; }
        };
        const elem = document.createElement('div');
        elem.dataset.juxHasMore = 'true';
        const apiClient = { getUrl: (path) => path, getJSON: vi.fn() };

        _setupLazyLoader(elem, apiClient, 'user1', {});
        _setupLazyLoader(elem, apiClient, 'user1', {});

        expect(disconnected).toEqual([true, false]);
        expect(elem.querySelectorAll('.jux-lazy-sentinel')).toHaveLength(1);
    });

    it('asks for a deferred page again after it failed, instead of skipping it (FIAB-08)', async () => {
        const elem = document.createElement('div');
        elem.dataset.juxHasMore = 'true';
        vi.spyOn(console, 'error').mockImplementation(() => {});
        const apiClient = {
            getUrl: (path, query) => path + '?page=' + query.page,
            getJSON: vi.fn()
                .mockRejectedValueOnce(new Error('503'))
                .mockResolvedValue({ Sections: [], HasMore: false })
        };

        _setupLazyLoader(elem, apiClient, 'user1', {});
        intersect();
        await flush();
        intersect();
        await flush();

        expect(apiClient.getJSON.mock.calls.map((c) => c[0])).toEqual(['JuxHomepage/Sections?page=1', 'JuxHomepage/Sections?page=1']);
        vi.restoreAllMocks();
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
    it('opens the native list of a genre by its id when its title is clicked (F-56)', async () => {
        window.JellyfinAPI = { layoutManager: { tv: false } };
        const client = {
            serverId: () => 'srv',
            getUrl: (path) => path,
            getJSON: vi.fn(() => Promise.resolve({ Id: 'g1' }))
        };
        const elem = document.createElement('div');

        await juxHomepage._buildSection(elem, client, 'u', null, { WidgetType: 'jux.admin.genre', DisplayName: 'Thriller', Route: 'genre', AdditionalData: 'Thriller', ViewMode: 'Portrait' });
        const link = elem.querySelector('.sectionTitleContainer a');
        expect(link.getAttribute('href')).toBe('#');
        expect(client.getJSON).not.toHaveBeenCalled();

        link.click();
        await vi.waitFor(() => expect(window.location.hash).toBe('#/list?genreId=g1&serverId=srv'));
        expect(client.getJSON).toHaveBeenCalledWith('Genres/Thriller');
        delete window.JellyfinAPI;
        window.location.hash = '';
    });
});

// A window stand-in for the stale chunk guard: session storage, resource timing, fetch, reload and the
// server's patch state are the only services it touches. The server patches by default.
function fakeWindow({ storage = new Map(), noStorage = false, resources = [], patched = false, tabs = false, server = { LoadSections: 'Patched', TabsAnchor: true } } = {}) {
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
        juxTabInjector: tabs ? {} : undefined,
        ApiClient: {
            getUrl: (route) => route,
            getJSON: vi.fn(() => (server instanceof Error ? Promise.reject(server) : Promise.resolve(server)))
        }
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

    it('shows the message instead of reloading when the session storage refuses the reload flag (Annexe.a)', async () => {
        const win = fakeWindow({ resources: [{ name: 'http://h/web/home-html.abc.chunk.js', transferSize: 0 }] });
        win.sessionStorage.setItem = () => { throw new Error('QuotaExceededError'); };

        const outcome = await _repairStaleChunks(win, document);

        expect(outcome).toBe('notice');
        expect(win.location.reload).not.toHaveBeenCalled();
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

    it('asks the server before reloading, and reloads only when it patches the home chunk (FIAB-04)', async () => {
        const win = fakeWindow({ server: { LoadSections: 'HookNotFound', TabsAnchor: true } });
        renderHomeView({ sections: 2 });

        _armChunkGuard(win, document);
        await vi.advanceTimersByTimeAsync(5000);

        expect(win.ApiClient.getJSON).toHaveBeenCalledWith('JuxHomepage/PatchStatus');
        expect(win.location.reload).not.toHaveBeenCalled();
        expect(win.fetch).not.toHaveBeenCalled();
        expect(win.storage.has('jux-chunk-reload')).toBe(false);
        const notice = document.getElementById('jux-chunk-notice');
        expect(notice.textContent).toContain('Jellyfin Web has changed');
        expect(notice.textContent).not.toContain('CDN');
    });

    it('does not reload for missing JellyUX tabs when the server has no tab anchor (FIAB-04)', async () => {
        const win = fakeWindow({ patched: true, tabs: true, server: { LoadSections: 'Patched', TabsAnchor: false } });
        renderHomeView({ sections: 2, juxPanes: false });

        _armChunkGuard(win, document);
        await vi.advanceTimersByTimeAsync(5000);

        expect(win.location.reload).not.toHaveBeenCalled();
        expect(document.getElementById('jux-chunk-notice').textContent).toContain('Jellyfin Web has changed');
    });

    it('neither reloads nor blames a cache when the server does not say it patched (FIAB-04)', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const win = fakeWindow({ server: new Error('offline') });
        renderHomeView({ sections: 2 });

        _armChunkGuard(win, document);
        await vi.advanceTimersByTimeAsync(5000);

        expect(win.location.reload).not.toHaveBeenCalled();
        expect(document.getElementById('jux-chunk-notice')).toBeNull();
        expect(errorSpy).toHaveBeenCalled();
        errorSpy.mockRestore();
    });

    it('waits for the home page, then stops after its first decision', async () => {
        const win = fakeWindow();
        document.body.innerHTML = '<div class="page" id="moviesPage"></div>';

        _armChunkGuard(win, document);
        await vi.advanceTimersByTimeAsync(3000);
        expect(win.location.reload).not.toHaveBeenCalled();

        renderHomeView({ sections: 2 });
        document.dispatchEvent(new CustomEvent('viewshow'));
        await vi.advanceTimersByTimeAsync(5000);

        expect(win.location.reload).toHaveBeenCalledTimes(1);
    });

    it('does not poll while another page is shown (Annexe.a)', async () => {
        const win = fakeWindow();
        document.body.innerHTML = '<div class="page" id="moviesPage"></div>';

        _armChunkGuard(win, document);
        await vi.advanceTimersByTimeAsync(3000);

        expect(vi.getTimerCount()).toBe(0);
    });
});

describe('_makeFetchData', () => {
    const { _makeFetchData } = juxHomepage;
    const apiClient = {
        getUrl: (path, params) => ({ path, params }),
        getJSON: (request) => Promise.resolve(request)
    };
    const common = {
        userId: 'u1',
        startIndex: 0,
        limit: 20,
        fields: 'PrimaryImageAspectRatio,DateCreated',
        imageTypeLimit: 1,
        enableImageTypes: 'Primary,Thumb,Backdrop',
        enableTotalRecordCount: false
    };

    // Native routes carry the filters other plugins hook on them (Jellyfin Enhanced hidden content
    // and Spoiler Guard), which /JuxHomepage/Section never sees (TODO_V4.md 4.3, F-15).
    it.each([
        ['jux.native.continue-watching', 'UserItems/Resume', { ...common, mediaTypes: 'Video' }],
        ['jux.native.next-up', 'Shows/NextUp', common],
        ['jux.native.recently-added-movies', 'Items/Latest', { ...common, includeItemTypes: 'Movie' }],
        ['jux.native.recently-added-shows', 'Items/Latest', { ...common, limit: 60, includeItemTypes: 'Episode' }],
        ['jux.native.recently-added-episodes', 'Items/Latest', { ...common, limit: 200, includeItemTypes: 'Episode', groupItems: false }]
    ])('reads %s from the native route %s', async (widgetType, path, params) => {
        const request = await _makeFetchData(apiClient, 'u1', { WidgetType: widgetType })();

        expect(request).toEqual({ path, params });
    });

    it('turns the bare array of Items/Latest into the rows of the section, at most MaxItems (F-36)', async () => {
        const latestClient = { getUrl: (path) => path, getJSON: () => Promise.resolve([{ Id: 'a' }, { Id: 'b' }, { Id: 'c' }]) };

        const result = await _makeFetchData(latestClient, 'u1', { WidgetType: 'jux.native.recently-added-shows', MaxItems: 2 })();

        expect(result).toEqual({ Items: [{ Id: 'a' }, { Id: 'b' }] });
    });

    it('keeps at most two episodes of a series in Recently Added Episodes, and fills the row with others (F-65)', async () => {
        const bulk = Array.from({ length: 10 }, (_, i) => ({ Id: 'a' + i, SeriesId: 'A' }));
        const others = Array.from({ length: 5 }, (_, i) => ({ Id: 'b' + i, SeriesId: 'B' + i }));
        const latestClient = { getUrl: (path) => path, getJSON: () => Promise.resolve([...bulk, ...others]) };

        const result = await _makeFetchData(latestClient, 'u1', { WidgetType: 'jux.native.recently-added-episodes', MaxItems: 6 })();

        expect(result.Items.map((e) => e.Id)).toEqual(['a0', 'a1', 'b0', 'b1', 'b2', 'b3']);
    });

    it('reads the other widgets from the JellyUX section endpoint', async () => {
        const request = await _makeFetchData(apiClient, 'u1', { WidgetType: 'jux.admin.genre', AdditionalData: 'Drama' })();

        expect(request).toEqual({
            path: 'JuxHomepage/Section/jux.admin.genre',
            params: { userId: 'u1', additionalData: 'Drama', startIndex: 0, limit: 20 }
        });
    });

    it('sends the key of the section row, so a second Personalized row reads its own settings (F-27)', async () => {
        const descriptor = { WidgetType: 'jux.personalized.favorite-genre', AdditionalData: 'Drama', SectionKey: 'jux.personalized.favorite-genre#2' };

        const request = await _makeFetchData(apiClient, 'u1', descriptor)();

        expect(request.params.sectionKey).toBe('jux.personalized.favorite-genre#2');
    });
});

describe('failed home section (F-40)', () => {
    it('resolves a failed fetch to one marker row, so the native container keeps the section visible', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const fetchData = _guardFetchData(() => Promise.reject(new Error('offline')), { WidgetType: 'jux.genre' });

        const result = await fetchData();

        expect(result.Items).toHaveLength(1);
        expect(_guardGetItemsHtml(() => 'cards')(result.Items)).toContain('jux-error-state-compact');
        errorSpy.mockRestore();
    });

    it('never answers a section whose container Jellyfin destroyed while it was loading (A-02)', async () => {
        const settle = (promise) => Promise.race([promise.then(() => 'answered', () => 'failed'), new Promise((r) => setTimeout(() => r('pending'), 20))]);
        const answers = [];
        const container = { getItemsHtml: () => 'cards' };
        container.fetchData = _guardFetchData(() => new Promise((resolve, reject) => answers.push({ resolve, reject })), { WidgetType: 'jux.genre' });
        const loaded = container.fetchData();
        const failed = container.fetchData();

        // destroySections (jellyfin-web 10.11.10) nulls the hooks of every container it removes.
        container.fetchData = null;
        container.getItemsHtml = null;
        answers[0].resolve({ Items: [{ Id: 'a' }] });
        answers[1].reject(new Error('aborted'));

        expect(await settle(loaded)).toBe('pending');
        expect(await settle(failed)).toBe('pending');
    });

    it('passes successful rows through to the card renderer', async () => {
        const fetchData = _guardFetchData(() => Promise.resolve({ Items: [{ Id: 'a' }] }), { WidgetType: 'jux.genre' });
        const getItemsHtml = _guardGetItemsHtml((items) => 'cards:' + items.length);

        expect(getItemsHtml((await fetchData()).Items)).toBe('cards:1');
    });
});

describe('first render (F-41)', () => {
    const originalHook = window.JUXHomepage.originalLoadSections;

    afterEach(() => {
        vi.useRealTimers();
        vi.restoreAllMocks();
        window.JUXHomepage.originalLoadSections = originalHook;
    });

    function apiClient(response) {
        let resolve;
        const pending = new Promise((r) => { resolve = r; });
        return {
            client: {
                getCurrentUserId: () => 'u1',
                getUrl: (route) => route,
                getJSON: vi.fn(() => pending)
            },
            answer: (value) => { resolve(value); return pending.then(() => new Promise((r) => setTimeout(r, 0))); },
            response
        };
    }

    it('shows grey placeholders before the first request answers, then replaces them', async () => {
        const elem = document.createElement('div');
        const api = apiClient();

        _renderHome({}, elem, api.client, {}, {});

        expect(elem.querySelectorAll('.jux-skeleton .jux-skeleton-section')).toHaveLength(3);
        expect(elem.querySelectorAll('.jux-skeleton .card.overflowBackdropCard')).toHaveLength(24);
        await api.answer({ Sections: [], HasMore: false, Enabled: true });
        expect(elem.querySelector('.jux-skeleton')).toBeNull();
        expect(api.client.getJSON).toHaveBeenCalledTimes(1);
        expect(api.client.getJSON.mock.calls[0][0]).toBe('JuxHomepage/Sections');
    });

    it('renders the native home without a Sections request when JellyUX is off for the user (F-36)', () => {
        const api = apiClient();
        const native = vi.fn();
        window.JUXHomepage.originalLoadSections = native;
        window.ApiClient = api.client;
        window.sessionStorage.setItem('jux-user-off:u1', '1');
        window.location.hash = '#/home';

        window.JUXHomepage.loadSections(document.createElement('div'), api.client, { Id: 'u1' }, {});

        expect(native).toHaveBeenCalledTimes(1);
        expect(api.client.getJSON).not.toHaveBeenCalled();
        window.sessionStorage.clear();
        delete window.ApiClient;
    });

    it('renders the native home without a Sections request in the TV layout, set on <html> (FIAB-05)', () => {
        const api = apiClient();
        const native = vi.fn();
        window.JUXHomepage.originalLoadSections = native;
        window.location.hash = '#/home';
        document.documentElement.classList.add('layout-tv');

        window.JUXHomepage.loadSections(document.createElement('div'), api.client, { Id: 'u1' }, {});

        document.documentElement.classList.remove('layout-tv');
        expect(native).toHaveBeenCalledTimes(1);
        expect(api.client.getJSON).not.toHaveBeenCalled();
    });

    it('hands the home screen back to Jellyfin when the first Sections page has not answered in 15 s (FIAB-08)', async () => {
        vi.useFakeTimers();
        const elem = document.createElement('div');
        const api = apiClient();
        const native = vi.fn();
        window.JUXHomepage.originalLoadSections = native;
        vi.spyOn(console, 'error').mockImplementation(() => {});

        _renderHome({}, elem, api.client, {}, {});
        await vi.advanceTimersByTimeAsync(14999);
        expect(native).not.toHaveBeenCalled();
        await vi.advanceTimersByTimeAsync(1);

        expect(native).toHaveBeenCalledTimes(1);
        expect(elem.querySelector('.jux-skeleton')).toBeNull();
        expect(elem.classList.contains('jux-homepage-active')).toBe(false);
        vi.useRealTimers();
        await api.answer({ Sections: [{ WidgetType: 'jux.native.my-media', DisplayName: 'Late' }], HasMore: false, Enabled: true });
        expect(elem.querySelector('.jux-widget-section')).toBeNull();
    });

    it('loses only the section whose descriptor throws, not the page (FIAB-08)', async () => {
        const elem = document.createElement('div');
        const api = apiClient();
        const native = vi.fn();
        window.JUXHomepage.originalLoadSections = native;
        vi.spyOn(console, 'error').mockImplementation(() => {});

        _renderHome({}, elem, api.client, {}, {});
        await api.answer({ Sections: [{ DisplayName: 'Broken' }, { WidgetType: 'jux.native.my-media', DisplayName: 'Fine' }], HasMore: false, Enabled: true });

        expect(native).not.toHaveBeenCalled();
        expect([...elem.querySelectorAll('.jux-widget-section .sectionTitle')].map((t) => t.textContent)).toEqual(['Fine']);
    });

    it('hands the home screen back to Jellyfin when the first page has no section, instead of a blank page (Annexe.b)', async () => {
        const elem = document.createElement('div');
        const api = apiClient();
        const native = vi.fn();
        window.JUXHomepage.originalLoadSections = native;

        _renderHome({}, elem, api.client, {}, {});
        await api.answer({ Sections: [], HasMore: false, Enabled: true });

        expect(native).toHaveBeenCalledTimes(1);
        expect(elem.classList.contains('jux-homepage-active')).toBe(false);
    });

    it('hands the home screen back to Jellyfin when Sections says the plugin is disabled', async () => {
        const elem = document.createElement('div');
        const api = apiClient();
        const native = vi.fn();
        window.JUXHomepage.originalLoadSections = native;

        _renderHome({}, elem, api.client, {}, {});
        await api.answer({ Sections: [], HasMore: false, Enabled: false });

        expect(native).toHaveBeenCalledTimes(1);
        expect(elem.querySelector('.jux-skeleton')).toBeNull();
        expect(elem.classList.contains('jux-homepage-active')).toBe(false);
    });

    it('treats a container emptied and reused by the native home tab like a new one', async () => {
        const elem = document.createElement('div');
        elem.classList.add('homeSectionsContainer', 'jux-homepage-active');
        const api = apiClient();
        const native = vi.fn();
        window.JUXHomepage.originalLoadSections = native;

        _renderHome({}, elem, api.client, {}, {});

        expect(elem.querySelector('.jux-skeleton')).not.toBeNull();
        await api.answer({ Sections: [], HasMore: false, Enabled: false });
        expect(native).toHaveBeenCalledTimes(1);
    });
});
