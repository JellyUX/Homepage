'use strict';

// JellyUX Movie History tab rendering.
// TODO_V3.md Phase 6.2, visually reworked in Phase 6 bis to use the shared window.JuxUI helpers
// (Web/jux-ui.js) instead of a bare <select> and inline styles: a native-look sort dialog, a loading
// spinner, and an icon-based empty state.
//
// Same conventions as jux-watchlist.js/jux-progress.js: click listener delegated on `document`,
// rendered via window.JellyfinAPI.cardBuilder.getCardsHtml + JuxUI.observeCardImages.
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

    window.juxHistory = {
        state: { sortBy: 'LastPlayed', sortOrder: 'Descending' },

        init: function () {
            var self = this;
            document.addEventListener('click', function (event) {
                if (event.target.closest('#jux-tabbtn-history')) {
                    self.render();
                }
            });
        },

        render: function () {
            var self = this;
            var pane = document.getElementById('jux-tab-history');
            if (!pane) {
                return;
            }

            var sections = pane.querySelector('.sections');
            if (!sections) {
                return;
            }

            var lang = _resolveLang();
            var userId = window.ApiClient && window.ApiClient.getCurrentUserId();
            if (!userId) {
                return;
            }

            sections.innerHTML = _buildShellHtml(lang, self.state);
            var itemsContainer = sections.querySelector('.jux-history-items');
            _wireControls(sections, self, lang);

            if (window.JuxUI) { window.JuxUI.showLoading(itemsContainer); }

            var url = window.ApiClient.getUrl('JuxHomepage/MovieHistory/Items', {
                userId: userId,
                sortBy: self.state.sortBy,
                sortOrder: self.state.sortOrder,
                startIndex: 0,
                limit: 100
            });

            window.ApiClient.getJSON(url).then(function (result) {
                _renderItems(itemsContainer, result, lang);
            }).catch(function (err) {
                console.error('[JellyUX] Movie History fetch failed:', err);
                _renderItems(itemsContainer, { Items: [], TotalRecordCount: 0 }, lang);
            });
        }
    };

    function _resolveLang() {
        return (document.documentElement.lang || 'en').toLowerCase().indexOf('fr') === 0 ? 'fr' : 'en';
    }

    function _sortOptions(t) {
        return [
            { value: 'LastPlayed', label: t.sortLastPlayed },
            { value: 'Name', label: t.sortName }
        ];
    }

    function _labelFor(options, value) {
        for (var i = 0; i < options.length; i++) {
            if (options[i].value === value) { return options[i].label; }
        }
        return options[0] && options[0].label;
    }

    function _buildShellHtml(lang, state) {
        var t = _labels[lang];
        return '<h2 class="sectionTitle sectionTitle-cards jux-section-title-container">' + _escHtml(t.title) + '</h2>' +
            '<div class="jux-view-controls">' +
            '<button type="button" class="jux-sort-button" id="jux-history-sort-btn">' +
            '<span class="material-icons sort" aria-hidden="true"></span>' +
            '<span class="jux-sort-button-label">' + _escHtml(_labelFor(_sortOptions(t), state.sortBy)) + '</span>' +
            '<span class="material-icons arrow_drop_down" aria-hidden="true"></span>' +
            '</button>' +
            '</div>' +
            '<div class="jux-history-items itemsContainer vertical-wrap padded-left padded-right"></div>';
    }

    function _wireControls(sections, self, lang) {
        var t = _labels[lang];
        var sortBtn = sections.querySelector('#jux-history-sort-btn');
        if (sortBtn && window.JuxUI) {
            sortBtn.addEventListener('click', function () {
                window.JuxUI.openSortDialog({
                    title: t.sortDialogTitle,
                    sortOptions: _sortOptions(t),
                    currentSortBy: self.state.sortBy,
                    currentSortOrder: self.state.sortOrder,
                    onChange: function (sortBy, sortOrder) {
                        self.state.sortBy = sortBy;
                        self.state.sortOrder = sortOrder;
                        self.render();
                    }
                });
            });
        }
    }

    function _renderItems(itemsContainer, result, lang) {
        if (!itemsContainer) {
            return;
        }

        var items = (result && result.Items) || [];
        if (items.length === 0) {
            if (window.JuxUI) {
                window.JuxUI.showEmpty(itemsContainer, {
                    icon: 'movie',
                    title: _labels[lang].empty,
                    subtitle: _labels[lang].emptySubtitle
                });
            } else {
                itemsContainer.innerHTML = '';
            }
            return;
        }

        var api = window.JellyfinAPI;
        if (!api || !api.cardBuilder) {
            itemsContainer.innerHTML = '';
            return;
        }

        itemsContainer.innerHTML = api.cardBuilder.getCardsHtml({
            items: items,
            shape: api.getPortraitShape(false),
            showTitle: true,
            overlayText: false,
            overlayPlayButton: true,
            centerText: true,
            showYear: true,
            lazy: true,
            lines: 2
        });

        if (window.JuxUI) {
            window.JuxUI.observeCardImages(itemsContainer);
        }
        _decorateCards(itemsContainer, items, lang);
    }

    function _decorateCards(container, items, lang) {
        var t = _labels[lang];
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

    function _escHtml(str) {
        if (!str) { return ''; }
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    window.juxHistory.init();

    // Guarded UMD-lite export (same convention as jux-watchlist.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _escHtml: _escHtml,
            _sortOptions: _sortOptions,
            _labelFor: _labelFor,
            _buildShellHtml: _buildShellHtml
        };
    }
})();
