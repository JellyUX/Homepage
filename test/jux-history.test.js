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

describe('watch date and tiles (F-58)', () => {
    const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

    afterEach(() => {
        delete window.ApiClient;
    });

    it('adds the absolute watch date to the text block of each card', () => {
        document.documentElement.lang = 'en';
        const container = document.createElement('div');
        container.innerHTML =
            '<div class="card" data-id="m1"><div class="cardBox"><div class="cardText cardTextCentered cardText-first">Film</div><div class="cardText cardTextCentered cardText-secondary">2024</div></div></div>' +
            '<div class="card" data-id="m2"><div class="cardBox"><div class="cardText cardText-first">Never</div></div></div>';
        juxHistory._decorateCards(container, [
            { Id: 'm1', UserData: { LastPlayedDate: '2026-07-21T22:23:00.000Z' } },
            { Id: 'm2', UserData: {} }
        ], { watchedOn: 'Watched {date}', addFavorite: 'a', removeFavorite: 'r' });

        const line = container.querySelector('[data-id="m1"] .jux-history-date');
        expect(line.className).toBe('cardText cardTextCentered cardText-secondary jux-history-date');
        expect(line.previousElementSibling.textContent).toBe('2024');
        expect(line.textContent).toBe('Watched ' + juxHistory._formatWatchedDate('2026-07-21T22:23:00.000Z', 'en'));
        expect(line.textContent).toMatch(/2026/);
        expect(line.title).toBe('Watched ' + juxHistory._formatWatchedDate('2026-07-21T22:23:00.000Z', 'en', true));
        expect(line.title.length).toBeGreaterThan(line.textContent.length);
        expect(container.querySelector('[data-id="m2"] .jux-history-date')).toBeNull();
    });

    it('shows the movies watched and those of this month above the list', async () => {
        window.ApiClient = {
            getCurrentUserId: () => 'u1',
            getUrl: (route, query) => route + '?' + new URLSearchParams(query).toString(),
            getJSON: vi.fn(() => Promise.resolve({ MoviesWatched: 120, MoviesWatchedThisMonth: 4 }))
        };
        const container = document.createElement('div');

        juxHistory._renderTiles(container, 'fr');
        await settle();

        expect(window.ApiClient.getJSON).toHaveBeenCalledWith('JuxHomepage/Statistics?userId=u1');
        expect([...container.querySelectorAll('.jux-stat-value')].map((el) => el.textContent)).toEqual(['120', '4']);
        expect([...container.querySelectorAll('.jux-stat-label')].map((el) => el.textContent)).toEqual(['Films vus', 'Ce mois-ci']);
    });

    it('leaves the tiles out when the counters cannot be read', async () => {
        // Another user: the counters of u1 read above are kept for a minute (JuxCore.getStatistics).
        window.ApiClient = {
            getCurrentUserId: () => 'u2',
            getUrl: (route) => route,
            getJSON: vi.fn(() => Promise.reject(new Error('down')))
        };
        const parent = document.createElement('div');
        const container = document.createElement('div');
        parent.appendChild(container);
        vi.spyOn(console, 'error').mockImplementation(() => {});

        juxHistory._renderTiles(container, 'en');
        await settle();

        expect(parent.children).toHaveLength(0);
    });
});
