import { describe, it, expect, vi, afterEach } from 'vitest';
import juxCollections from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-collections.js';

const {
    _isSupportedForCollections,
    _isBoxSet,
    _sortFields,
    _storageKeys,
    _buildCollectionsSectionSkeleton,
    _currentDetailItemId,
    _syncObserver,
    _resortCollection
} = juxCollections;

describe('_isSupportedForCollections', () => {
    it('returns true for Movie and Series items', () => {
        expect(_isSupportedForCollections({ Type: 'Movie' })).toBe(true);
        expect(_isSupportedForCollections({ Type: 'Series' })).toBe(true);
    });

    it('returns false for other item types', () => {
        expect(_isSupportedForCollections({ Type: 'BoxSet' })).toBe(false);
        expect(_isSupportedForCollections({ Type: 'Episode' })).toBe(false);
    });

    it('returns false for a nullish item', () => {
        expect(_isSupportedForCollections(null)).toBe(false);
    });
});

describe('_isBoxSet', () => {
    it('returns true only for BoxSet items', () => {
        expect(_isBoxSet({ Type: 'BoxSet' })).toBe(true);
        expect(_isBoxSet({ Type: 'Movie' })).toBe(false);
        expect(_isBoxSet(null)).toBe(false);
    });
});

describe('_sortFields', () => {
    it('returns all 5 sort criteria, including both community and critic rating', () => {
        const fields = _sortFields();
        expect(fields).toHaveLength(5);
        const values = fields.map((f) => f.value);
        expect(values).toEqual(['SortName', 'PremiereDate', 'DateCreated', 'CommunityRating', 'CriticRating']);
    });
});

describe('_storageKeys', () => {
    it('builds jux-prefixed localStorage keys scoped to the collection id', () => {
        expect(_storageKeys('abc-123')).toEqual({
            sort: 'jux-collection-sort-abc-123',
            order: 'jux-collection-order-abc-123'
        });
    });
});

describe('_buildCollectionsSectionSkeleton', () => {
    it('renders an escaped title and an empty items container', () => {
        const section = _buildCollectionsSectionSkeleton('Included In');

        expect(section.className).toContain('jux-collections-section');
        expect(section.querySelector('.sectionTitle').textContent).toBe('Included In');
        const itemsContainer = section.querySelector('.itemsContainer');
        expect(itemsContainer).not.toBeNull();
        expect(itemsContainer.children.length).toBe(0);
    });

    it('escapes the title', () => {
        const section = _buildCollectionsSectionSkeleton('<script>Alert</script>');
        expect(section.innerHTML).not.toContain('<script>Alert</script>');
    });
});

describe('_currentDetailItemId', () => {
    it('extracts the id from the location hash', () => {
        window.location.hash = '#/details?id=abc123-def';
        expect(_currentDetailItemId()).toBe('abc123-def');
    });

    it('returns null when no id is present', () => {
        window.location.hash = '#/home';
        expect(_currentDetailItemId()).toBeNull();
    });
});

describe('detail page requests (F-09)', () => {
    const flush = () => new Promise(resolve => setTimeout(resolve, 0));

    function setUpDetailPage(itemId, item, refs) {
        document.body.innerHTML = '<div class="libraryPage"><div class="detailPageContent"></div></div>';
        location.hash = '#/details?id=' + itemId;
        // JellyUX is known to be on for the user, as after the first answer of a session.
        window.sessionStorage.setItem('jux-user-off:user1', '0');
        const api = {
            getCurrentUserId: () => 'user1',
            getUrl: path => path,
            getItem: vi.fn().mockResolvedValue(item),
            getJSON: vi.fn().mockResolvedValue(refs),
            getItems: vi.fn().mockResolvedValue({ Items: [{ Id: 'boxset1', Type: 'BoxSet' }] })
        };
        window.ApiClient = api;
        return api;
    }

    async function mutateTenTimes() {
        const page = document.querySelector('.libraryPage');
        for (let i = 0; i < 10; i++) {
            page.appendChild(document.createElement('div'));
            await flush();
        }
    }

    afterEach(() => {
        location.hash = '';
        _syncObserver();
        delete window.ApiClient;
        delete window.JellyfinAPI;
        window.sessionStorage.clear();
        document.body.innerHTML = '';
    });

    it('sends one getItem for ten mutations on a movie page, shared by Included In and the sort check', async () => {
        const api = setUpDetailPage('aa01', { Type: 'Movie' }, []);
        _syncObserver();
        await mutateTenTimes();

        expect(api.getItem).toHaveBeenCalledTimes(1);
        expect(api.getJSON).toHaveBeenCalledTimes(1);
    });

    it('does not resend Included In requests while cardBuilder is missing', async () => {
        const api = setUpDetailPage('aa02', { Type: 'Movie' }, [{ CollectionId: 'boxset1' }]);
        _syncObserver();
        await mutateTenTimes();

        expect(api.getItem).toHaveBeenCalledTimes(1);
        expect(api.getJSON).toHaveBeenCalledTimes(1);
        expect(api.getItems).toHaveBeenCalledTimes(1);
        expect(document.querySelector('.jux-collections-section')).toBeNull();
    });

    it('stops observing once the user leaves the detail page', async () => {
        const api = setUpDetailPage('aa03', { Type: 'Movie' }, []);
        _syncObserver();
        await flush();
        location.hash = '#/home';
        _syncObserver();
        api.getItem.mockClear();

        document.body.appendChild(document.createElement('div'));
        await flush();

        expect(api.getItem).not.toHaveBeenCalled();
    });
    it('adds one sort button to a collection page when a mutation burst runs it again before the first call resolves (in-flight guard)', async () => {
        const api = setUpDetailPage('aa05', { Type: 'BoxSet' }, []);
        document.querySelector('.detailPageContent').innerHTML =
            '<div class="verticalSection"><h2 class="sectionTitle">Items</h2><div class="itemsContainer collectionItemsContainer"></div></div>';
        for (let i = 0; i < 5; i++) {
            _syncObserver();
        }
        await flush();
        await flush();

        expect(document.querySelectorAll('.jux-collection-sort-btn')).toHaveLength(1);
        expect(api.getItem).toHaveBeenCalledTimes(1);
    });

    it('adds the sort button and keeps a sort change when the local storage is blocked (Annexe.a)', async () => {
        setUpDetailPage('aa06', { Type: 'BoxSet' }, []);
        document.querySelector('.detailPageContent').innerHTML =
            '<div class="verticalSection"><h2 class="sectionTitle">Items</h2><div class="itemsContainer collectionItemsContainer"></div></div>';
        // Only localStorage is blocked: the user state still reads sessionStorage.
        const { getItem, setItem } = Storage.prototype;
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(function (key) {
            if (this === window.localStorage) { throw new Error('SecurityError'); }
            return getItem.call(this, key);
        });
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (key, value) {
            if (this === window.localStorage) { throw new Error('SecurityError'); }
            return setItem.call(this, key, value);
        });
        let onChange;
        window.JuxUI.openSortDialog = (options) => { onChange = options.onChange; };
        vi.spyOn(console, 'error').mockImplementation(() => {});
        _syncObserver();
        await flush();
        await flush();

        const button = document.querySelector('.jux-collection-sort-btn');
        expect(button).not.toBeNull();
        button.click();
        expect(() => onChange('PremiereDate', 'Descending')).not.toThrow();
        vi.restoreAllMocks();
    });

    it('inserts one Included In section when a mutation burst runs it again before the first call resolves (in-flight guard)', async () => {
        setUpDetailPage('aa04', { Type: 'Movie' }, [{ CollectionId: 'boxset1' }]);
        window.JellyfinAPI = { cardBuilder: { getCardsHtml: () => '' }, getPortraitShape: () => 'portrait' };
        for (let i = 0; i < 5; i++) {
            _syncObserver();
        }
        await flush();
        await flush();

        expect(document.querySelectorAll('.jux-collections-section')).toHaveLength(1);
        expect(document.querySelector('.libraryPage').dataset.juxCollectionsPending).toBeUndefined();
    });
});

// BKL-39: a re-sort the server never answers fails after the request delay and keeps the cards shown.
describe('_resortCollection on a silent server', () => {
    afterEach(() => {
        vi.useRealTimers();
        delete window.JellyfinAPI;
        delete window.ApiClient;
    });

    it('gives up after the request delay and keeps the cards', async () => {
        vi.useFakeTimers();
        window.JellyfinAPI = { cardBuilder: { getCardsHtml: () => '' }, getPortraitShape: () => 'portrait' };
        window.ApiClient = { getCurrentUserId: () => 'u1', getUrl: (route) => route, getJSON: vi.fn(() => new Promise(() => {})) };
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const container = document.createElement('div');
        container.innerHTML = '<div class="card"></div>';

        _resortCollection('c1', container, { sortBy: 'SortName', sortOrder: 'Ascending' });
        await vi.advanceTimersByTimeAsync(15000);

        expect(errorSpy).toHaveBeenCalledWith('[JellyUX] Collection re-sort fetch failed:', expect.any(Error));
        expect(container.querySelectorAll('.card')).toHaveLength(1);
        errorSpy.mockRestore();
    });
});
