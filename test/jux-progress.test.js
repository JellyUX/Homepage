import { describe, it, expect, afterEach, vi } from 'vitest';
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
        const html = _buildShellHtml('en', { sortBy: 'SortName', sortOrder: 'Ascending' });

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

describe('_decorateCards', () => {
    const t = { episodesWatched: '{watched}/{total}', lastEpisode: '{code} {name}', markWatched: 'Mark as watched', unwatched: '{count} unwatched' };

    function decorate() {
        const container = document.createElement('div');
        container.innerHTML = '<div class="card" data-id="s1"><div class="cardBox"></div></div>';
        juxProgress._decorateCards(container, [{ Item: { Id: 's1' }, WatchedEpisodes: 1, TotalEpisodes: 3 }], t);
        return container;
    }

    it('styles the mark-watched control as a JellyUX pill, not as the unstyled native button-flat', () => {
        const button = decorate().querySelector('.jux-progress-mark-watched:not(.jux-progress-unwatched)');

        expect(button.classList.contains('button-flat')).toBe(false);
        expect(button.title).toBe('Mark as watched');
        expect(button.querySelector('.jux-progress-mark-watched-label').textContent).toBe('Mark as watched');
    });

    it('marks the card so its info block can push the button to a shared baseline', () => {
        const card = decorate().querySelector('.card');

        expect(card.classList.contains('jux-progress-card')).toBe(true);
        expect(card.lastElementChild.className).toBe('jux-progress-info');
    });
});

describe('unwatched episodes dialog (F-55)', () => {
    const t = {
        episodesWatched: '{watched}/{total}', lastEpisode: '{code} {name}', markWatched: 'Mark as watched',
        unwatched: '{count} unwatched', unwatchedTitle: 'Unwatched episodes', unwatchedEmpty: 'None.',
        unwatchedMore: '{count} more', airDate: 'Aired {date}', markEpisodeWatched: 'Mark {episode} as watched'
    };

    function settle() {
        return new Promise((resolve) => setTimeout(resolve, 0));
    }

    function mount(watched, total, episodes) {
        document.documentElement.lang = 'en';
        document.body.innerHTML = '<div class="items"><div class="card" data-id="s1"><button class="opener">x</button></div></div>';
        window.ApiClient = {
            getCurrentUserId: () => 'u1',
            serverId: () => 'srv',
            getScaledImageUrl: (id) => '/Items/' + id + '/Images/Primary',
            getItems: vi.fn(() => Promise.resolve({ Items: episodes, TotalRecordCount: episodes.length })),
            markPlayed: vi.fn(() => Promise.resolve())
        };
        const row = { Item: { Id: 's1', Name: 'Show' }, WatchedEpisodes: watched, TotalEpisodes: total };
        juxProgress._decorateCards(document.querySelector('.items'), [row], t);
        return document.querySelector('.card');
    }

    afterEach(() => {
        delete window.ApiClient;
        document.body.innerHTML = '';
    });

    it('counts the unwatched episodes on the card and reads them from the series when opened', async () => {
        const card = mount(1, 3, [{ Id: 'e2', Name: 'Two', ParentIndexNumber: 1, IndexNumber: 2, PremiereDate: '2026-03-01T00:00:00.0000000Z' }]);
        const button = card.querySelector('.jux-progress-unwatched');
        expect(button.textContent).toBe('2 unwatched');

        button.click();
        await settle();

        expect(window.ApiClient.getItems).toHaveBeenCalledWith('u1', expect.objectContaining({ ParentId: 's1', IncludeItemTypes: 'Episode', Recursive: true, IsPlayed: false, IsMissing: false }));
        const link = document.querySelector('.jux-episode-link');
        expect(link.textContent).toBe('S01E02 - Two');
        expect(link.getAttribute('href')).toBe('#/details?id=e2&serverId=srv');
        expect(document.querySelector('.jux-episode-date').textContent).toBe('Aired Mar 1, 2026');
        expect(document.activeElement).toBe(document.querySelector('.jux-episode-watched'));
    });

    it('updates the card after each episode marked, and removes it with the last one', async () => {
        const card = mount(1, 3, [
            { Id: 'e2', Name: 'Two', ParentIndexNumber: 1, IndexNumber: 2 },
            { Id: 'e3', Name: 'Three', ParentIndexNumber: 1, IndexNumber: 3 }
        ]);
        card.querySelector('.jux-progress-unwatched').click();
        await settle();

        document.querySelector('.jux-episode-watched').click();
        await settle();
        expect(window.ApiClient.markPlayed).toHaveBeenCalledWith('u1', 'e2', expect.any(Date));
        expect(document.querySelectorAll('.jux-episode')).toHaveLength(1);
        expect(card.querySelector('.jux-progress-counts').textContent).toContain('2/3');
        expect(card.querySelector('.jux-progress-unwatched').title).toBe('1 unwatched');

        document.querySelector('.jux-episode-watched').click();
        await settle();
        expect(card.style.display).toBe('none');
        expect(document.querySelector('.jux-episodes-dialog')).toBeNull();
    });

    it('reads an air date as a UTC calendar day', () => {
        expect(juxProgress._formatAirDate('2026-03-01T00:00:00.0000000Z', 'en')).toBe('Mar 1, 2026');
        expect(juxProgress._formatAirDate(null, 'en')).toBe('');
    });
});
