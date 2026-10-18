import { describe, it, expect, vi, afterEach } from 'vitest';
import juxBreadcrumb from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-breadcrumb.js';

const {
    _tryRenderBreadcrumb,
    _currentDetailItemId,
    _isDetailPage,
    _isSupportedType,
    _libraryHashFor,
    _buildBreadcrumbSegments,
    _seasonFallbackName,
    _episodeLabel
} = juxBreadcrumb;

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

describe('_isDetailPage', () => {
    it('returns true for a details route', () => {
        expect(_isDetailPage('#/details?id=abc')).toBe(true);
    });

    it('returns false for a library listing route', () => {
        expect(_isDetailPage('#/movies?topParentId=abc')).toBe(false);
    });

    it('returns false for an empty hash', () => {
        expect(_isDetailPage('')).toBe(false);
        expect(_isDetailPage(undefined)).toBe(false);
    });
});

describe('_isSupportedType', () => {
    it.each(['Movie', 'Series', 'Season', 'Episode', 'MusicArtist', 'MusicAlbum', 'Audio'])(
        'returns true for %s',
        (type) => {
            expect(_isSupportedType({ Type: type })).toBe(true);
        }
    );

    it('returns false for an unsupported type', () => {
        expect(_isSupportedType({ Type: 'BoxSet' })).toBe(false);
        expect(_isSupportedType({ Type: 'Genre' })).toBe(false);
    });

    it('returns false for a nullish item', () => {
        expect(_isSupportedType(null)).toBe(false);
        expect(_isSupportedType(undefined)).toBe(false);
    });
});

describe('_libraryHashFor', () => {
    it('maps Movie to the movies library route', () => {
        expect(_libraryHashFor('Movie', 'lib1')).toBe('#/movies?topParentId=lib1');
    });

    it('maps Series/Season/Episode to the tv library route', () => {
        expect(_libraryHashFor('Series', 'lib2')).toBe('#/tv?topParentId=lib2');
        expect(_libraryHashFor('Season', 'lib2')).toBe('#/tv?topParentId=lib2');
        expect(_libraryHashFor('Episode', 'lib2')).toBe('#/tv?topParentId=lib2');
    });

    it('maps MusicArtist/MusicAlbum/Audio to the music library route', () => {
        expect(_libraryHashFor('MusicArtist', 'lib3')).toBe('#/music?topParentId=lib3');
        expect(_libraryHashFor('MusicAlbum', 'lib3')).toBe('#/music?topParentId=lib3');
        expect(_libraryHashFor('Audio', 'lib3')).toBe('#/music?topParentId=lib3');
    });

    it('returns null when there is no topParentId', () => {
        expect(_libraryHashFor('Movie', null)).toBeNull();
    });

    it('returns null for an unsupported type', () => {
        expect(_libraryHashFor('BoxSet', 'lib1')).toBeNull();
    });
});

describe('_seasonFallbackName', () => {
    it('formats the season number in English by default', () => {
        document.documentElement.lang = 'en';
        expect(_seasonFallbackName({ IndexNumber: 2 })).toBe('Season 2');
    });

    it('falls back to season 1 when IndexNumber is missing', () => {
        document.documentElement.lang = 'en';
        expect(_seasonFallbackName({})).toBe('Season 1');
    });
});

describe('_episodeLabel', () => {
    it('formats "NxNN - Title" when both index numbers are present', () => {
        expect(_episodeLabel({ ParentIndexNumber: 1, IndexNumber: 3, Name: 'Pilot' })).toBe('1x03 - Pilot');
    });

    it('pads single-digit episode numbers to two digits', () => {
        expect(_episodeLabel({ ParentIndexNumber: 2, IndexNumber: 9, Name: 'Finale' })).toBe('2x09 - Finale');
    });

    it('does not pad episode numbers already two digits or more', () => {
        expect(_episodeLabel({ ParentIndexNumber: 1, IndexNumber: 12, Name: 'Long Season' })).toBe('1x12 - Long Season');
    });

    it('falls back to the plain name when index numbers are missing', () => {
        expect(_episodeLabel({ Name: 'Special' })).toBe('Special');
    });
});

describe('_buildBreadcrumbSegments', () => {
    const library = { Id: 'lib1', Name: 'Movies' };

    it('returns an empty array for an unsupported type', () => {
        expect(_buildBreadcrumbSegments({ Type: 'BoxSet', Name: 'X' }, library, null)).toEqual([]);
    });

    it('returns an empty array for a nullish item', () => {
        expect(_buildBreadcrumbSegments(null, library, null)).toEqual([]);
    });

    it('builds Library > Movie for a movie with no collection', () => {
        const item = { Type: 'Movie', Name: 'The Movie' };
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/movies?topParentId=lib1' },
            { text: 'The Movie', url: null }
        ]);
    });

    it('builds Library > Collection > Movie when a collection ref is provided', () => {
        const item = { Type: 'Movie', Name: 'The Movie' };
        const collectionRef = { CollectionId: 'col1', CollectionName: 'The Saga' };
        expect(_buildBreadcrumbSegments(item, library, collectionRef)).toEqual([
            { text: 'Movies', url: '#/movies?topParentId=lib1' },
            { text: 'The Saga', url: '#/details?id=col1' },
            { text: 'The Movie', url: null }
        ]);
    });

    it('builds Library > Series for a series', () => {
        const item = { Type: 'Series', Name: 'The Show' };
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/tv?topParentId=lib1' },
            { text: 'The Show', url: null }
        ]);
    });

    it('builds Library > Series > Season for a season, using SeriesName/SeriesId', () => {
        const item = { Type: 'Season', Name: 'Season 2', SeriesId: 'series1', SeriesName: 'The Show' };
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/tv?topParentId=lib1' },
            { text: 'The Show', url: '#/details?id=series1' },
            { text: 'Season 2', url: null }
        ]);
    });

    it('falls back to "Season {number}" when a season has no Name', () => {
        const item = { Type: 'Season', IndexNumber: 3, SeriesId: 'series1', SeriesName: 'The Show' };
        document.documentElement.lang = 'en';
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/tv?topParentId=lib1' },
            { text: 'The Show', url: '#/details?id=series1' },
            { text: 'Season 3', url: null }
        ]);
    });

    it('builds the full chain for an episode using SeriesName/SeasonName already on the item', () => {
        const item = {
            Type: 'Episode',
            Name: 'Pilot',
            SeriesId: 'series1',
            SeriesName: 'The Show',
            SeasonId: 'season1',
            SeasonName: 'Season 1',
            ParentIndexNumber: 1,
            IndexNumber: 1
        };
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/tv?topParentId=lib1' },
            { text: 'The Show', url: '#/details?id=series1' },
            { text: 'Season 1', url: '#/details?id=season1' },
            { text: '1x01 - Pilot', url: null }
        ]);
    });

    it('builds Library > Artist for a music artist', () => {
        const item = { Type: 'MusicArtist', Name: 'The Band' };
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/music?topParentId=lib1' },
            { text: 'The Band', url: null }
        ]);
    });

    it('builds Library > Artist > Album for a music album using AlbumArtists', () => {
        const item = { Type: 'MusicAlbum', Name: 'The Album', AlbumArtists: [{ Id: 'artist1', Name: 'The Band' }] };
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/music?topParentId=lib1' },
            { text: 'The Band', url: '#/details?id=artist1' },
            { text: 'The Album', url: null }
        ]);
    });

    it('builds Library > Artist > Album > Track for an audio track using AlbumArtists/AlbumId/Album', () => {
        const item = {
            Type: 'Audio',
            Name: 'The Track',
            AlbumArtists: [{ Id: 'artist1', Name: 'The Band' }],
            AlbumId: 'album1',
            Album: 'The Album'
        };
        expect(_buildBreadcrumbSegments(item, library, null)).toEqual([
            { text: 'Movies', url: '#/music?topParentId=lib1' },
            { text: 'The Band', url: '#/details?id=artist1' },
            { text: 'The Album', url: '#/details?id=album1' },
            { text: 'The Track', url: null }
        ]);
    });

    it('omits the library segment entirely when no ancestor library was resolved', () => {
        const item = { Type: 'Movie', Name: 'The Movie' };
        expect(_buildBreadcrumbSegments(item, null, null)).toEqual([
            { text: 'The Movie', url: null }
        ]);
    });
});


describe('breadcrumb on a detail page reopened from the home page (FIAB-09)', () => {
    const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

    afterEach(() => {
        location.hash = '';
        delete window.ApiClient;
        document.body.innerHTML = '';
    });

    it('shows the breadcrumb again on X -> home -> X', async () => {
        document.body.innerHTML = '<div class="skinHeader"><div class="headerLeft"></div></div>';
        window.ApiClient = {
            getCurrentUserId: () => 'u1',
            getUrl: (path) => path,
            getItem: vi.fn().mockResolvedValue({ Id: 'bc01', Name: 'Alder Creek', Type: 'Movie' }),
            getAncestorItems: vi.fn().mockResolvedValue([{ Type: 'CollectionFolder', Id: 'lib1', Name: 'Movies' }]),
            getJSON: vi.fn().mockResolvedValue([])
        };
        const visit = async (hash) => {
            location.hash = hash;
            _tryRenderBreadcrumb();
            await flush();
            await flush();
            return document.querySelector('.jux-breadcrumb').style.display;
        };

        expect(await visit('#/details?id=bc01')).toBe('');
        expect(await visit('#/home')).toBe('none');
        expect(await visit('#/details?id=bc01')).toBe('');
        expect(document.querySelector('.jux-breadcrumb').textContent).toContain('Alder Creek');
    });
});
