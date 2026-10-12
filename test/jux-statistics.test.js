import { describe, it, expect } from 'vitest';
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
