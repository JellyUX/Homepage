'use strict';

// JellyUX Movie History tab rendering.
// TODO_V3.md Phase 6.2, visually reworked in Phase 6 bis to use the shared window.JuxUI helpers
// (Web/jux-ui.js) instead of a bare <select> and inline styles: a native-look sort dialog, a loading
// spinner, and an icon-based empty state.
//
// Built on the shared list tab of jux-ui.js (JuxUI.createListTab, TODO_V4.md 5.1), like
// jux-watchlist.js and jux-progress.js.
//
// Unlike Series Progress, each row here is a plain BaseItemDto (see
// Watchlist/MovieHistoryViewService.cs -- history rows need no extra per-item fields, the last-played
// date is already on BaseItemDto.UserData), so this file only needs to add one thing per card: a
// favorite-toggle button, via Jellyfin's own native endpoint (the same one the classic favorite heart
// uses elsewhere in the app) -- scoped to this view only, not a global card hook.
(function () {
    if (typeof window.juxHistory !== 'undefined') {
        return;
    }

    function _sortOptions(t) {
        return [
            { value: 'LastPlayed', label: t.sortLastPlayed },
            { value: 'Name', label: t.sortName }
        ];
    }

    function _decorateCards(container, items, t) {
        var byId = {};
        for (var i = 0; i < items.length; i++) {
            byId[items[i].Id] = items[i];
        }

        // "Watched on ...": the absolute date and time of the last play, as KefinTweaks shows it
        // (TODO_V4.md 8.5, F-58), as one more text line of the card, styled like the line above it
        // (cardBuilder puts these lines straight in the card box, with no footer around them).
        var lang = window.JuxCore.resolveLang();
        Array.prototype.forEach.call(container.querySelectorAll('.card[data-id]'), function (card) {
            var item = byId[card.getAttribute('data-id')];
            var date = item && _formatWatchedDate(item.UserData && item.UserData.LastPlayedDate, lang);
            var lines = card.querySelectorAll('.cardText');
            var last = lines[lines.length - 1];
            if (!date || !last) { return; }

            var line = document.createElement('div');
            line.className = last.className.replace('cardText-first', 'cardText-secondary') + ' jux-history-date';
            line.textContent = t.watchedOn.replace('{date}', date);
            line.title = t.watchedOn.replace('{date}', _formatWatchedDate(item.UserData.LastPlayedDate, lang, true));
            last.parentNode.insertBefore(line, last.nextSibling);
        });

        var overlays = container.querySelectorAll('.cardOverlayContainer');
        Array.prototype.forEach.call(overlays, function (overlay) {
            var card = overlay.closest('.card[data-id]');
            var item = card && byId[card.getAttribute('data-id')];
            if (!item) { return; }

            // The favorite state lives with the button and follows each confirmed toggle, so every
            // click flips the current state, not the one of the first render (F-52).
            var state = { favorite: !!(item.UserData && item.UserData.IsFavorite) };

            var button = document.createElement('button');
            button.type = 'button';
            button.setAttribute('is', 'paper-icon-button-light');
            button.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light jux-history-favorite-toggle';
            button.title = state.favorite ? t.removeFavorite : t.addFavorite;

            var icon = document.createElement('span');
            icon.className = 'material-icons ' + (state.favorite ? 'favorite' : 'favorite_border');
            icon.setAttribute('aria-hidden', 'true');
            button.appendChild(icon);

            button.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                _toggleFavorite(item.Id, state, button, icon, t);
            });

            overlay.appendChild(button);
        });
    }

    // The card shows the day, which fits a phone-width card; the time is in the tooltip.
    function _formatWatchedDate(value, lang, withTime) {
        if (!value) { return ''; }
        var date = new Date(value);
        if (isNaN(date.getTime())) { return ''; }
        var options = { day: 'numeric', month: 'short', year: 'numeric' };
        if (withTime) {
            options.hour = '2-digit';
            options.minute = '2-digit';
        }
        return date.toLocaleString(lang, options);
    }

    // The two tiles above the list (F-58): every watched movie and those of this month, from the
    // Statistics route (the same History cache, no library query). A failure only leaves them out.
    function _renderTiles(container, lang) {
        if (!container || !window.ApiClient) { return; }
        var t = window.JuxCore.labels('history', lang);
        var url = window.ApiClient.getUrl('JuxHomepage/Statistics', { userId: window.ApiClient.getCurrentUserId() });
        window.ApiClient.getJSON(url).then(function (stats) {
            container.innerHTML =
                window.JuxUI.buildStatCard('movie', stats.MoviesWatched, t.moviesWatched) +
                window.JuxUI.buildStatCard('calendar_month', stats.MoviesWatchedThisMonth, t.moviesThisMonth);
        }).catch(function (err) {
            console.error('[JellyUX] History tiles fetch failed:', err);
            container.remove();
        });
    }

    // Favorite toggle via Jellyfin's own native endpoint (POST/DELETE
    // /Users/{userId}/FavoriteItems/{itemId}) -- no JellyUX endpoint needed, same convention already
    // used for the Watchlist toggle's underlying Likes field.
    function _toggleFavorite(itemId, state, button, icon, t) {
        if (!window.ApiClient || button.disabled) { return; }
        var userId = window.ApiClient.getCurrentUserId();
        var nowFavorite = !state.favorite;

        button.disabled = true;
        window.ApiClient.updateFavoriteStatus(userId, itemId, nowFavorite).then(function () {
            state.favorite = nowFavorite;
            icon.className = 'material-icons ' + (nowFavorite ? 'favorite' : 'favorite_border');
            button.title = nowFavorite ? t.removeFavorite : t.addFavorite;
        }).catch(function (err) {
            console.error('[JellyUX] Failed to toggle favorite state:', err);
        }).then(function () {
            button.disabled = false;
        });
    }

    window.juxHistory = window.JuxUI.createListTab({
        id: 'history',
        endpoint: 'JuxHomepage/MovieHistory/Items',
        logName: 'Movie History',
        labels: 'history',
        state: { sortBy: 'LastPlayed', sortOrder: 'Descending' },
        sortOptions: _sortOptions,
        emptyIcon: 'movie',
        decorate: _decorateCards,
        header: _renderTiles
    });

    window.juxHistory.init();

    // Guarded UMD-lite export (same convention as jux-watchlist.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _sortOptions: _sortOptions,
            _buildShellHtml: window.juxHistory.buildShellHtml,
            _decorateCards: _decorateCards,
            _formatWatchedDate: _formatWatchedDate,
            _renderTiles: _renderTiles
        };
    }
})();
