'use strict';

// JellyUX Statistics tab rendering.
// TODO_V3.md Phase 6.3, visually reworked in Phase 6 bis to use the shared window.JuxUI helpers
// (Web/jux-ui.js) instead of bare inline-styled text tiles: a real section title, a loading spinner,
// and window.JuxUI.buildStatCard for each counter (icon, big value, label -- styled card, not plain
// stacked text).
//
// Same click-delegation convention as jux-watchlist.js/jux-progress.js/jux-history.js, but this view
// is not a card grid -- just a handful of stat tiles built from GET JuxHomepage/Statistics
// (Watchlist/StatisticsService.cs, purely derived from the already-cached Series Progress / Movie
// History data, no new library scan).
(function () {
    if (typeof window.juxStatistics !== 'undefined') {
        return;
    }

    var _labels = {
        en: {
            title: 'Statistics',
            moviesWatched: 'Movies watched',
            seriesTracked: 'Series tracked',
            seriesCompleted: 'Series completed',
            episodesWatched: 'Episodes watched'
        },
        fr: {
            title: 'Statistiques',
            moviesWatched: 'Films vus',
            seriesTracked: 'Séries suivies',
            seriesCompleted: 'Séries terminées',
            episodesWatched: 'Épisodes vus'
        }
    };

    window.juxStatistics = {
        init: function () {
            var self = this;
            document.addEventListener('click', function (event) {
                if (event.target.closest('#jux-tabbtn-statistics')) {
                    self.render();
                }
            });
        },

        render: function () {
            var self = this;
            var pane = document.getElementById('jux-tab-statistics');
            if (!pane) {
                return;
            }

            var sections = pane.querySelector('.sections');
            if (!sections) {
                return;
            }

            var lang = window.JuxCore.resolveLang();
            var userId = window.ApiClient && window.ApiClient.getCurrentUserId();
            if (!userId) {
                return;
            }

            sections.innerHTML = _buildTitleHtml(lang) + '<div class="jux-statistics-body"></div>';
            var body = sections.querySelector('.jux-statistics-body');
            if (window.JuxUI) { window.JuxUI.showLoading(body); }

            var url = window.ApiClient.getUrl('JuxHomepage/Statistics', { userId: userId });

            window.ApiClient.getJSON(url).then(function (stats) {
                body.innerHTML = _buildTilesHtml(stats, lang);
            }).catch(function (err) {
                console.error('[JellyUX] Statistics fetch failed:', err);
                window.JuxCore.showError(body, {
                    name: _labels[lang].title,
                    onRetry: function () { self.render(); }
                });
            });
        }
    };

    function _buildTitleHtml(lang) {
        return '<h2 class="sectionTitle sectionTitle-cards jux-section-title-container">' + window.JuxCore.escHtml(_labels[lang].title) + '</h2>';
    }

    function _buildTilesHtml(stats, lang) {
        var t = _labels[lang];
        var buildCard = (typeof window !== 'undefined' && window.JuxUI) ? window.JuxUI.buildStatCard : _fallbackTile;

        return '<div class="jux-statistics-tiles">' +
            buildCard('movie', stats.MoviesWatched, t.moviesWatched) +
            buildCard('live_tv', stats.SeriesTracked, t.seriesTracked) +
            buildCard('check_circle', stats.SeriesCompleted, t.seriesCompleted) +
            buildCard('playlist_play', stats.EpisodesWatched, t.episodesWatched) +
            '</div>';
    }

    // Only reached if jux-ui.js somehow failed to load -- keeps the tab from rendering nothing.
    function _fallbackTile(icon, value, label) {
        return '<div class="jux-stat-card"><div class="jux-stat-value">' + window.JuxCore.escHtml(String(value)) + '</div>' +
            '<div class="jux-stat-label">' + window.JuxCore.escHtml(label) + '</div></div>';
    }

    window.juxStatistics.init();

    // Guarded UMD-lite export (same convention as jux-watchlist.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _buildTitleHtml: _buildTitleHtml,
            _buildTilesHtml: _buildTilesHtml
        };
    }
})();
