import { describe, it, expect } from 'vitest';
import juxHistory from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-history.js';

const { _sortOptions, _labelFor, _buildShellHtml } = juxHistory;

describe('_labelFor', () => {
    it('returns the label matching the current value', () => {
        const options = [{ value: 'LastPlayed', label: 'Last Watched' }, { value: 'Name', label: 'Name' }];
        expect(_labelFor(options, 'Name')).toBe('Name');
    });

    it('falls back to the first option when the value is unknown', () => {
        const options = [{ value: 'LastPlayed', label: 'Last Watched' }, { value: 'Name', label: 'Name' }];
        expect(_labelFor(options, 'Unknown')).toBe('Last Watched');
    });
});

describe('_sortOptions', () => {
    it('returns 2 sort options', () => {
        const t = { sortLastPlayed: 'Last Watched', sortName: 'Name' };
        expect(_sortOptions(t)).toHaveLength(2);
    });
});

describe('_buildShellHtml', () => {
    it('renders the section title and the sort button with the current label', () => {
        const html = _buildShellHtml('en', { sortBy: 'Name', sortOrder: 'Ascending' });

        expect(html).toContain('History');
        expect(html).toContain('id="jux-history-sort-btn"');
        expect(html).toContain('>Name<');
    });

    it('renders localized labels for French', () => {
        const html = _buildShellHtml('fr', { sortBy: 'LastPlayed', sortOrder: 'Descending' });

        expect(html).toContain('Historique');
        expect(html).toContain('Dernier vu');
    });
});
