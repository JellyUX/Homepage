import { describe, it, expect, afterEach, vi } from 'vitest';
import juxHistory from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-history.js';

const { _sortOptions, _buildShellHtml } = juxHistory;

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

describe('favorite toggle (F-52)', () => {
    const t = { addFavorite: 'Add to favorites', removeFavorite: 'Remove from favorites' };

    afterEach(() => {
        delete window.ApiClient;
    });

    it('flips the current state on each click, not the state of the first render', async () => {
        const updateFavoriteStatus = vi.fn(() => Promise.resolve());
        window.ApiClient = { getCurrentUserId: () => 'u1', updateFavoriteStatus };
        const container = document.createElement('div');
        container.innerHTML = '<div class="card" data-id="m1"><div class="cardOverlayContainer"></div></div>';
        juxHistory._decorateCards(container, [{ Id: 'm1', UserData: { IsFavorite: false } }], t);
        const button = container.querySelector('.jux-history-favorite-toggle');
        const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

        button.click();
        await settle();
        expect(button.title).toBe('Remove from favorites');
        button.click();
        await settle();

        expect(updateFavoriteStatus.mock.calls.map((call) => call[2])).toEqual([true, false]);
        expect(button.title).toBe('Add to favorites');
        expect(button.querySelector('.material-icons').classList.contains('favorite_border')).toBe(true);
    });

    it('ignores a second click while the first request is pending', () => {
        const updateFavoriteStatus = vi.fn(() => new Promise(() => {}));
        window.ApiClient = { getCurrentUserId: () => 'u1', updateFavoriteStatus };
        const container = document.createElement('div');
        container.innerHTML = '<div class="card" data-id="m1"><div class="cardOverlayContainer"></div></div>';
        juxHistory._decorateCards(container, [{ Id: 'm1', UserData: { IsFavorite: true } }], t);
        const button = container.querySelector('.jux-history-favorite-toggle');

        button.click();
        button.click();

        expect(updateFavoriteStatus).toHaveBeenCalledTimes(1);
        expect(updateFavoriteStatus.mock.calls[0][2]).toBe(false);
    });
});
