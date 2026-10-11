'use strict';

// JellyUX collections: "Included In" section on the item detail page + sort controls on the
// Collection (BoxSet) page. TODO_V3.md Phase 7.2.
//
// Page-hook idiom: same as jux-series-flatten.js -- item id read from location.hash, a dedicated
// MutationObserver on document.body, a hashchange re-arm. Confirmed with the user to keep this over
// KefinTweaks' window.Emby.Page.onViewShow monkey-patch.
//
// Backend: the reverse index (item -> collections it belongs to) already existed since TODO_V3.md
// Phase 4.3 (Library/CollectionsIndexCacheService.cs, refreshed daily) -- this file only consumes the
// new GET JuxHomepage/Collections/IncludedIn/{itemId} endpoint added in Phase 7.2.
(function () {
    if (typeof window.juxCollections !== 'undefined') {
        return;
    }

    var _labels = {
        en: {
            includedIn: 'Included In',
            sortTitle: 'Sort Title',
            releaseDate: 'Release Date',
            dateAdded: 'Date Added',
            communityRating: 'Community Rating',
            criticRating: 'Critic Rating',
            sortDialogTitle: 'Sort Collection'
        },
        fr: {
            includedIn: 'Fait partie de',
            sortTitle: 'Titre',
            releaseDate: 'Date de sortie',
            dateAdded: 'Date d’ajout',
            communityRating: 'Note communauté',
            criticRating: 'Note critique',
            sortDialogTitle: 'Trier la collection'
        }
    };

    var _observer = null;

    window.juxCollections = {
        init: function () {
            window.addEventListener('hashchange', function () {
                setTimeout(_syncObserver, 400);
            });

            _syncObserver();
        }
    };

    function _run() {
        _tryIncludedIn();
        _tryCollectionSort();
    }

    // One observer for this module, connected only while a detail page (#...?id=) is shown and
    // disconnected when the user leaves it: on every other page this module costs nothing.
    function _syncObserver() {
        if (!_currentDetailItemId()) {
            if (_observer) {
                _observer.disconnect();
                _observer = null;
            }
            return;
        }

        if (!_observer) {
            _observer = new MutationObserver(_run);
            _observer.observe(document.body, { childList: true, subtree: true });
        }

        _run();
    }

    // Network results of the current detail item, one shared Promise per call: however many
    // mutation batches re-run _tryIncludedIn/_tryCollectionSort, each request is sent at most once
    // per item. A failed request stays cached (no retry loop on every mutation) until the user
    // opens another item.
    var _cache = { itemId: null };

    function _cached(itemId, key, request) {
        if (_cache.itemId !== itemId) {
            _cache = { itemId: itemId };
        }
        if (!_cache[key]) {
            _cache[key] = request();
        }
        return _cache[key];
    }

    function _getItem(userId, itemId) {
        return _cached(itemId, 'item', function () {
            return window.ApiClient.getItem(userId, itemId);
        });
    }

    function _getIncludedIn(userId, itemId) {
        return _cached(itemId, 'includedIn', function () {
            var url = window.ApiClient.getUrl('JuxHomepage/Collections/IncludedIn/' + itemId);
            return window.ApiClient.getJSON(url).then(function (refs) {
                if (!refs || refs.length === 0) {
                    return [];
                }
                var ids = refs.map(function (r) { return r.CollectionId; }).join(',');
                return window.ApiClient.getItems(userId, { Ids: ids, Fields: 'PrimaryImageAspectRatio' }).then(function (result) {
                    return (result && result.Items) || [];
                });
            });
        });
    }

    function _resolveLang() {
        return (document.documentElement.lang || 'en').toLowerCase().indexOf('fr') === 0 ? 'fr' : 'en';
    }

    function _currentDetailItemId() {
        var match = /[#&?]id=([a-f0-9-]+)/i.exec(location.hash);
        return match ? match[1] : null;
    }

    function _isSupportedForCollections(item) {
        return !!item && (item.Type === 'Movie' || item.Type === 'Series');
    }

    function _isBoxSet(item) {
        return !!item && item.Type === 'BoxSet';
    }

    function _sortFields() {
        var t = _labels[_resolveLang()];
        return [
            { value: 'SortName', label: t.sortTitle },
            { value: 'PremiereDate', label: t.releaseDate },
            { value: 'DateCreated', label: t.dateAdded },
            { value: 'CommunityRating', label: t.communityRating },
            { value: 'CriticRating', label: t.criticRating }
        ];
    }

    function _labelFor(options, value) {
        for (var i = 0; i < options.length; i++) {
            if (options[i].value === value) { return options[i].label; }
        }
        return options[0] && options[0].label;
    }

    function _storageKeys(collectionId) {
        return {
            sort: 'jux-collection-sort-' + collectionId,
            order: 'jux-collection-order-' + collectionId
        };
    }

    // -------------------------------------------------------------------------
    // "Included In" -- item detail page (Movie/Series only)
    // -------------------------------------------------------------------------

    function _tryIncludedIn() {
        var itemId = _currentDetailItemId();
        if (!itemId) {
            return;
        }

        var activePage = document.querySelector('.libraryPage:not(.hide)');
        if (!activePage) {
            return;
        }

        if (activePage.dataset.juxCollectionsChecked === itemId ||
            activePage.dataset.juxCollectionsPending === itemId ||
            activePage.querySelector('.jux-collections-section')) {
            return;
        }

        if (!window.ApiClient) {
            return;
        }

        var userId = window.ApiClient.getCurrentUserId();
        if (!userId) {
            return;
        }

        // Marked synchronously, before any async call -- the MutationObserver this is called from
        // fires many times in quick succession while the DOM settles, and each of those calls this
        // function again well before the first one's async chain below resolves. Without a
        // synchronous in-flight guard, several concurrent calls would all pass the checks above and
        // each build/insert their own "Included In" section (confirmed live: produced 2 duplicate
        // sections). Cleared on failure so a later retry isn't permanently blocked.
        activePage.dataset.juxCollectionsPending = itemId;

        _getItem(userId, itemId).then(function (item) {
            if (!_isSupportedForCollections(item)) {
                // Final: an item's Type never changes, so there's no point re-checking on every
                // future mutation for this same item id.
                activePage.dataset.juxCollectionsChecked = itemId;
                return;
            }

            return _getIncludedIn(userId, itemId).then(function (boxSets) {
                if (boxSets.length === 0) {
                    // Genuinely final: this item belongs to no visible collection.
                    activePage.dataset.juxCollectionsChecked = itemId;
                    return;
                }

                var api = window.JellyfinAPI;
                if (!api || !api.cardBuilder) {
                    // Not final -- cardBuilder may only become available after a later Home visit
                    // this session. Deliberately does NOT set juxCollectionsChecked, so the next
                    // MutationObserver tick/hashchange retries (from the cached results, without a
                    // new request) instead of leaving this permanently unrendered.
                    return;
                }

                activePage.dataset.juxCollectionsChecked = itemId;

                var section = _buildCollectionsSectionSkeleton(_labels[_resolveLang()].includedIn);
                var itemsContainer = section.querySelector('.itemsContainer');
                itemsContainer.innerHTML = api.cardBuilder.getCardsHtml({
                    items: boxSets,
                    // true ("overflow" shape) matches the larger card size native horizontal
                    // scroller sections (e.g. More Like This) use on this page -- confirmed live
                    // that `false` here rendered noticeably smaller cards than every native
                    // section around it (portraitCard vs overflowPortraitCard).
                    shape: api.getPortraitShape(true),
                    showTitle: true,
                    overlayText: false,
                    centerText: true,
                    lazy: true,
                    lines: 2
                });
                _loadCardImages(itemsContainer);

                var similar = activePage.querySelector('#similarCollapsible');
                if (similar) {
                    similar.parentNode.insertBefore(section, similar);
                } else {
                    var detailPageContent = activePage.querySelector('.detailPageContent');
                    if (detailPageContent) {
                        detailPageContent.appendChild(section);
                    }
                }
            });
        }).catch(function (err) {
            console.error('[JellyUX] Included In check failed:', err);
        }).finally(function () {
            if (activePage.dataset.juxCollectionsPending === itemId) {
                delete activePage.dataset.juxCollectionsPending;
            }
        });
    }

    function _buildCollectionsSectionSkeleton(title) {
        var wrapper = document.createElement('div');
        wrapper.innerHTML =
            // Confirmed live on jellyux-test: detail-page sections (Next Up, More Like This) use a
            // bare <h2 class="sectionTitle sectionTitle-cards padded-right"> with no wrapping
            // container -- the sectionTitleContainer/padded-left combo used for home-page widgets
            // (jux-homepage.js) doesn't apply here and was adding ~60px of unwanted left indentation
            // relative to every other section on this page.
            '<div class="verticalSection detailVerticalSection jux-collections-section">' +
            '<h2 class="sectionTitle sectionTitle-cards padded-right">' + _escHtml(title) + '</h2>' +
            // no-padding confirmed live as required: without it the scroller applies its own default
            // left padding on top of the outer section's padding, misaligning the cards (but not the
            // title, which sits outside the scroller) ~55px right of every other section on the page.
            '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true">' +
            '<div is="emby-itemscontainer" class="itemsContainer scrollSlider focuscontainer-x"></div>' +
            '</div>' +
            '</div>';
        return wrapper.firstElementChild;
    }

    // -------------------------------------------------------------------------
    // Collection (BoxSet) page sort
    // -------------------------------------------------------------------------

    function _tryCollectionSort() {
        var itemId = _currentDetailItemId();
        if (!itemId) {
            return;
        }

        var activePage = document.querySelector('.libraryPage:not(.hide)');
        if (!activePage) {
            return;
        }

        if (activePage.querySelector('.jux-collection-sort-btn') ||
            activePage.dataset.juxCollectionSortChecked === itemId ||
            activePage.dataset.juxCollectionSortPending === itemId) {
            return;
        }

        if (!window.ApiClient) {
            return;
        }

        var userId = window.ApiClient.getCurrentUserId();
        if (!userId) {
            return;
        }

        // See the matching comment in _tryIncludedIn above -- same synchronous in-flight guard,
        // needed for the same reason (the button-existence check above can't see a button that
        // hasn't been created yet by a still-pending earlier call).
        activePage.dataset.juxCollectionSortPending = itemId;

        _getItem(userId, itemId).then(function (item) {
            if (!_isBoxSet(item)) {
                // Final, same reasoning as in _tryIncludedIn: only a collection gets a sort button.
                activePage.dataset.juxCollectionSortChecked = itemId;
                return;
            }

            // Confirmed live on jellyux-test: the collection's item grid is a plain .itemsContainer
            // carrying the collectionItemsContainer class, inside a .verticalSection whose own
            // .sectionTitle is the natural place for a sort button (no separate ".collectionItems"
            // wrapper on this Jellyfin Web build).
            var itemsContainer = activePage.querySelector('.itemsContainer.collectionItemsContainer');
            if (!itemsContainer) {
                return;
            }

            var section = itemsContainer.closest('.verticalSection');
            var titleContainer = section ? section.querySelector('.sectionTitleContainer, .sectionTitle') : null;
            if (!titleContainer) {
                return;
            }

            var keys = _storageKeys(itemId);
            var state = {
                sortBy: localStorage.getItem(keys.sort) || 'SortName',
                sortOrder: localStorage.getItem(keys.order) || 'Ascending'
            };

            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'jux-sort-button jux-collection-sort-btn';
            button.innerHTML =
                '<span class="material-icons sort" aria-hidden="true"></span>' +
                '<span class="jux-sort-button-label">' + _escHtml(_labelFor(_sortFields(), state.sortBy)) + '</span>' +
                '<span class="material-icons arrow_drop_down" aria-hidden="true"></span>';

            button.addEventListener('click', function () {
                if (!window.JuxUI) {
                    return;
                }

                window.JuxUI.openSortDialog({
                    title: _labels[_resolveLang()].sortDialogTitle,
                    sortOptions: _sortFields(),
                    currentSortBy: state.sortBy,
                    currentSortOrder: state.sortOrder,
                    onChange: function (sortBy, sortOrder) {
                        state.sortBy = sortBy;
                        state.sortOrder = sortOrder;
                        localStorage.setItem(keys.sort, sortBy);
                        localStorage.setItem(keys.order, sortOrder);
                        button.querySelector('.jux-sort-button-label').textContent = _labelFor(_sortFields(), sortBy);
                        var liveItemsContainer = activePage.querySelector('.itemsContainer.collectionItemsContainer') || itemsContainer;
                        _resortCollection(itemId, liveItemsContainer, state);
                    }
                });
            });

            titleContainer.appendChild(button);

            if (state.sortBy !== 'SortName' || state.sortOrder !== 'Ascending') {
                // Delayed: confirmed live that resorting immediately here can race Jellyfin's own
                // native fetch/render of this same collection page, which replaces the .itemsContainer
                // element with a fresh one once its own async items fetch completes -- if that happens
                // after our resort, our sorted cards render into a node that's already been detached
                // from the document, invisibly. A short delay gives native rendering time to settle
                // first, and the container is re-queried fresh below (rather than reusing the possibly
                // now-detached `itemsContainer` captured above) so a resort scheduled just before a
                // native replacement still targets the live element.
                setTimeout(function () {
                    var liveItemsContainer = activePage.querySelector('.itemsContainer.collectionItemsContainer') || itemsContainer;
                    _resortCollection(itemId, liveItemsContainer, state);
                }, 1000);
            }
        }).catch(function (err) {
            console.error('[JellyUX] Collection sort setup failed:', err);
        }).finally(function () {
            if (activePage.dataset.juxCollectionSortPending === itemId) {
                delete activePage.dataset.juxCollectionSortPending;
            }
        });
    }

    function _resortCollection(collectionId, itemsContainer, state) {
        var api = window.JellyfinAPI;
        if (!api || !api.cardBuilder || !window.ApiClient) {
            return;
        }

        var userId = window.ApiClient.getCurrentUserId();
        var url = window.ApiClient.getUrl('Items', {
            userId: userId,
            ParentId: collectionId,
            SortBy: state.sortBy,
            SortOrder: state.sortOrder,
            Fields: 'PrimaryImageAspectRatio'
        });

        window.ApiClient.getJSON(url).then(function (result) {
            var items = (result && result.Items) || [];
            itemsContainer.innerHTML = api.cardBuilder.getCardsHtml({
                items: items,
                shape: api.getPortraitShape(false),
                showTitle: true,
                overlayText: false,
                centerText: true,
                lazy: true,
                lines: 2
            });
            _loadCardImages(itemsContainer);
        }).catch(function (err) {
            console.error('[JellyUX] Collection re-sort fetch failed:', err);
        });
    }

    // See jux-watchlist.js -- a JUX-built card grid never gets Jellyfin's native lazy-image loading,
    // so the background image has to be applied manually.
    function _loadCardImages(container) {
        var lazyImages = container.querySelectorAll('.cardImageContainer.lazy[data-src]');
        Array.prototype.forEach.call(lazyImages, function (el) {
            var src = el.getAttribute('data-src');
            if (!src) { return; }
            el.style.backgroundImage = 'url(\'' + src + '\')';
            el.classList.remove('lazy');
            el.classList.add('lazy-image-fadein-fast');
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

    window.juxCollections.init();

    // Guarded UMD-lite export (same convention as jux-watchlist.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _isSupportedForCollections: _isSupportedForCollections,
            _isBoxSet: _isBoxSet,
            _sortFields: _sortFields,
            _labelFor: _labelFor,
            _storageKeys: _storageKeys,
            _buildCollectionsSectionSkeleton: _buildCollectionsSectionSkeleton,
            _currentDetailItemId: _currentDetailItemId,
            _escHtml: _escHtml,
            _syncObserver: _syncObserver
        };
    }
})();
