import { describe, it, expect, vi } from 'vitest';
import juxSeriesFlatten from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-series-flatten.js';

const { _shouldFlatten, _seasonTitle, _buildFlattenedSectionSkeleton, _currentDetailItemId, _renderFlattened } = juxSeriesFlatten;

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


// R11 (phase 06, BKL-39 sisters): an episode list the server never answers fails after the request
// delay and is asked again on the next pass, instead of holding the series page for good.
describe('_renderFlattened on a silent server', () => {
    it('gives up after the request delay and asks again on the next pass', async () => {
        vi.useFakeTimers();
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const page = document.createElement('div');
        page.innerHTML = '<div id="listChildrenCollapsible"></div>';
        const getJSON = vi.fn(() => new Promise(() => {}));
        window.ApiClient = {
            getUrl: (path) => path,
            getItem: vi.fn().mockResolvedValue({ Id: 'sf1', Type: 'Series', ChildCount: 1 }),
            getJSON
        };

        const first = _renderFlattened(page, 'u1', 'sf1');
        await vi.advanceTimersByTimeAsync(15000);
        await first;
        expect(errorSpy).toHaveBeenCalledWith('[JellyUX] Series flatten check failed:', expect.any(Error));
        _renderFlattened(page, 'u1', 'sf1');
        await vi.advanceTimersByTimeAsync(0);
        expect(getJSON).toHaveBeenCalledTimes(2);

        errorSpy.mockRestore();
        delete window.ApiClient;
        vi.useRealTimers();
    });
});
