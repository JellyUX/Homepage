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
            markWatched: 'Mark as watched',
            unwatched: '{count} unwatched',
            unwatchedTitle: 'Unwatched episodes',
            unwatchedEmpty: 'Every episode is watched.',
            unwatchedMore: '{count} more episodes not listed',
            airDate: 'Aired {date}',
            markEpisodeWatched: 'Mark {episode} as watched'
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
            markWatched: 'Marquer comme vu',
            unwatched: '{count} non vus',
            unwatchedTitle: 'Épisodes non vus',
            unwatchedEmpty: 'Tous les épisodes sont vus.',
            unwatchedMore: '{count} autres épisodes non listés',
            airDate: 'Diffusé le {date}',
            markEpisodeWatched: 'Marquer {episode} comme vu'
        }
    };

    function _sortOptions(t) {
        return [
            { value: 'LastPlayed', label: t.sortLastPlayed },
            { value: 'Name', label: t.sortName }
        ];
    }

    // The unwatched episodes listed in the dialog at most; a longer remainder is counted, not listed.
    var EPISODE_LIMIT = 100;

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

            var counts = document.createElement('div');
            counts.className = 'jux-progress-counts';
            info.appendChild(counts);

            if (row.LastEpisodeName) {
                var lastLine = document.createElement('div');
                lastLine.textContent = _formatTemplate(t.lastEpisode, {
                    code: _formatEpisodeCode(row.LastEpisodeSeasonNumber, row.LastEpisodeIndexNumber),
                    name: row.LastEpisodeName
                });
                info.appendChild(lastLine);
            }

            var actions = document.createElement('div');
            actions.className = 'jux-progress-actions';

            var unwatched = _pillButton('jux-progress-unwatched', 'playlist_play', '');
            unwatched.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                _openUnwatchedDialog(row, card, t);
            });
            actions.appendChild(unwatched);

            var button = _pillButton('', null, t.markWatched);
            button.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                _markSeriesWatched(row.Item.Id, card, button, t);
            });
            actions.appendChild(button);
            info.appendChild(actions);

            card.classList.add('jux-progress-card');
            card.appendChild(info);
            _renderCounts(card, row, t);
        });
    }

    function _pillButton(className, icon, label) {
        var button = document.createElement('button');
        button.type = 'button';
        button.className = ('jux-progress-mark-watched ' + className).trim();
        if (icon) {
            var iconEl = document.createElement('span');
            iconEl.className = 'material-icons ' + icon;
            iconEl.setAttribute('aria-hidden', 'true');
            button.appendChild(iconEl);
        }
        var labelEl = document.createElement('span');
        labelEl.className = 'jux-progress-mark-watched-label';
        labelEl.textContent = label;
        button.appendChild(labelEl);
        if (label) {
            button.title = label;
        }
        return button;
    }

    // The progress bar, the "N/M episodes" line and the unwatched count of a card, drawn again
    // after each episode marked in the dialog.
    function _renderCounts(card, row, t) {
        var counts = card.querySelector('.jux-progress-counts');
        counts.innerHTML = window.JuxUI.buildProgressBar(row.WatchedEpisodes, row.TotalEpisodes);
        var line = document.createElement('div');
        line.textContent = _formatTemplate(t.episodesWatched, { watched: row.WatchedEpisodes, total: row.TotalEpisodes });
        counts.appendChild(line);

        var label = _formatTemplate(t.unwatched, { count: Math.max(0, row.TotalEpisodes - row.WatchedEpisodes) });
        var button = card.querySelector('.jux-progress-unwatched');
        button.querySelector('.jux-progress-mark-watched-label').textContent = label;
        button.title = label;
    }

    // The series' unwatched episodes, read when the dialog opens (Jellyfin's own /Items route, the
    // episodes the Progress counts are made of: not virtual, every season), each with a "watched"
    // button that updates the card. The last one closes the dialog and the card leaves the tab, like
    // a finished series does (TODO_V4.md 8.2).
    function _openUnwatchedDialog(row, card, t) {
        if (!window.ApiClient) { return; }
        var userId = window.ApiClient.getCurrentUserId();
        var opened = window.JuxUI.openDialog({
            title: t.unwatchedTitle + ' - ' + (row.Item.Name || ''),
            className: 'jux-sort-dialog jux-episodes-dialog',
            contentClassName: 'jux-episodes-dialog-content'
        });
        var content = opened.dialog.querySelector('.jux-episodes-dialog-content');

        function load() {
            window.JuxUI.showLoading(content);
            window.ApiClient.getItems(userId, {
                ParentId: row.Item.Id,
                IncludeItemTypes: 'Episode',
                Recursive: true,
                IsPlayed: false,
                IsMissing: false,
                SortBy: 'ParentIndexNumber,IndexNumber,SortName',
                SortOrder: 'Ascending',
                Fields: 'PremiereDate',
                EnableImageTypes: 'Primary',
                ImageTypeLimit: 1,
                EnableUserData: false,
                Limit: EPISODE_LIMIT
            }).then(function (result) {
                _renderEpisodes(content, result, row, card, opened, t);
            }).catch(function (err) {
                console.error('[JellyUX] Unwatched episodes fetch failed:', err);
                window.JuxCore.showError(content, { name: t.unwatchedTitle, onRetry: load });
            });
        }

        load();
    }

    function _renderEpisodes(content, result, row, card, opened, t) {
        var episodes = (result && result.Items) || [];
        if (episodes.length === 0) {
            content.innerHTML = '<p class="jux-episodes-empty">' + window.JuxCore.escHtml(t.unwatchedEmpty) + '</p>';
            return;
        }

        var lang = window.JuxCore.resolveLang();
        var serverId = window.ApiClient.serverId();
        var html = '<ul class="jux-episodes-list">' + episodes.map(function (episode) {
            return _episodeRowHtml(episode, serverId, lang, t);
        }).join('') + '</ul>';
        var rest = ((result && result.TotalRecordCount) || 0) - episodes.length;
        if (rest > 0) {
            html += '<p class="jux-episodes-more">' + window.JuxCore.escHtml(_formatTemplate(t.unwatchedMore, { count: rest })) + '</p>';
        }
        content.innerHTML = html;

        content.querySelectorAll('.jux-episode-watched').forEach(function (button) {
            button.addEventListener('click', function () {
                _markEpisodeWatched(button, row, card, opened, t);
            });
        });
        content.querySelectorAll('.jux-episode-link').forEach(function (link) {
            link.addEventListener('click', function () { opened.close(); });
        });

        var first = content.querySelector('.jux-episode-watched');
        if (first) {
            first.focus();
        }
    }

    function _episodeRowHtml(episode, serverId, lang, t) {
        var esc = window.JuxCore.escHtml;
        var code = _formatEpisodeCode(episode.ParentIndexNumber, episode.IndexNumber);
        var label = (code ? code + ' - ' : '') + (episode.Name || '');
        var image = episode.ImageTags && episode.ImageTags.Primary
            ? '<img class="jux-episode-image" loading="lazy" alt="" src="' + esc(window.ApiClient.getScaledImageUrl(episode.Id, { type: 'Primary', maxWidth: 240, tag: episode.ImageTags.Primary })) + '">'
            : '<span class="jux-episode-image jux-episode-image-empty material-icons tv" aria-hidden="true"></span>';
        var date = _formatAirDate(episode.PremiereDate, lang);
        var markLabel = _formatTemplate(t.markEpisodeWatched, { episode: code || episode.Name || '' });

        return '<li class="jux-episode" data-id="' + esc(episode.Id) + '">' + image +
            '<div class="jux-episode-text">' +
            '<a class="jux-episode-link" href="#/details?id=' + encodeURIComponent(episode.Id) + '&serverId=' + encodeURIComponent(serverId || '') + '">' + esc(label) + '</a>' +
            (date ? '<span class="jux-episode-date">' + esc(_formatTemplate(t.airDate, { date: date })) + '</span>' : '') +
            '</div>' +
            '<button type="button" class="jux-episode-watched" title="' + esc(markLabel) + '" aria-label="' + esc(markLabel) + '">' +
            '<span class="material-icons check" aria-hidden="true"></span></button>' +
            '</li>';
    }

    // An air date is a calendar day stored as midnight UTC: read in UTC, or a viewer west of
    // Greenwich sees the day before.
    function _formatAirDate(value, lang) {
        if (!value) { return ''; }
        var date = new Date(value);
        if (isNaN(date.getTime())) { return ''; }
        return date.toLocaleDateString(lang, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
    }

    function _markEpisodeWatched(button, row, card, opened, t) {
        var item = button.closest('.jux-episode');
        var userId = window.ApiClient.getCurrentUserId();
        button.disabled = true;
        window.ApiClient.markPlayed(userId, item.getAttribute('data-id'), new Date()).then(function () {
            var next = item.nextElementSibling || item.previousElementSibling;
            item.remove();
            row.WatchedEpisodes = Math.min(row.TotalEpisodes, row.WatchedEpisodes + 1);
            _renderCounts(card, row, t);
            if (row.WatchedEpisodes >= row.TotalEpisodes) {
                card.style.display = 'none';
                opened.close();
                return;
            }
            var focus = next && next.querySelector('.jux-episode-watched');
            if (focus) {
                focus.focus();
            }
        }).catch(function (err) {
            console.error('[JellyUX] Failed to mark episode as watched:', err);
            button.disabled = false;
        });
    }

    // Marks the whole series as watched via Jellyfin's own native endpoint (same approach as the
    // favorite toggle in jux-history.js) -- POST /Users/{userId}/PlayedItems/{itemId} cascades to
    // every episode server-side, no JellyUX endpoint needed. A finished series leaves Progress
    // (TODO_V4.md 8.2), so the card is removed from view at once.
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
            _formatAirDate: _formatAirDate,
            _decorateCards: _decorateCards,
            _openUnwatchedDialog: _openUnwatchedDialog
        };
    }
})();
