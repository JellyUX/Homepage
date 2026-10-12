import { describe, it, expect } from 'vitest';
import juxSeriesFlatten from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-series-flatten.js';

const { _shouldFlatten, _seasonTitle, _buildFlattenedSectionSkeleton, _currentDetailItemId } = juxSeriesFlatten;

describe('_shouldFlatten', () => {
    it('returns true for a single-season series', () => {
        expect(_shouldFlatten({ Type: 'Series', ChildCount: 1 })).toBe(true);
    });

    it('returns false for a series with more than one season', () => {
        expect(_shouldFlatten({ Type: 'Series', ChildCount: 3 })).toBe(false);
    });

    it('returns false for a non-series item', () => {
        expect(_shouldFlatten({ Type: 'Movie', ChildCount: 1 })).toBe(false);
    });

    it('returns false for a nullish item', () => {
        expect(_shouldFlatten(null)).toBe(false);
        expect(_shouldFlatten(undefined)).toBe(false);
    });
});

describe('_seasonTitle', () => {
    it('uses the episode SeasonName when present', () => {
        expect(_seasonTitle({ SeasonName: 'Miniseries', ParentIndexNumber: 1 })).toBe('Miniseries');
    });

    it('falls back to "Season {number}" when SeasonName is missing', () => {
        expect(_seasonTitle({ ParentIndexNumber: 2 })).toBe('Season 2');
    });

    it('falls back to season 1 when ParentIndexNumber is also missing', () => {
        expect(_seasonTitle({})).toBe('Season 1');
    });
});

describe('_buildFlattenedSectionSkeleton', () => {
    it('renders an escaped title and an empty items container', () => {
        const section = _buildFlattenedSectionSkeleton('<script>Alert</script>');

        expect(section.className).toContain('jux-flattened-season-section');
        expect(section.querySelector('.sectionTitle').textContent).toBe('<script>Alert</script>');
        expect(section.innerHTML).not.toContain('<script>Alert</script>');
        const itemsContainer = section.querySelector('.itemsContainer');
        expect(itemsContainer).not.toBeNull();
        expect(itemsContainer.children.length).toBe(0);
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

