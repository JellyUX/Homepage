'use strict';

// JellyUX Watchlist tab rendering.
// TODO_V3.md Phase 5.1, visually reworked in Phase 6 bis to use the shared window.JuxUI helpers
// (Web/jux-ui.js, loaded first -- see TransformationPatches.IndexHtml) instead of a bare <select> and
// inline styles: a native-look sort/filter dialog, a loading spinner, and an icon-based empty state.
// Loaded as a plain <script src>, independent of the loadSections override -- unlike jux-homepage.js,
// this script does not need window.JellyfinAPI to exist (it only needs window.JellyfinAPI.cardBuilder,
// which is lazily read at render time, by which point the user has necessarily visited Home at least
// once this session, since navigating there is how the Watchlist tab itself got created -- see
// jux-tab-injector.js).
//
// The tab bar (and our button) is recreated on every view mount (see jux-tab-injector.js), so the
// click listener below is delegated on `document`, never attached to the button element directly.
(function () {
    if (typeof window.juxWatchlist !== 'undefined') {
        return;
    }

    var _labels = {
        en: {
            title: 'Watchlist',
            empty: 'Your watchlist is empty',
            emptySubtitle: 'Items you bookmark from a card, the "More" menu, or the detail page show up here.',
            sortName: 'Name',
            sortDateAdded: 'Date Added',
            sortReleaseDate: 'Release Date',
            sortCommunityRating: 'Community Rating',
            sortDialogTitle: 'Sort Watchlist',
            typeAll: 'All',
            typeMovie: 'Movies',
            typeSeries: 'Series',
            typeDialogTitle: 'Filter by Type'
        },
        fr: {
            title: 'Watchlist',
            empty: 'Ta watchlist est vide',
            emptySubtitle: 'Les items que tu ajoutes depuis une carte, le menu "More" ou la page de détail apparaissent ici.',
            sortName: 'Nom',
            sortDateAdded: 'Date d’ajout',
            sortReleaseDate: 'Date de sortie',
            sortCommunityRating: 'Note communauté',
            sortDialogTitle: 'Trier la Watchlist',
            typeAll: 'Tous',
            typeMovie: 'Films',
            typeSeries: 'Séries',
            typeDialogTitle: 'Filtrer par type'
        }
    };

    window.juxWatchlist = {
        state: { sortBy: 'DateAdded', sortOrder: 'Descending', includeItemTypes: 'All' },

        init: function () {
            var self = this;
            document.addEventListener('click', function (event) {
                if (event.target.closest('#jux-tabbtn-watchlist')) {
                    self.render();
                }
            });
        },

        render: function () {
            var self = this;
            var pane = document.getElementById('jux-tab-watchlist');
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
            var itemsContainer = sections.querySelector('.jux-watchlist-items');
            _wireControls(sections, self, lang);

            if (window.JuxUI) { window.JuxUI.showLoading(itemsContainer); }

            var url = window.ApiClient.getUrl('JuxHomepage/Watchlist/Items', {
                userId: userId,
                sortBy: self.state.sortBy,
                sortOrder: self.state.sortOrder,
                includeItemTypes: self.state.includeItemTypes,
                startIndex: 0,
                limit: 100
            });

            window.ApiClient.getJSON(url).then(function (result) {
                _renderItems(itemsContainer, result, lang);
            }).catch(function (err) {
                console.error('[JellyUX] Watchlist fetch failed:', err);
                _renderItems(itemsContainer, { Items: [], TotalRecordCount: 0 }, lang);
            });
        }
    };

    function _resolveLang() {
        return (document.documentElement.lang || 'en').toLowerCase().indexOf('fr') === 0 ? 'fr' : 'en';
    }

    function _sortOptions(t) {
        return [
            { value: 'DateAdded', label: t.sortDateAdded },
            { value: 'Name', label: t.sortName },
            { value: 'ReleaseDate', label: t.sortReleaseDate },
            { value: 'CommunityRating', label: t.sortCommunityRating }
        ];
    }

    function _typeOptions(t) {
        return [
            { value: 'All', label: t.typeAll },
            { value: 'Movie', label: t.typeMovie },
            { value: 'Series', label: t.typeSeries }
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
        return '<h2 class="sectionTitle sectionTitle-cards jux-section-title-container">' + window.JuxCore.escHtml(t.title) + '</h2>' +
            '<div class="jux-view-controls">' +
            '<button type="button" class="jux-sort-button" id="jux-watchlist-sort-btn">' +
            '<span class="material-icons sort" aria-hidden="true"></span>' +
            '<span class="jux-sort-button-label">' + window.JuxCore.escHtml(_labelFor(_sortOptions(t), state.sortBy)) + '</span>' +
            '<span class="material-icons arrow_drop_down" aria-hidden="true"></span>' +
            '</button>' +
            '<button type="button" class="jux-sort-button" id="jux-watchlist-type-btn">' +
            '<span class="material-icons filter_list" aria-hidden="true"></span>' +
            '<span class="jux-sort-button-label">' + window.JuxCore.escHtml(_labelFor(_typeOptions(t), state.includeItemTypes)) + '</span>' +
            '<span class="material-icons arrow_drop_down" aria-hidden="true"></span>' +
            '</button>' +
            '</div>' +
            '<div class="jux-watchlist-items itemsContainer vertical-wrap padded-left padded-right"></div>';
    }

    function _wireControls(sections, self, lang) {
        var t = _labels[lang];
        var sortBtn = sections.querySelector('#jux-watchlist-sort-btn');
        var typeBtn = sections.querySelector('#jux-watchlist-type-btn');

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

        if (typeBtn && window.JuxUI) {
            typeBtn.addEventListener('click', function () {
                window.JuxUI.openSortDialog({
                    title: t.typeDialogTitle,
                    sortOptions: _typeOptions(t),
                    currentSortBy: self.state.includeItemTypes,
                    orderOptions: [],
                    onChange: function (includeItemTypes) {
                        self.state.includeItemTypes = includeItemTypes;
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
                    icon: 'bookmark_border',
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
            showParentTitle: true,
            overlayText: false,
            overlayPlayButton: true,
            centerText: true,
            showYear: true,
            lazy: true,
            lines: 2
        });

        window.JuxCore.observeCardImages(itemsContainer);
    }


    window.juxWatchlist.init();

    // Guarded UMD-lite export (same convention as jux-homepage.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _sortOptions: _sortOptions,
            _typeOptions: _typeOptions,
            _labelFor: _labelFor,
            _buildShellHtml: _buildShellHtml
        };
    }
})();
