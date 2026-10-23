'use strict';

// JellyUX Watchlist tab rendering (TODO_V3.md Phase 5.1), built on the shared list tab of jux-ui.js
// (JuxUI.createListTab, TODO_V4.md 5.1): sort dialog, type filter, loading, empty state and cards.
// Loaded as a plain <script src>, independent of the loadSections override: it only needs
// window.JellyfinAPI.cardBuilder, read at render time, by which point the user has necessarily
// visited Home this session, since navigating there is how the Watchlist tab itself got created
// (see jux-tab-injector.js).
(function () {
    if (typeof window.juxWatchlist !== 'undefined') {
        return;
    }

    function _sortOptions(t) {
        return [
            { value: 'DateCreated', label: t.sortDateAdded },
            { value: 'SortName', label: t.sortName },
            { value: 'PremiereDate', label: t.sortReleaseDate },
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

    window.juxWatchlist = window.JuxUI.createListTab({
        id: 'watchlist',
        endpoint: 'JuxHomepage/Watchlist/Items',
        logName: 'Watchlist',
        labels: 'watchlist',
        state: { sortBy: 'DateCreated', sortOrder: 'Descending', includeItemTypes: 'All' },
        sortOptions: _sortOptions,
        filters: [{ name: 'type', key: 'includeItemTypes', icon: 'filter_list', titleKey: 'typeDialogTitle', options: _typeOptions }],
        emptyIcon: 'bookmark_border',
        cardOptions: { showParentTitle: true }
    });

    window.juxWatchlist.init();

    // Guarded UMD-lite export, so Vitest can exercise the pure functions directly.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _sortOptions: _sortOptions,
            _typeOptions: _typeOptions,
            _buildShellHtml: window.juxWatchlist.buildShellHtml
        };
    }
})();
