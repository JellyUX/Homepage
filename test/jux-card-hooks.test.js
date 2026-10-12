import { describe, it, expect } from 'vitest';
import juxCardHooks from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-card-hooks.js';

const {
    _iconName,
    _readCardItemInfo,
    _isWatchlistType,
    _currentDetailItemId,
    _infiniteScrollMediaType,
    _parseSortSettings,
    _buildItemsQuery,
    _hasMoreItems
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
