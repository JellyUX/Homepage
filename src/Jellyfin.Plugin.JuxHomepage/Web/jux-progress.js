'use strict';

// JellyUX Series Progress tab rendering.
// TODO_V3.md Phase 6.1, visually reworked in Phase 6 bis to use the shared window.JuxUI helpers
// (Web/jux-ui.js) instead of a bare <select>/text-only progress and inline styles: a native-look
// sort dialog, a loading spinner, an icon-based empty state, and a real visual progress bar per card
// (the plain "3/10 episodes" text line is kept alongside it, not replaced).
//
// Built on the shared list tab of jux-ui.js (JuxUI.createListTab, TODO_V4.md 5.1), like
// jux-watchlist.js and jux-history.js.
//
// Each row is a series -- not just a BaseItemDto, but a { Item, WatchedEpisodes, TotalEpisodes,
// LastEpisodeName, ... } object (see Watchlist/SeriesProgressViewService.cs), so after cardBuilder
// renders the poster/name, this file decorates each card with a progress bar, a progress line, and a
// "mark as watched" button.
(function () {
    if (typeof window.juxProgress !== 'undefined') {
        return;
    }

    var _labels = {
        en: {
            title: 'Progress',
            empty: 'No series in progress',
            emptySubtitle: 'Watch an episode of a series and it will show up here.',
            sortLastPlayed: 'Last Watched',
            sortName: 'Name',
            sortDialogTitle: 'Sort Progress',
            episodesWatched: '{watched}/{total} episodes',
            lastEpisode: 'Last: {code} - {name}',
            markWatched: 'Mark as watched'
        },
        fr: {
            title: 'Progression',
            empty: 'Aucune série en cours',
            emptySubtitle: 'Regarde un épisode d’une série et elle apparaîtra ici.',
            sortLastPlayed: 'Dernier vu',
            sortName: 'Nom',
            sortDialogTitle: 'Trier la progression',
            episodesWatched: '{watched}/{total} épisodes',
            lastEpisode: 'Dernier : {code} - {name}',
            markWatched: 'Marquer comme vu'
        }
    };

    function _sortOptions(t) {
        return [
            { value: 'LastPlayed', label: t.sortLastPlayed },
            { value: 'Name', label: t.sortName }
        ];
    }

    function _decorateCards(container, rows, t) {
        var byId = {};
        for (var i = 0; i < rows.length; i++) {
            byId[rows[i].Item.Id] = rows[i];
        }

        var cards = container.querySelectorAll('.card[data-id]');
        Array.prototype.forEach.call(cards, function (card) {
            var row = byId[card.getAttribute('data-id')];
            if (!row) { return; }

            var info = document.createElement('div');
            info.className = 'jux-progress-info';

            info.innerHTML = window.JuxUI.buildProgressBar(row.WatchedEpisodes, row.TotalEpisodes);

            var countsLine = document.createElement('div');
            countsLine.textContent = _formatTemplate(t.episodesWatched, { watched: row.WatchedEpisodes, total: row.TotalEpisodes });
            info.appendChild(countsLine);

            if (row.LastEpisodeName) {
                var lastLine = document.createElement('div');
                lastLine.textContent = _formatTemplate(t.lastEpisode, {
                    code: _formatEpisodeCode(row.LastEpisodeSeasonNumber, row.LastEpisodeIndexNumber),
                    name: row.LastEpisodeName
                });
                info.appendChild(lastLine);
            }

            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'jux-progress-mark-watched';
            button.title = t.markWatched;
            var label = document.createElement('span');
            label.className = 'jux-progress-mark-watched-label';
            label.textContent = t.markWatched;
            button.appendChild(label);
            button.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                _markSeriesWatched(row.Item.Id, card, button, t);
            });
            info.appendChild(button);

            card.classList.add('jux-progress-card');
            card.appendChild(info);
        });
    }

    // Marks the whole series as watched via Jellyfin's own native endpoint (same approach as the
    // favorite toggle in jux-history.js) -- POST /Users/{userId}/PlayedItems/{itemId} cascades to
    // every episode server-side, no JellyUX endpoint needed. The Series Progress list itself is a
    // once-a-day server cache (WatchlistDailyRefreshTask), so rather than re-fetching (which would
    // still show the old counts until the next refresh), this optimistically removes the card from
    // view -- it reappears correctly counted after the next daily cache refresh.
    function _markSeriesWatched(itemId, card, button, t) {
        if (!window.ApiClient) { return; }
        var userId = window.ApiClient.getCurrentUserId();

        button.disabled = true;
        window.ApiClient.markPlayed(userId, itemId, new Date()).then(function () {
            card.style.display = 'none';
        }).catch(function (err) {
            console.error('[JellyUX] Failed to mark series as watched:', err);
            button.disabled = false;
        });
    }

    function _formatEpisodeCode(seasonNumber, episodeNumber) {
        if (seasonNumber == null || episodeNumber == null) {
            return '';
        }
        return 'S' + _padTwoDigits(seasonNumber) + 'E' + _padTwoDigits(episodeNumber);
    }

    function _padTwoDigits(value) {
        var str = String(value);
        return str.length >= 2 ? str : '0' + str;
    }

    function _formatTemplate(template, values) {
        return template.replace(/\{(\w+)\}/g, function (match, key) {
            return Object.prototype.hasOwnProperty.call(values, key) ? String(values[key]) : match;
        });
    }

    window.juxProgress = window.JuxUI.createListTab({
        id: 'progress',
        endpoint: 'JuxHomepage/SeriesProgress/Items',
        logName: 'Series Progress',
        labels: _labels,
        state: { sortBy: 'LastPlayed', sortOrder: 'Descending' },
        sortOptions: _sortOptions,
        emptyIcon: 'movie_filter',
        itemOf: function (row) { return row.Item; },
        decorate: _decorateCards
    });

    window.juxProgress.init();

    // Guarded UMD-lite export (same convention as jux-watchlist.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _sortOptions: _sortOptions,
            _buildShellHtml: window.juxProgress.buildShellHtml,
            _formatEpisodeCode: _formatEpisodeCode,
            _formatTemplate: _formatTemplate,
            _decorateCards: _decorateCards
        };
    }
})();
