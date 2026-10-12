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

    var _labels = {
        en: {
            title: 'History',
            empty: 'No watched movies yet',
            emptySubtitle: 'Movies you finish watching show up here.',
            sortLastPlayed: 'Last Watched',
            sortName: 'Name',
            sortDialogTitle: 'Sort History',
            addFavorite: 'Add to favorites',
            removeFavorite: 'Remove from favorites'
        },
        fr: {
            title: 'Historique',
            empty: 'Aucun film vu pour le moment',
            emptySubtitle: 'Les films que tu termines apparaissent ici.',
            sortLastPlayed: 'Dernier vu',
            sortName: 'Nom',
            sortDialogTitle: 'Trier l’historique',
            addFavorite: 'Ajouter aux favoris',
            removeFavorite: 'Retirer des favoris'
        }
    };

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

        var overlays = container.querySelectorAll('.cardOverlayContainer');
        Array.prototype.forEach.call(overlays, function (overlay) {
            var card = overlay.closest('.card[data-id]');
            var item = card && byId[card.getAttribute('data-id')];
            if (!item) { return; }

            var isFavorite = !!(item.UserData && item.UserData.IsFavorite);

            var button = document.createElement('button');
            button.type = 'button';
            button.setAttribute('is', 'paper-icon-button-light');
            button.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light jux-history-favorite-toggle';
            button.title = isFavorite ? t.removeFavorite : t.addFavorite;

            var icon = document.createElement('span');
            icon.className = 'material-icons ' + (isFavorite ? 'favorite' : 'favorite_border');
            icon.setAttribute('aria-hidden', 'true');
            button.appendChild(icon);

            button.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                _toggleFavorite(item.Id, !isFavorite, button, icon, t);
            });

            overlay.appendChild(button);
        });
    }

    // Favorite toggle via Jellyfin's own native endpoint (POST/DELETE
    // /Users/{userId}/FavoriteItems/{itemId}) -- no JellyUX endpoint needed, same convention already
    // used for the Watchlist toggle's underlying Likes field.
    function _toggleFavorite(itemId, nowFavorite, button, icon, t) {
        if (!window.ApiClient) { return; }
        var userId = window.ApiClient.getCurrentUserId();

        window.ApiClient.updateFavoriteStatus(userId, itemId, nowFavorite).then(function () {
            icon.className = 'material-icons ' + (nowFavorite ? 'favorite' : 'favorite_border');
            button.title = nowFavorite ? t.removeFavorite : t.addFavorite;
        }).catch(function (err) {
            console.error('[JellyUX] Failed to toggle favorite state:', err);
        });
    }

    window.juxHistory = window.JuxUI.createListTab({
        id: 'history',
        endpoint: 'JuxHomepage/MovieHistory/Items',
        logName: 'Movie History',
        labels: _labels,
        state: { sortBy: 'LastPlayed', sortOrder: 'Descending' },
        sortOptions: _sortOptions,
        emptyIcon: 'movie',
        decorate: _decorateCards
    });

    window.juxHistory.init();

    // Guarded UMD-lite export (same convention as jux-watchlist.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _sortOptions: _sortOptions,
            _buildShellHtml: window.juxHistory.buildShellHtml
        };
    }
})();
