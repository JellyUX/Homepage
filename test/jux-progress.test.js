import { describe, it, expect } from 'vitest';
import juxProgress from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-progress.js';

const { _sortOptions, _buildShellHtml, _formatEpisodeCode, _formatTemplate } = juxProgress;

describe('_sortOptions', () => {
    it('returns 2 sort options', () => {
        const t = { sortLastPlayed: 'Last Watched', sortName: 'Name' };
        expect(_sortOptions(t)).toHaveLength(2);
    });
});

describe('_buildShellHtml', () => {
    it('renders the section title and the sort button with the current label', () => {
        const html = _buildShellHtml('en', { sortBy: 'Name', sortOrder: 'Ascending' });

        expect(html).toContain('Progress');
        expect(html).toContain('id="jux-progress-sort-btn"');
        expect(html).toContain('>Name<');
    });

    it('renders localized labels for French', () => {
        const html = _buildShellHtml('fr', { sortBy: 'LastPlayed', sortOrder: 'Descending' });

        expect(html).toContain('Progression');
        expect(html).toContain('Dernier vu');
    });
});

describe('_formatEpisodeCode', () => {
    it('formats season and episode numbers as SxxEyy, zero-padded', () => {
        expect(_formatEpisodeCode(1, 3)).toBe('S01E03');
    });

    it('does not pad numbers already two digits or more', () => {
        expect(_formatEpisodeCode(12, 34)).toBe('S12E34');
    });

    it('returns an empty string when either number is missing', () => {
        expect(_formatEpisodeCode(null, 3)).toBe('');
        expect(_formatEpisodeCode(1, null)).toBe('');
        expect(_formatEpisodeCode(undefined, undefined)).toBe('');
    });
});

describe('_formatTemplate', () => {
    it('substitutes named placeholders', () => {
        expect(_formatTemplate('{watched}/{total} episodes', { watched: 3, total: 10 })).toBe('3/10 episodes');
    });

    it('leaves unknown placeholders untouched', () => {
        expect(_formatTemplate('{missing} thing', { watched: 1 })).toBe('{missing} thing');
    });
});
