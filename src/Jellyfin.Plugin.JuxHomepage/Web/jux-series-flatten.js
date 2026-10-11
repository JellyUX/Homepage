'use strict';

// JellyUX single-season show flattening.
// TODO_V3.md Phase 7.1: on a series detail page, if the series has exactly one season, the native
// "Seasons" section (a single card leading to a second click-through) is replaced by the season's
// episodes shown directly on the series page -- one click saved for the most common "mini-series"/
// "TV movie in parts" case.
//
// Page-hook idiom: reuses the exact detection already proven live by jux-card-hooks.js (item id read
// from location.hash, a MutationObserver on document.body, a hashchange re-arm) -- confirmed with the
// user to keep, rather than adopting KefinTweaks' window.Emby.Page.onViewShow monkey-patch, so this
// project doesn't grow a second kind of fragile dependency on undocumented Jellyfin Web internals.
// This file owns its own small MutationObserver instance (not shared with jux-card-hooks.js, whose
// observer is explicitly reserved for Phase 7.3's infinite scroll).
//
// Native seasons container: confirmed live on jellyux-test (Phase 7 planning) that this Jellyfin Web
// build actually renders #listChildrenCollapsible (not #childrenCollapsible, which is present in the
// DOM but always hidden/empty on this build) -- both are checked, in that order, for resilience across
// builds, matching the fallback already documented in the KefinTweaks reference implementation.
//
// Graceful fallback (must-honor): window.JellyfinAPI.cardBuilder is only populated after the user has
// visited Home at least once this session (see jux-card-hooks.js's own comment on this). A series page
// reached via a direct/deep link can arrive here first. If cardBuilder is unavailable, this file does
// nothing at all -- it never hides the native seasons section without providing a replacement.
(function () {
    if (typeof window.juxSeriesFlatten !== 'undefined') {
        return;
    }

    var _observer = null;

    window.juxSeriesFlatten = {
        init: function () {
            window.addEventListener('hashchange', function () {
                setTimeout(_syncObserver, 400);
            });

            _syncObserver();
        }
    };

    // One observer for this module, connected only while a detail page (#...?id=) is shown and
    // disconnected when the user leaves it (same idiom as jux-collections.js).
    function _syncObserver() {
        if (!_currentDetailItemId()) {
            if (_observer) {
                _observer.disconnect();
                _observer = null;
            }
            return;
        }

        if (!_observer) {
            _observer = new MutationObserver(_tryFlatten);
            _observer.observe(document.body, { childList: true, subtree: true });
        }

        _tryFlatten();
    }

    // Network results of the current detail item, one shared Promise per call, so repeated mutation
    // batches never resend a request for the same item (see jux-collections.js). A failed request
    // stays cached until the user opens another item.
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

    function _resolveLang() {
        return (document.documentElement.lang || 'en').toLowerCase().indexOf('fr') === 0 ? 'fr' : 'en';
    }

    var _labels = {
        en: { seasonFallback: 'Season {number}' },
        fr: { seasonFallback: 'Saison {number}' }
    };

    function _currentDetailItemId() {
        var match = /[#&?]id=([a-f0-9-]+)/i.exec(location.hash);
        return match ? match[1] : null;
    }

    function _shouldFlatten(item) {
        return !!item && item.Type === 'Series' && item.ChildCount === 1;
    }

    function _seasonTitle(episode) {
        var lang = _resolveLang();
        if (episode && episode.SeasonName) {
            return episode.SeasonName;
        }
        var number = episode && episode.ParentIndexNumber != null ? episode.ParentIndexNumber : 1;
        return _labels[lang].seasonFallback.replace('{number}', String(number));
    }

    function _findSeasonsContainer(activePage) {
        return activePage.querySelector('#listChildrenCollapsible') || activePage.querySelector('#childrenCollapsible');
    }

    function _tryFlatten() {
        var itemId = _currentDetailItemId();
        if (!itemId) {
            return;
        }

        var activePage = document.querySelector('.libraryPage:not(.hide)');
        if (!activePage) {
            return;
        }

        if (activePage.dataset.juxFlattenedSeriesId === itemId ||
            activePage.dataset.juxFlattenPending === itemId ||
            activePage.querySelector('.jux-flattened-season-section')) {
            return;
        }

        if (!window.ApiClient) {
            return;
        }

        var userId = window.ApiClient.getCurrentUserId();
        if (!userId) {
            return;
        }

        // Marked synchronously, before any async call -- the MutationObserver this runs from fires
        // repeatedly while the DOM settles, well before the async chain below resolves. Without this,
        // several concurrent calls all pass the existence checks above (the section doesn't exist yet)
        // and each build/insert their own flattened section -- confirmed live as the same duplicate-
        // section bug found and fixed in jux-collections.js's _tryIncludedIn. Cleared in .finally() so
        // a genuine retry (e.g. cardBuilder becoming available later) isn't permanently blocked.
        activePage.dataset.juxFlattenPending = itemId;

        _cached(itemId, 'item', function () {
            return window.ApiClient.getItem(userId, itemId);
        }).then(function (item) {
            if (!_shouldFlatten(item)) {
                // Final: an item's Type/ChildCount never changes, so there's no point re-checking on
                // every future mutation for this same item id.
                activePage.dataset.juxFlattenedSeriesId = itemId;
                return;
            }

            var seasonsContainer = _findSeasonsContainer(activePage);
            if (!seasonsContainer) {
                return;
            }

            return _cached(itemId, 'episodes', function () {
                return window.ApiClient.getJSON(window.ApiClient.getUrl('Shows/' + itemId + '/Episodes', {
                    userId: userId,
                    Fields: 'UserData,PrimaryImageAspectRatio'
                }));
            }).then(function (result) {
                var episodes = (result && result.Items) || [];
                if (episodes.length === 0) {
                    return;
                }

                var api = window.JellyfinAPI;
                if (!api || !api.cardBuilder) {
                    // No replacement can be built -- leave the native seasons section fully intact.
                    return;
                }

                var section = _buildFlattenedSectionSkeleton(_seasonTitle(episodes[0]));
                var itemsContainer = section.querySelector('.itemsContainer');
                itemsContainer.innerHTML = api.cardBuilder.getCardsHtml({
                    items: episodes,
                    shape: api.getBackdropShape(true),
                    showTitle: true,
                    overlayText: false,
                    overlayPlayButton: true,
                    centerText: true,
                    lazy: true,
                    lines: 2
                });
                _loadCardImages(itemsContainer);

                seasonsContainer.parentNode.insertBefore(section, seasonsContainer);

                var nativeItemsContainer = seasonsContainer.querySelector('.itemsContainer');
                if (nativeItemsContainer && nativeItemsContainer.children.length <= 1) {
                    seasonsContainer.style.display = 'none';
                }

                activePage.dataset.juxFlattenedSeriesId = itemId;
            });
        }).catch(function (err) {
            console.error('[JellyUX] Series flatten check failed:', err);
        }).finally(function () {
            if (activePage.dataset.juxFlattenPending === itemId) {
                delete activePage.dataset.juxFlattenPending;
            }
        });
    }

    function _buildFlattenedSectionSkeleton(title) {
        var wrapper = document.createElement('div');
        wrapper.innerHTML =
            // Confirmed live on jellyux-test: detail-page sections (Next Up, More Like This) use a
            // bare <h2 class="sectionTitle sectionTitle-cards padded-right"> with no wrapping
            // container -- the sectionTitleContainer/padded-left combo used for home-page widgets
            // (jux-homepage.js) doesn't apply here and was adding ~60px of unwanted left indentation
            // relative to every other section on this page.
            '<div class="verticalSection detailVerticalSection jux-flattened-season-section">' +
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

    window.juxSeriesFlatten.init();

    // Guarded UMD-lite export (same convention as jux-watchlist.js), so Vitest can exercise the pure
    // functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _shouldFlatten: _shouldFlatten,
            _seasonTitle: _seasonTitle,
            _buildFlattenedSectionSkeleton: _buildFlattenedSectionSkeleton,
            _currentDetailItemId: _currentDetailItemId,
            _escHtml: _escHtml,
            _syncObserver: _syncObserver
        };
    }
})();
