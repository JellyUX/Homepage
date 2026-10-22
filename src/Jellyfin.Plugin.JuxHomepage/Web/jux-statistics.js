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

            window.JuxCore.getStatistics(userId).then(function (stats) {
                body.innerHTML = _buildTilesHtml(stats, lang) + _buildTopSeriesHtml(stats, lang, window.ApiClient.serverId());
                var seeProgress = body.querySelector('.jux-top-series-more');
                if (seeProgress) {
                    seeProgress.addEventListener('click', _openProgressTab);
                }
            }).catch(function (err) {
                console.error('[JellyUX] Statistics fetch failed:', err);
                window.JuxCore.showError(body, {
                    name: window.JuxCore.labels('statistics', lang).title,
                    onRetry: function () { self.render(); }
                });
            });
        }
    };

    function _buildTitleHtml(lang) {
        return '<h2 class="sectionTitle sectionTitle-cards jux-section-title-container padded-left padded-right">' + window.JuxCore.escHtml(window.JuxCore.labels('statistics', lang).title) + '</h2>';
    }

    function _buildTilesHtml(stats, lang) {
        var t = window.JuxCore.labels('statistics', lang);
        var buildCard = (typeof window !== 'undefined' && window.JuxUI) ? window.JuxUI.buildStatCard : _fallbackTile;

        return '<div class="jux-statistics-tiles padded-left padded-right">' +
            buildCard('movie', stats.MoviesWatched, t.moviesWatched) +
            buildCard('live_tv', stats.SeriesTracked, t.seriesTracked) +
            buildCard('check_circle', stats.SeriesCompleted, t.seriesCompleted) +
            buildCard('playlist_play', stats.EpisodesWatched, t.episodesWatched) +
            '</div>';
    }

    // The most watched series (TODO_V4.md 8.5, F-55), ranked by the server: each row links to the
    // series, and "See progress" opens the Progress tab, which lists every series in progress.
    function _buildTopSeriesHtml(stats, lang, serverId) {
        var rows = (stats && stats.TopSeries) || [];
        if (rows.length === 0) {
            return '';
        }

        var t = window.JuxCore.labels('statistics', lang);
        var esc = window.JuxCore.escHtml;
        var html = '<section class="jux-top-series padded-left padded-right" aria-labelledby="jux-top-series-title">' +
            '<h3 class="jux-top-series-title" id="jux-top-series-title">' + esc(t.topSeries) + '</h3>' +
            '<ol class="jux-top-series-list">';
        rows.forEach(function (row, index) {
            var percent = row.TotalEpisodes > 0 ? Math.floor(row.WatchedEpisodes * 100 / row.TotalEpisodes) : 0;
            var detail = t.topSeriesEpisodes
                .replace('{watched}', row.WatchedEpisodes)
                .replace('{total}', row.TotalEpisodes)
                .replace('{percent}', percent);
            html += '<li class="jux-top-series-row">' +
                '<span class="jux-top-series-rank" aria-hidden="true">' + (index + 1) + '</span>' +
                '<div class="jux-top-series-body">' +
                '<a class="jux-top-series-name" href="#/details?id=' + encodeURIComponent(row.SeriesId) + '&serverId=' + encodeURIComponent(serverId || '') + '">' + esc(row.Name) + '</a>' +
                (window.JuxUI ? window.JuxUI.buildProgressBar(row.WatchedEpisodes, row.TotalEpisodes) : '') +
                '<span class="jux-top-series-detail">' + esc(detail) + '</span>' +
                '</div>' +
                '</li>';
        });
        html += '</ol>';
        if (document.getElementById('jux-tabbtn-progress')) {
            html += '<button type="button" class="jux-sort-button jux-top-series-more">' +
                '<span class="jux-sort-button-label">' + esc(t.seeProgress) + '</span>' +
                '<span class="material-icons chevron_right" aria-hidden="true"></span></button>';
        }
        return html + '</section>';
    }

    function _openProgressTab() {
        var tabButton = document.getElementById('jux-tabbtn-progress');
        if (tabButton) {
            tabButton.click();
        }
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
            _buildTilesHtml: _buildTilesHtml,
            _buildTopSeriesHtml: _buildTopSeriesHtml
        };
    }
})();
