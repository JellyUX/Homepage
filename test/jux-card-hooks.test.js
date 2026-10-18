import { describe, it, expect, afterEach, vi } from 'vitest';
import juxCardHooks from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-card-hooks.js';

const {
    _iconName,
    _readCardItemInfo,
    _isWatchlistType,
    _currentDetailItemId,
    _infiniteScrollMediaType,
    _parseSortSettings,
    _buildItemsQuery,
    _hasMoreItems,
    _syncSession
} = juxCardHooks;

describe('_iconName', () => {
    it('returns the filled bookmark icon when liked', () => {
        expect(_iconName(true)).toBe('bookmark');
    });

    it('returns the outline bookmark icon when not liked', () => {
        expect(_iconName(false)).toBe('bookmark_border');
    });
});

describe('_readCardItemInfo', () => {
    it('reads the item id and type from a card element\'s data attributes', () => {
        document.body.innerHTML = '<div class="card" data-id="abc123" data-type="Movie"></div>';
        const card = document.querySelector('.card');

        expect(_readCardItemInfo(card)).toEqual({ id: 'abc123', type: 'Movie' });
    });

    it('returns null when the card has no data-id', () => {
        document.body.innerHTML = '<div class="card"></div>';
        const card = document.querySelector('.card');

        expect(_readCardItemInfo(card)).toBeNull();
    });
});

describe('_isWatchlistType', () => {
    it('accepts movies and series, the only types the Watchlist tab lists', () => {
        expect(_isWatchlistType('Movie')).toBe(true);
        expect(_isWatchlistType('Series')).toBe(true);
    });

    it('rejects episodes, seasons and a missing type', () => {
        expect(_isWatchlistType('Episode')).toBe(false);
        expect(_isWatchlistType('Season')).toBe(false);
        expect(_isWatchlistType(null)).toBe(false);
    });
});

describe('_currentDetailItemId', () => {
    it('extracts the item id from a details page hash', () => {
        window.location.hash = '#/details?id=abc123def456';
        expect(_currentDetailItemId()).toBe('abc123def456');
    });

    it('returns null when the hash has no id', () => {
        window.location.hash = '#/home';
        expect(_currentDetailItemId()).toBeNull();
    });
});

describe('_infiniteScrollMediaType', () => {
    it('recognizes the Movies library route, with or without .html', () => {
        expect(_infiniteScrollMediaType('#/movies?topParentId=abc')).toBe('movies');
        expect(_infiniteScrollMediaType('#/movies.html?topParentId=abc')).toBe('movies');
    });

    it('recognizes the Series library route as media type "series"', () => {
        expect(_infiniteScrollMediaType('#/tv?topParentId=abc')).toBe('series');
        expect(_infiniteScrollMediaType('#/tv.html?topParentId=abc')).toBe('series');
    });

    it('returns null for unrelated routes', () => {
        expect(_infiniteScrollMediaType('#/home')).toBeNull();
        expect(_infiniteScrollMediaType('#/music?topParentId=abc')).toBeNull();
    });
});

describe('_parseSortSettings', () => {
    it('parses populated sort and filter JSON', () => {
        const sort = JSON.stringify({ SortBy: 'PremiereDate', SortOrder: 'Descending' });
        const filter = JSON.stringify({ Filters: 'IsUnplayed', Years: '2020', Genres: 'Drama', Tags: 'based on a book' });

        expect(_parseSortSettings(sort, filter)).toEqual({
            sortBy: 'PremiereDate',
            sortOrder: 'Descending',
            filters: 'IsUnplayed',
            years: '2020',
            genres: 'Drama',
            tags: 'based on a book'
        });
    });

    it('falls back to defaults when both are missing', () => {
        expect(_parseSortSettings(null, null)).toEqual({
            sortBy: 'SortName,ProductionYear',
            sortOrder: 'Ascending',
            filters: '',
            years: '',
            genres: '',
            tags: ''
        });
    });

    it('falls back to defaults on invalid JSON rather than throwing', () => {
        expect(_parseSortSettings('not json', 'also not json')).toEqual({
            sortBy: 'SortName,ProductionYear',
            sortOrder: 'Ascending',
            filters: '',
            years: '',
            genres: '',
            tags: ''
        });
    });
});

describe('_buildItemsQuery', () => {
    it('builds the base query without optional filter fields when absent', () => {
        const query = _buildItemsQuery({
            parentId: 'p1',
            includeItemTypes: 'Movie',
            sortBy: 'SortName',
            sortOrder: 'Ascending',
            startIndex: 100
        });

        expect(query).toEqual({
            ParentId: 'p1',
            IncludeItemTypes: 'Movie',
            Recursive: true,
            SortBy: 'SortName',
            SortOrder: 'Ascending',
            StartIndex: 100,
            Limit: 100,
            Fields: 'PrimaryImageAspectRatio'
        });
    });

    it('includes optional filter fields only when provided', () => {
        const query = _buildItemsQuery({
            parentId: 'p1',
            includeItemTypes: 'Series',
            sortBy: 'SortName',
            sortOrder: 'Ascending',
            startIndex: 0,
            filters: 'IsUnplayed',
            years: '2020',
            genres: 'Drama',
            tags: 'x',
            nameStartsWith: 'A',
            limit: 50
        });

        expect(query.Filters).toBe('IsUnplayed');
        expect(query.Years).toBe('2020');
        expect(query.Genres).toBe('Drama');
        expect(query.Tags).toBe('x');
        expect(query.NameStartsWith).toBe('A');
        expect(query.Limit).toBe(50);
    });
});

describe('_hasMoreItems', () => {
    it('returns true while startIndex is below the total record count', () => {
        expect(_hasMoreItems(100, 250)).toBe(true);
    });

    it('returns false once startIndex reaches the total record count', () => {
        expect(_hasMoreItems(250, 250)).toBe(false);
        expect(_hasMoreItems(300, 250)).toBe(false);
    });
});

describe('_syncSession (F-51)', () => {
    const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

    afterEach(() => {
        delete window.ApiClient;
    });

    function client(answers) {
        let userId = null;
        return {
            setUser: (id) => { userId = id; },
            getCurrentUserId: () => userId,
            getUrl: (route, query) => route + '?userId=' + query.userId,
            getJSON: vi.fn((url) => answers[url.split('userId=')[1]])
        };
    }

    it('waits for a signed-in user, then loads the ids of that user once', async () => {
        const api = client({ alice: Promise.resolve({ Ids: ['m1'], Enabled: true }) });
        window.ApiClient = api;
        const state = { likedIds: new Set(), userId: null };

        _syncSession(state);
        expect(api.getJSON).not.toHaveBeenCalled();

        api.setUser('alice');
        _syncSession(state);
        _syncSession(state);
        await flush();

        expect(api.getJSON).toHaveBeenCalledTimes(1);
        expect([...state.likedIds]).toEqual(['m1']);
    });

    it('empties the set at once for another user and drops a late answer for the previous one', async () => {
        let answerAlice;
        const api = client({ alice: new Promise((r) => { answerAlice = r; }), bob: Promise.resolve({ Ids: ['s9'], Enabled: true }) });
        window.ApiClient = api;
        const state = { likedIds: new Set(['old']), userId: null };

        api.setUser('alice');
        _syncSession(state);
        expect(state.likedIds.size).toBe(0);
        api.setUser('bob');
        _syncSession(state);
        answerAlice({ Ids: ['m1'], Enabled: true });
        await flush();

        expect(state.userId).toBe('bob');
        expect([...state.likedIds]).toEqual(['s9']);
    });

    it('removes the buttons and stays off when the answer says JellyUX is off for the user (F-36)', async () => {
        const api = client({ alice: Promise.resolve({ Ids: ['m1'], Enabled: false }) });
        window.ApiClient = api;
        document.body.innerHTML = '<button class="jux-watchlist-toggle"></button><button class="jux-watchlist-detail-toggle"></button>';
        const state = { likedIds: new Set(), userId: null };

        api.setUser('alice');
        _syncSession(state);
        await flush();

        expect(document.querySelectorAll('.jux-watchlist-toggle, .jux-watchlist-detail-toggle')).toHaveLength(0);
        expect(state.likedIds.size).toBe(0);
        expect(window.JuxCore.isOffForUser()).toBe(true);
        window.sessionStorage.clear();
        document.body.innerHTML = '';
    });

    it('retries on the next view after a failed load', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const api = client({ alice: Promise.reject(new Error('slow')) });
        window.ApiClient = api;
        const state = { likedIds: new Set(), userId: null };

        api.setUser('alice');
        _syncSession(state);
        await flush();
        _syncSession(state);

        expect(api.getJSON).toHaveBeenCalledTimes(2);
        errorSpy.mockRestore();
    });
});

describe('library infinite scroll, end of the list (FIAB-01)', () => {
    const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
    const NativeIO = window.IntersectionObserver;

    const viewSwap = () => new Promise((resolve) => setTimeout(resolve, 450));

    async function setUpLibrary(getJSON) {
        localStorage.clear();
        document.body.innerHTML = '<div class="libraryPage"><div id="moviesTab" class="pageTabContent is-active">' +
            '<div class="itemsContainer">' + '<div class="card"></div>'.repeat(4) + '</div><div class="paging"></div></div></div>';
        // The sentinel is always in view: a list that re-arms queries again at once.
        window.IntersectionObserver = class {
            constructor(callback) { this.callback = callback; }
            observe(el) { setTimeout(() => this.callback([{ isIntersecting: true, target: el }]), 0); }
            unobserve() {}
            disconnect() {}
        };
        window.JellyfinAPI = { cardBuilder: { getCardsHtml: () => '' }, getPortraitShape: () => 'portrait' };
        window.ApiClient = { getCurrentUserId: () => 'u1', getUrl: (path, query) => path + '?' + new URLSearchParams(query), getJSON };
        location.hash = '#/movies?topParentId=abc1';
        // The view change re-arms 400 ms after the hash change.
        await viewSwap();
    }

    async function mutate(times) {
        for (let i = 0; i < times; i++) {
            document.body.appendChild(document.createElement('div'));
            await flush();
            await flush();
        }
    }

    afterEach(async () => {
        location.hash = '#/home';
        await viewSwap();
        window.IntersectionObserver = NativeIO;
        delete window.JellyfinAPI;
        delete window.ApiClient;
        document.body.innerHTML = '';
        localStorage.clear();
    });

    it('queries a list shorter than one page once, then never again on later mutations', async () => {
        const getJSON = vi.fn().mockResolvedValue({ Items: [], TotalRecordCount: 4 });
        await setUpLibrary(getJSON);
        await mutate(1);
        const listed = getJSON.mock.calls.length;
        expect(listed).toBeGreaterThan(0);

        await mutate(10);

        expect(getJSON.mock.calls.length - listed).toBe(0);
        expect(document.querySelectorAll('.jux-infinite-sentinel')).toHaveLength(0);
    });

    it('re-arms once on a native sort change, and once on a view change', async () => {
        const getJSON = vi.fn().mockResolvedValue({ Items: [], TotalRecordCount: 4 });
        await setUpLibrary(getJSON);
        await mutate(3);
        const listed = getJSON.mock.calls.length;

        localStorage.setItem('u1-abc1-movies', JSON.stringify({ SortBy: 'DateCreated', SortOrder: 'Descending' }));
        await mutate(3);
        expect(getJSON.mock.calls.length - listed).toBe(1);
        expect(getJSON.mock.calls[listed][0]).toContain('SortBy=DateCreated');

        location.hash = '#/movies?topParentId=abc1&tab=0';
        await viewSwap();
        await mutate(3);
        expect(getJSON.mock.calls.length - listed).toBe(2);
    });
});
