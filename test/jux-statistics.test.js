import { describe, it, expect, afterEach } from 'vitest';
import juxStatistics from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-statistics.js';

const { _buildTitleHtml, _buildTilesHtml } = juxStatistics;

describe('_buildTitleHtml', () => {
    it('renders the localized section title', () => {
        expect(_buildTitleHtml('en')).toContain('Statistics');
        expect(_buildTitleHtml('fr')).toContain('Statistiques');
    });
});

describe('_buildTilesHtml', () => {
    // window.JuxUI is not loaded in this test environment, so this exercises the built-in fallback
    // tile renderer -- the JuxUI.buildStatCard path itself is covered by test/jux-ui.test.js.
    it('renders all four counters with localized labels', () => {
        const stats = { MoviesWatched: 5, SeriesTracked: 3, SeriesCompleted: 1, EpisodesWatched: 42 };

        const htmlEn = _buildTilesHtml(stats, 'en');
        expect(htmlEn).toContain('5');
        expect(htmlEn).toContain('Movies watched');
        expect(htmlEn).toContain('3');
        expect(htmlEn).toContain('Series tracked');
        expect(htmlEn).toContain('1');
        expect(htmlEn).toContain('Series completed');
        expect(htmlEn).toContain('42');
        expect(htmlEn).toContain('Episodes watched');

        const htmlFr = _buildTilesHtml(stats, 'fr');
        expect(htmlFr).toContain('Films vus');
        expect(htmlFr).toContain('Séries suivies');
        expect(htmlFr).toContain('Séries terminées');
        expect(htmlFr).toContain('Épisodes vus');
    });
});

describe('gutters (F-46)', () => {
    it('places the title and the tiles on the native page gutters', () => {
        const container = document.createElement('div');
        container.innerHTML = juxStatistics._buildTitleHtml('en') +
            juxStatistics._buildTilesHtml({ MoviesWatched: 1, SeriesTracked: 2, SeriesCompleted: 3, EpisodesWatched: 4 }, 'en');

        for (const el of container.children) {
            expect(el.classList.contains('padded-left') && el.classList.contains('padded-right')).toBe(true);
        }
    });
});

describe('_buildTopSeriesHtml (F-55)', () => {
    const top = [
        { SeriesId: 's1', Name: 'Long <Show>', WatchedEpisodes: 40, TotalEpisodes: 40 },
        { SeriesId: 's2', Name: 'Short', WatchedEpisodes: 2, TotalEpisodes: 3 }
    ];

    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('lists the ranked series as links to their page, with their progress', () => {
        document.body.innerHTML = juxStatistics._buildTopSeriesHtml({ TopSeries: top }, 'fr', 'srv');

        const rows = document.querySelectorAll('.jux-top-series-row');
        expect(rows).toHaveLength(2);
        expect(document.querySelector('.jux-top-series-title').textContent).toBe('Séries les plus regardées');
        expect(rows[0].querySelector('.jux-top-series-rank').textContent).toBe('1');
        expect(rows[0].querySelector('.jux-top-series-name').textContent).toBe('Long <Show>');
        expect(rows[0].querySelector('.jux-top-series-name').getAttribute('href')).toBe('#/details?id=s1&serverId=srv');
        expect(rows[1].querySelector('.jux-top-series-detail').textContent).toBe('2 sur 3 épisodes (66 %)');
        expect(rows[1].querySelector('.jux-progress-bar')).not.toBeNull();
    });

    it('opens the Progress tab from "See progress" only when that tab exists', () => {
        expect(juxStatistics._buildTopSeriesHtml({ TopSeries: top }, 'en', 'srv')).not.toContain('jux-top-series-more');

        document.body.innerHTML = '<button id="jux-tabbtn-progress"></button>';
        expect(juxStatistics._buildTopSeriesHtml({ TopSeries: top }, 'en', 'srv')).toContain('See progress');
    });

    it('leaves the block out when no series is watched', () => {
        expect(juxStatistics._buildTopSeriesHtml({ TopSeries: [] }, 'en', 'srv')).toBe('');
        expect(juxStatistics._buildTopSeriesHtml({}, 'en', 'srv')).toBe('');
    });
});
