import { describe, it, expect } from 'vitest';
import juxWatchlist from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-watchlist.js';

const { _sortOptions, _typeOptions, _labelFor, _buildShellHtml } = juxWatchlist;

describe('_labelFor', () => {
    const options = [{ value: 'A', label: 'Alpha' }, { value: 'B', label: 'Beta' }];

    it('returns the label matching the current value', () => {
        expect(_labelFor(options, 'B')).toBe('Beta');
    });

    it('falls back to the first option when the value is unknown', () => {
        expect(_labelFor(options, 'Z')).toBe('Alpha');
    });
});

describe('_sortOptions / _typeOptions', () => {
    it('returns 4 sort options and 3 type options in English', () => {
        const t = { sortName: 'Name', sortDateAdded: 'Date Added', sortReleaseDate: 'Release Date', sortCommunityRating: 'Community Rating', typeAll: 'All', typeMovie: 'Movies', typeSeries: 'Series' };
        expect(_sortOptions(t)).toHaveLength(4);
        expect(_typeOptions(t)).toHaveLength(3);
    });
});

describe('_buildShellHtml', () => {
    it('renders the section title and both sort/filter buttons with the current labels', () => {
        const html = _buildShellHtml('en', { sortBy: 'Name', sortOrder: 'Ascending', includeItemTypes: 'Movie' });

        expect(html).toContain('Watchlist');
        expect(html).toContain('id="jux-watchlist-sort-btn"');
        expect(html).toContain('id="jux-watchlist-type-btn"');
        expect(html).toContain('>Name<');
        expect(html).toContain('>Movies<');
    });

    it('renders localized labels for French', () => {
        const html = _buildShellHtml('fr', { sortBy: 'DateAdded', sortOrder: 'Descending', includeItemTypes: 'All' });

        expect(html).toContain('Date d’ajout');
        expect(html).toContain('Tous');
    });
});
