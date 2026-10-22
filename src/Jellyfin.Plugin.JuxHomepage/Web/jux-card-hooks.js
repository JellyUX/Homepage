'use strict';

// JellyUX Watchlist toggle -- everywhere an item can be acted on.
// TODO_V3.md Phase 5.2, extended after the Phase 5 manual test round to also cover the item
// detail page's action row (next to the native "Add to favorites" heart) and the "More" (...)
// action-sheet menu, both on cards and on the detail page -- confirmed live on jellyux-test that
// both menu triggers (card `button[data-action="menu"]` and the detail page's `.btnMoreCommands`)
// open the exact same native `.actionSheetScroller` component, so a single injector covers both.
//
// One subscription to the shared body MutationObserver of jux-core.js (JuxCore.onDomMutation) catches
// every surface (card overlays, the detail page's button row, and action sheets) without needing
// window.JellyfinAPI (only populated after a Home visit) or any chunk-level patch of cardBuilder itself.
//
// TODO_V3.md Phase 7.3 (Infinite Scroll) extends this same file with its own card-level hook (see
// _initInfiniteScroll below), driven by the same _observe() subscription.
(function () {
    if (typeof window.juxCardHooks !== 'undefined') {
        return;
    }

    // How long a captured "More" menu context stays valid -- the action-sheet observer fires
    // within a frame or two of the menu opening; this just bounds how stale a context can be.
    var MENU_CONTEXT_TTL_MS = 5000;
    var _menuContext = null;

    // The Watchlist tab only lists movies and series, and /Watchlist/Ids only returns those (F-06:
    // filtering by type keeps that query on the Type index), so the toggle is offered on them only.
    var WATCHLIST_TYPES = ['Movie', 'Series'];
    var _itemTypes = {};

    window.juxCardHooks = {
        likedIds: null,
        // The user the liked ids belong to: the set is never shown to anyone else (TODO_V4.md 5.4,
        // F-51).
        userId: null,

        init: function () {
            var self = this;
            self.likedIds = new Set();

            _hookExistingCards(self.likedIds);
            _hookDetailButton(self.likedIds);
            _observe(self.likedIds);
            _watchMenuTriggers();
            _watchNavigation(self);
            _tryInitLibraryInfiniteScroll();

            // The liked ids are preloaded in one call (TODO_V3.md Phase 5.2) for the signed-in user,
            // at load and again on every view shown: a slow sign-in that leaves no user at load, or a
            // sign-out followed by another account, reloads them on the first view of the new user.
            document.addEventListener('viewshow', function () { _syncSession(self); });
            // The detail-page modules ask for the user state as soon as a user is signed in, which
            // can come before the first viewshow (H-09).
            window.JuxCore.provideUserState(function () { _syncSession(self); });
            _syncSession(self);
        }
    };

    // Loads the liked ids of the signed-in user when they differ from the user the set belongs to.
    // The set is emptied at once, so the previous user's ids never mark another user's cards, and a
    // late answer for a user who has since left is dropped.
    function _syncSession(state) {
        var userId = window.ApiClient && window.ApiClient.getCurrentUserId();
        if (!userId || userId === state.userId) {
            return;
        }

        state.userId = userId;
        state.likedIds.clear();
        _resyncAllButtons(state.likedIds);

        var url = window.ApiClient.getUrl('JuxHomepage/Watchlist/Ids', { userId: userId });
        // Time-limited: the detail-page modules wait for this answer (FIAB-08, R11 review).
        var request = window.JuxCore.fetchWithTimeout(window.ApiClient, url);
        // The answer also says whether JellyUX runs for the user: the detail-page modules wait for it
        // instead of asking again (H-09).
        window.JuxCore.reportUserStateRequest(userId, request.then(function (result) {
            window.JuxCore.setUserEnabled(userId, !(result && result.Enabled === false));
        }));
        request.then(function (result) {
            if (state.userId !== userId) {
                return;
            }
            // Turned off for this user (TODO_V4.md 8.3): the buttons added before the answer go.
            window.JuxCore.setUserEnabled(userId, !(result && result.Enabled === false));
            if (window.JuxCore.isOffForUser()) {
                Array.prototype.forEach.call(
                    document.querySelectorAll('.jux-watchlist-toggle, .jux-watchlist-detail-toggle'),
                    function (button) { button.remove(); });
                return;
            }
            ((result && result.Ids) || []).forEach(function (id) { state.likedIds.add(id); });
            _resyncAllButtons(state.likedIds);
        }).catch(function (err) {
            console.error('[JellyUX] Failed to preload watchlist ids:', err);
            if (state.userId === userId) {
                state.userId = null;
            }
        });
    }

    // -------------------------------------------------------------------------
    // DOM observation
    // -------------------------------------------------------------------------

    function _observe(likedIds) {
        window.JuxCore.onDomMutation(function (mutations) {
            var mightHaveNewActionSheet = false;
            for (var i = 0; i < mutations.length; i++) {
                var added = mutations[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    var node = added[j];
                    if (node.nodeType !== 1) { continue; }
                    _hookNode(node, likedIds);
                    mightHaveNewActionSheet = true;
                }
            }
            if (mightHaveNewActionSheet) {
                _addWatchlistMenuItem(likedIds);
            }
            _tryInitLibraryInfiniteScroll();
        });
    }

    function _hookExistingCards(likedIds) {
        var containers = document.querySelectorAll('.cardOverlayContainer');
        Array.prototype.forEach.call(containers, function (c) { _hookOverlay(c, likedIds); });
    }

    function _hookNode(node, likedIds) {
        if (node.classList && node.classList.contains('cardOverlayContainer')) {
            _hookOverlay(node, likedIds);
        }

        var descendants = typeof node.querySelectorAll === 'function'
            ? node.querySelectorAll('.cardOverlayContainer')
            : [];
        Array.prototype.forEach.call(descendants, function (c) { _hookOverlay(c, likedIds); });

        var hasDetailButton = (node.classList && node.classList.contains('btnUserRating')) ||
            (typeof node.querySelector === 'function' && node.querySelector('.btnUserRating'));
        if (hasDetailButton) {
            _hookDetailButton(likedIds);
        }
    }

    // -------------------------------------------------------------------------
    // Card overlay toggle button
    // -------------------------------------------------------------------------

    function _hookOverlay(overlayEl, likedIds) {
        if (overlayEl.getAttribute('data-jux-hooked') || window.JuxCore.isOffForUser()) {
            return;
        }

        var card = overlayEl.closest('.card');
        var itemInfo = card ? _readCardItemInfo(card) : null;
        if (!itemInfo || !_isWatchlistType(itemInfo.type)) {
            return;
        }

        overlayEl.setAttribute('data-jux-hooked', '1');

        var button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('is', 'paper-icon-button-light');
        button.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light jux-watchlist-toggle';
        button.setAttribute('data-jux-item-id', itemInfo.id);

        var icon = document.createElement('span');
        icon.className = 'material-icons';
        icon.setAttribute('aria-hidden', 'true');
        button.appendChild(icon);

        _applyIconState(button, likedIds.has(itemInfo.id), window.JuxCore.resolveLang());

        button.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            _toggle(itemInfo.id, likedIds);
        });

        overlayEl.appendChild(button);
    }

    function _readCardItemInfo(card) {
        var id = card.getAttribute('data-id');
        if (!id) {
            return null;
        }

        return { id: id, type: card.getAttribute('data-type') };
    }

    function _isWatchlistType(type) {
        return WATCHLIST_TYPES.indexOf(type) !== -1;
    }

    /**
     * Resolves an item's type, once per item id: the detail page URL carries only the id.
     */
    function _resolveItemType(itemId) {
        if (Object.prototype.hasOwnProperty.call(_itemTypes, itemId)) {
            return Promise.resolve(_itemTypes[itemId]);
        }

        var apiClient = window.ApiClient;
        if (!apiClient || typeof apiClient.getItem !== 'function') {
            return Promise.resolve(null);
        }

        // The detail-item cache of jux-core.js, shared with collections, the breadcrumb and
        // flattened seasons (TODO_V4.md 5.1).
        return window.JuxCore.getItem(apiClient.getCurrentUserId(), itemId).then(function (item) {
            _itemTypes[itemId] = (item && item.Type) || null;
            return _itemTypes[itemId];
        }, function () {
            return null;
        });
    }

    // -------------------------------------------------------------------------
    // Item detail page button, next to the native "Add to favorites" heart
    // -------------------------------------------------------------------------

    function _currentDetailItemId() {
        var match = /[#&?]id=([a-f0-9-]+)/i.exec(location.hash);
        return match ? match[1] : null;
    }

    // Jellyfin keeps previously visited detail pages in the DOM, hidden: only the visible page's
    // heart belongs to the current item.
    function _currentDetailHeart() {
        return document.querySelector('.page:not(.hide) .btnUserRating.detailButton');
    }

    function _hookDetailButton(likedIds) {
        var itemId = _currentDetailItemId();
        if (!itemId || !_currentDetailHeart()) {
            return;
        }

        _resolveItemType(itemId).then(function (type) {
            if (_isWatchlistType(type) && _currentDetailItemId() === itemId) {
                _insertDetailButton(itemId, likedIds);
            }
        });
    }

    function _insertDetailButton(itemId, likedIds) {
        var heart = _currentDetailHeart();
        if (!heart || window.JuxCore.isOffForUser()) {
            return;
        }

        var existing = heart.parentElement.querySelector('.jux-watchlist-detail-toggle');
        if (existing) {
            if (existing.getAttribute('data-jux-item-id') === itemId) {
                return;
            }
            // A different item's detail page reused the same button row (SPA navigation) --
            // the stale button belongs to the previous item and must not linger.
            existing.remove();
        }

        var button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('is', 'emby-button');
        // Built explicitly rather than cloning heart.className: the native heart button can carry
        // a transient "hide" class (removed by Jellyfin's own controller once its data/capability
        // check resolves) -- confirmed live that cloning it at the wrong instant baked "hide" in
        // permanently, since this button's class is only ever set once, here, and never revisited.
        button.className = 'button-flat detailButton emby-button jux-watchlist-detail-toggle';
        button.setAttribute('data-jux-item-id', itemId);

        var icon = document.createElement('span');
        icon.className = 'material-icons';
        icon.setAttribute('aria-hidden', 'true');
        button.appendChild(icon);

        _applyIconState(button, likedIds.has(itemId), window.JuxCore.resolveLang());

        button.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            _toggle(itemId, likedIds);
        });

        heart.insertAdjacentElement('afterend', button);
    }

    // -------------------------------------------------------------------------
    // "More" (...) action-sheet menu item -- shared by card menus and the detail page menu
    // -------------------------------------------------------------------------

    function _watchMenuTriggers() {
        document.body.addEventListener('mousedown', function (event) {
            var trigger = event.target.closest('button[data-action="menu"], .btnMoreCommands');
            if (!trigger) {
                return;
            }

            var card = trigger.closest('.card[data-id]');
            var itemId = card ? card.getAttribute('data-id') : _currentDetailItemId();
            var type = card ? card.getAttribute('data-type') : _itemTypes[itemId];
            _menuContext = itemId && _isWatchlistType(type) ? { itemId: itemId, ts: Date.now() } : null;
        }, true);
    }

    function _getActiveActionSheetScroller() {
        var scrollers = document.querySelectorAll('.actionSheetScroller');
        for (var i = scrollers.length - 1; i >= 0; i--) {
            if (scrollers[i].offsetParent !== null) {
                return scrollers[i];
            }
        }
        return null;
    }

    function _addWatchlistMenuItem(likedIds) {
        var scroller = _getActiveActionSheetScroller();
        if (!scroller) {
            return;
        }

        var existing = scroller.querySelector('.jux-watchlist-menuitem');
        var contextIsFresh = _menuContext && (Date.now() - _menuContext.ts) <= MENU_CONTEXT_TTL_MS;

        if (!contextIsFresh) {
            if (existing) { existing.remove(); }
            return;
        }

        if (existing) {
            if (existing.getAttribute('data-jux-item-id') === _menuContext.itemId) {
                return;
            }
            existing.remove();
        }

        var ref = scroller.querySelector('.actionSheetMenuItem');
        if (!ref) {
            return;
        }

        var itemId = _menuContext.itemId;
        var itemClass = ref.getAttribute('class').replace(/\bselected\b/g, '').replace(/\s+/g, ' ').trim();

        var wrapper = document.createElement('div');
        wrapper.innerHTML =
            '<button is="emby-button" type="button" class="' + window.JuxCore.escHtml(itemClass) + ' jux-watchlist-menuitem" data-jux-item-id="' + window.JuxCore.escHtml(itemId) + '">' +
            '<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons" aria-hidden="true"></span>' +
            '<div class="listItemBody actionsheetListItemBody"><div class="listItemBodyText actionSheetItemText"></div></div>' +
            '</button>';
        var button = wrapper.firstElementChild;
        _applyIconState(button, likedIds.has(itemId), window.JuxCore.resolveLang());

        button.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            _toggle(itemId, likedIds);
        });

        var insertionPoint = scroller.querySelector('[data-id="playallfromhere"]') ||
            scroller.querySelector('[data-id="resume"]') ||
            scroller.querySelector('[data-id="play"]');

        if (insertionPoint) {
            insertionPoint.insertAdjacentElement('afterend', button);
        } else {
            ref.parentElement.insertBefore(button, ref);
        }
    }

    // -------------------------------------------------------------------------
    // Navigation re-arming (SPA route changes don't reload the page)
    // -------------------------------------------------------------------------

    function _watchNavigation(state) {
        window.addEventListener('hashchange', function () {
            setTimeout(function () {
                _hookDetailButton(state.likedIds);
                _finishedList = null;
                _resetLibraryInfiniteScroll();
                _tryInitLibraryInfiniteScroll();
            }, 400);
        });
    }

    // -------------------------------------------------------------------------
    // Shared icon/label state + toggle
    // -------------------------------------------------------------------------

    function _iconName(isLiked) {
        return isLiked ? 'bookmark' : 'bookmark_border';
    }

    // Applies the liked/unliked visual state to any of the three button kinds this file creates
    // (card overlay button, detail-page button, action-sheet menu item) -- each carries a
    // ".material-icons" child, and the menu item additionally carries a text label to keep in sync.
    function _applyIconState(el, isLiked, lang) {
        var icon = el.querySelector('.material-icons');
        if (icon) {
            icon.className = 'material-icons ' + _iconName(isLiked);
        }

        var text = window.JuxCore.labels('cardhooks', lang);
        var label = isLiked ? text.remove : text.add;
        el.title = label;

        var textEl = el.querySelector('.actionSheetItemText');
        if (textEl) {
            textEl.textContent = label;
        }
    }

    function _toggle(itemId, likedIds) {
        if (!window.ApiClient) {
            return;
        }

        var userId = window.ApiClient.getCurrentUserId();
        var nowLiked = !likedIds.has(itemId);

        // Removing clears the rating: a false "likes" would leave the item disliked in Jellyfin.
        var request = nowLiked
            ? window.ApiClient.updateUserItemRating(userId, itemId, true)
            : window.ApiClient.clearUserItemRating(userId, itemId);

        request.then(function () {
            if (nowLiked) {
                likedIds.add(itemId);
            } else {
                likedIds.delete(itemId);
            }

            _syncButtonsForItem(itemId, nowLiked);
        }).catch(function (err) {
            console.error('[JellyUX] Failed to toggle watchlist state:', err);
        });
    }

    function _syncButtonsForItem(itemId, isLiked) {
        var lang = window.JuxCore.resolveLang();
        var matches = document.querySelectorAll('[data-jux-item-id="' + CSS.escape(itemId) + '"]');
        Array.prototype.forEach.call(matches, function (el) { _applyIconState(el, isLiked, lang); });
    }

    // Repairs any button that rendered before the liked-id set of the current user finished loading
    // (see _syncSession).
    function _resyncAllButtons(likedIds) {
        var lang = window.JuxCore.resolveLang();
        var all = document.querySelectorAll('[data-jux-item-id]');
        Array.prototype.forEach.call(all, function (el) {
            _applyIconState(el, likedIds.has(el.getAttribute('data-jux-item-id')), lang);
        });
    }

    // -------------------------------------------------------------------------
    // Library page infinite scroll (TODO_V3.md Phase 7.3)
    //
    // Driven by this file's subscription to the shared body MutationObserver (see the header
    // comment). Scoped to the Movies/Series library listing pages only (TODO_V3.md's own stated
    // scope) -- confirmed live on jellyux-test that this Jellyfin Web build shows classic native
    // pagination (.paging with .btnPreviousPage/.btnNextPage) on these pages, which is hidden once
    // infinite scroll takes over. Reads the user's currently active native sort/filter/alpha-picker
    // choice (localStorage keys ${userId}-${parentId}-${mediaType}[-filter], confirmed live to hold
    // exactly {"SortBy":...,"SortOrder":...} / {"Filters":...} shapes) so appended pages respect it,
    // the same way KefinTweaks' infiniteScroll.js does -- but using an IntersectionObserver sentinel
    // (consistent with the home lazy-loader fix above) rather than a throttled scroll listener.
    // -------------------------------------------------------------------------

    var _libraryScroll = null;

    // The list read to its end (FIAB-01): { key, container, count }. Every mutation, the sentinel's own
    // removal included, calls _tryInitLibraryInfiniteScroll again; without this memory a finished list
    // re-armed at once and queried its end in a loop (899 requests in 15 s on the 8098 bench). Cleared
    // on a view change (hashchange); a native sort, filter or letter change gives another key, and a
    // native re-render another card count, either of which re-arms (test: jux-card-hooks.test.js
    // "end of the list").
    var _finishedList = null;

    // The user's native sort and filter choice for a library, null-safe when storage is blocked.
    function _readSortSettings(userId, parentId, mediaType) {
        var base = userId + '-' + parentId + '-' + mediaType;
        var sortRaw = null;
        var filterRaw = null;
        try {
            sortRaw = localStorage.getItem(base);
            filterRaw = localStorage.getItem(base + '-filter');
        } catch (e) {
            // Storage blocked: the native defaults apply.
        }
        var settings = _parseSortSettings(sortRaw, filterRaw);
        var alphaSelected = document.querySelector('.alphaPickerButton-selected');
        settings.nameStartsWith = alphaSelected ? alphaSelected.getAttribute('data-value') : null;
        return settings;
    }

    function _listKey(mediaType, parentId, settings) {
        return [mediaType, parentId, settings.sortBy, settings.sortOrder, settings.filters, settings.years,
            settings.genres, settings.tags, settings.nameStartsWith || ''].join('|');
    }

    function _infiniteScrollMediaType(hash) {
        hash = (hash || '').toLowerCase();
        if (/^#\/movies(\.html)?([/?]|$)/.test(hash)) { return 'movies'; }
        if (/^#\/tv(\.html)?([/?]|$)/.test(hash)) { return 'series'; }
        return null;
    }

    function _parseSortSettings(rawSortJson, rawFilterJson) {
        var sort = {};
        var filter = {};
        try { sort = rawSortJson ? JSON.parse(rawSortJson) : {}; } catch (e) { sort = {}; }
        try { filter = rawFilterJson ? JSON.parse(rawFilterJson) : {}; } catch (e) { filter = {}; }

        return {
            sortBy: sort.SortBy || 'SortName,ProductionYear',
            sortOrder: sort.SortOrder || 'Ascending',
            filters: filter.Filters || '',
            years: filter.Years || '',
            genres: filter.Genres || '',
            tags: filter.Tags || ''
        };
    }

    function _buildItemsQuery(params) {
        var query = {
            ParentId: params.parentId,
            IncludeItemTypes: params.includeItemTypes,
            Recursive: true,
            SortBy: params.sortBy,
            SortOrder: params.sortOrder,
            StartIndex: params.startIndex,
            Limit: params.limit || 100,
            Fields: 'PrimaryImageAspectRatio'
        };
        if (params.filters) { query.Filters = params.filters; }
        if (params.years) { query.Years = params.years; }
        if (params.genres) { query.Genres = params.genres; }
        if (params.tags) { query.Tags = params.tags; }
        if (params.nameStartsWith) { query.NameStartsWith = params.nameStartsWith; }
        return query;
    }

    function _hasMoreItems(startIndex, totalRecordCount) {
        return startIndex < totalRecordCount;
    }

    function _activeLibraryTab(activePage) {
        // Scoped to the current .libraryPage, not the whole document: Jellyfin Web's page DOM cache
        // (data-dom-cache="true" on .libraryPage, confirmed live on jellyux-test) can leave a stale,
        // detached #moviesTab.is-active element behind elsewhere in the document after switching
        // sub-tabs (Movies/Suggestions/Favorites/...). A document-wide querySelector deterministically
        // returns the FIRST match in document order, which was that stale zero-size element rather
        // than the real visible tab -- cards and the scroll sentinel were then appended to a node the
        // user could never see, while native pagination (in the real, untouched tab) stayed visible.
        // Confirmed live: exactly one #moviesTab exists inside .libraryPage:not(.hide) at any time.
        if (!activePage) { return null; }
        var moviesTab = activePage.querySelector('#moviesTab.is-active');
        if (moviesTab) { return { tab: moviesTab, mediaType: 'movies', includeItemTypes: 'Movie' }; }
        var seriesTab = activePage.querySelector('#seriesTab.is-active');
        if (seriesTab) { return { tab: seriesTab, mediaType: 'series', includeItemTypes: 'Series' }; }
        return null;
    }

    function _tryInitLibraryInfiniteScroll() {
        // Called unconditionally on every body mutation (see _observe above), so this must never
        // throw even in edge environments where the global location/document may be momentarily
        // unavailable (observed in Vitest/jsdom: a mutation queued just before test teardown can have
        // its MutationObserver callback fire after the environment's globals are torn down).
        if (typeof location === 'undefined' || typeof document === 'undefined') {
            return;
        }

        var mediaType = _infiniteScrollMediaType(location.hash);
        if (!mediaType || window.JuxCore.isOffForUser()) {
            return;
        }

        if (_libraryScroll && _libraryScroll.mediaType === mediaType && document.body.contains(_libraryScroll.container)) {
            return;
        }

        var activePage = document.querySelector('.libraryPage:not(.hide)');
        var active = _activeLibraryTab(activePage);
        if (!active || active.mediaType !== mediaType) {
            return;
        }

        var container = active.tab.querySelector('.itemsContainer');
        if (!container || container.children.length === 0) {
            // Native rendering hasn't produced the first page yet -- the body MutationObserver will
            // call this again once it has.
            return;
        }

        if (!window.ApiClient) {
            return;
        }

        var parentIdMatch = /[?&]topParentId=([a-f0-9-]+)/i.exec(location.hash);
        if (parentIdMatch) {
            _activateInfiniteScroll(mediaType, active, container, parentIdMatch[1]);
            return;
        }

        // Confirmed live on jellyux-test: reaching this same library route via a Home section title
        // ("Recently Added Movies >") or the left nav's "Films" link can land on #/movies with no
        // topParentId in the hash at all (unlike a direct topParentId-bearing URL) -- Jellyfin Web's
        // own native rendering doesn't need it since it resolves the library from the user's views
        // internally. Fall back to the same resolution here rather than silently never activating.
        _resolveLibraryViewIds().then(function (ids) {
            var resolvedId = ids && ids[mediaType];
            if (!resolvedId) {
                return;
            }

            // Re-validate against the current page state -- this resolves asynchronously, and the
            // user may have navigated away, switched tabs, or another mutation may have already armed
            // infinite scroll for this exact tab in the meantime.
            if (_infiniteScrollMediaType(location.hash) !== mediaType) {
                return;
            }

            var freshPage = document.querySelector('.libraryPage:not(.hide)');
            var freshActive = _activeLibraryTab(freshPage);
            if (!freshActive || freshActive.mediaType !== mediaType) {
                return;
            }

            var freshContainer = freshActive.tab.querySelector('.itemsContainer');
            if (!freshContainer || freshContainer.children.length === 0) {
                return;
            }

            if (_libraryScroll && _libraryScroll.mediaType === mediaType && document.body.contains(_libraryScroll.container)) {
                return;
            }

            _activateInfiniteScroll(mediaType, freshActive, freshContainer, resolvedId);
        });
    }

    function _activateInfiniteScroll(mediaType, active, container, parentId) {
        // Checked here, not only inside _loadMoreLibraryItems: cardBuilder is only populated after a
        // Home visit this session (see jux-series-flatten.js's header comment on the same fallback).
        // A library page reached first (direct link/bookmark/typed URL, matching the exact navigation
        // pattern this project must always support gracefully) would otherwise still hide the native
        // .paging element below, then permanently fail every load attempt once the sentinel is
        // reached -- leaving the user stuck with no pagination and no infinite scroll. Deferring here
        // instead leaves native pagination fully intact; the body MutationObserver retries this
        // function on every mutation, so it activates as soon as cardBuilder becomes available.
        var api = window.JellyfinAPI;
        if (!api || !api.cardBuilder) {
            return;
        }

        var key = _listKey(mediaType, parentId, _readSortSettings(window.ApiClient.getCurrentUserId(), parentId, mediaType));
        if (_finishedList && _finishedList.key === key && _finishedList.container === container &&
            _finishedList.count === container.children.length) {
            return;
        }

        _resetLibraryInfiniteScroll();

        // Confirmed live on jellyux-test: a library tab can contain several .paging elements (one per
        // alpha/genre sub-view Jellyfin Web pre-renders), not just one -- hiding only the first left a
        // stale "1-100 of 143" bar with a native "next page" arrow visible underneath the infinitely
        // scrolling grid, even though every item had already loaded.
        var pagingElements = active.tab.querySelectorAll('.paging');
        Array.prototype.forEach.call(pagingElements, function (paging) {
            paging.style.display = 'none';
        });

        var sentinel = document.createElement('div');
        sentinel.className = 'jux-infinite-sentinel';
        sentinel.style.height = '1px';
        container.parentNode.appendChild(sentinel);

        // Confirmed live on jellyux-test: with the default 100-item first page, a small rootMargin
        // (400px) meant the sentinel only entered view after scrolling almost all the way past those
        // 100 cards -- functionally working, but reading as "stuck"/broken well before that point. A
        // much larger margin loads the next batch proactively, long before the user reaches the
        // literal bottom, which is what makes scrolling actually feel infinite.
        var observer = new IntersectionObserver(function (entries) {
            if (entries[0].isIntersecting) {
                _loadMoreLibraryItems();
            }
        }, { rootMargin: '2000px' });
        observer.observe(sentinel);

        _libraryScroll = {
            observer: observer,
            sentinel: sentinel,
            container: container,
            mediaType: mediaType,
            parentId: parentId,
            key: key,
            includeItemTypes: active.includeItemTypes,
            startIndex: container.children.length,
            totalRecordCount: null,
            loading: false,
            finished: false
        };
    }

    var _libraryViewIds = null;
    var _libraryViewIdsPromise = null;

    function _resolveLibraryViewIds() {
        if (_libraryViewIds) {
            return Promise.resolve(_libraryViewIds);
        }
        if (_libraryViewIdsPromise) {
            return _libraryViewIdsPromise;
        }
        if (!window.ApiClient) {
            return Promise.resolve(null);
        }

        _libraryViewIdsPromise = window.ApiClient.getUserViews().then(function (result) {
            var ids = {};
            var views = (result && result.Items) || [];
            views.forEach(function (view) {
                if (view.CollectionType === 'movies' && !ids.movies) { ids.movies = view.Id; }
                if (view.CollectionType === 'tvshows' && !ids.series) { ids.series = view.Id; }
            });
            _libraryViewIds = ids;
            return ids;
        }).catch(function (err) {
            console.error('[JellyUX] Failed to resolve library view ids:', err);
            _libraryViewIdsPromise = null;
            return null;
        });

        return _libraryViewIdsPromise;
    }

    function _resetLibraryInfiniteScroll() {
        if (_libraryScroll) {
            if (_libraryScroll.observer) { _libraryScroll.observer.disconnect(); }
            if (_libraryScroll.sentinel && _libraryScroll.sentinel.parentNode) {
                _libraryScroll.sentinel.parentNode.removeChild(_libraryScroll.sentinel);
            }
            _libraryScroll = null;
        }
    }

    function _finishList(state) {
        state.finished = true;
        _finishedList = { key: state.key, container: state.container, count: state.container.children.length };
        _resetLibraryInfiniteScroll();
    }

    function _loadMoreLibraryItems() {
        var state = _libraryScroll;
        if (!state || state.loading || state.finished) {
            return;
        }

        var api = window.JellyfinAPI;
        if (!api || !api.cardBuilder) {
            // No replacement rendering path available -- leave the native pagination controls alone
            // (they were already left in place since this setup never got this far without cardBuilder
            // present at init time, but guard here too in case it disappears mid-session).
            _resetLibraryInfiniteScroll();
            return;
        }

        var userId = window.ApiClient.getCurrentUserId();
        var settings = _readSortSettings(userId, state.parentId, state.mediaType);

        state.loading = true;

        var query = _buildItemsQuery({
            parentId: state.parentId,
            includeItemTypes: state.includeItemTypes,
            sortBy: settings.sortBy,
            sortOrder: settings.sortOrder,
            filters: settings.filters,
            years: settings.years,
            genres: settings.genres,
            tags: settings.tags,
            nameStartsWith: settings.nameStartsWith,
            startIndex: state.startIndex,
            limit: 100
        });

        var url = window.ApiClient.getUrl('Users/' + userId + '/Items', query);

        window.ApiClient.getJSON(url).then(function (result) {
            state.loading = false;
            var items = (result && result.Items) || [];
            state.totalRecordCount = (result && result.TotalRecordCount) || 0;

            if (items.length === 0) {
                _finishList(state);
                return;
            }

            var html = api.cardBuilder.getCardsHtml({
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

            state.container.insertAdjacentHTML('beforeend', html);
            window.JuxCore.observeCardImages(state.container);

            state.startIndex += items.length;

            if (!_hasMoreItems(state.startIndex, state.totalRecordCount)) {
                _finishList(state);
                return;
            }

            // Keep the sentinel last so continued scrolling keeps triggering the observer.
            state.sentinel.parentNode.appendChild(state.sentinel);
        }).catch(function (err) {
            console.error('[JellyUX] Library infinite scroll fetch failed:', err);
            state.loading = false;
        });
    }

    window.juxCardHooks.init();

    // Guarded UMD-lite export (same convention as jux-homepage.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _iconName: _iconName,
            _readCardItemInfo: _readCardItemInfo,
            _isWatchlistType: _isWatchlistType,
            _currentDetailItemId: _currentDetailItemId,
            _infiniteScrollMediaType: _infiniteScrollMediaType,
            _parseSortSettings: _parseSortSettings,
            _buildItemsQuery: _buildItemsQuery,
            _hasMoreItems: _hasMoreItems,
            _syncSession: _syncSession,
            _toggle: _toggle
        };
    }
})();
