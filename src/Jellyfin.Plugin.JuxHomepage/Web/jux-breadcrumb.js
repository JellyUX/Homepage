'use strict';

// JellyUX breadcrumb navigation. TODO_V3.md Phase 8.1.
//
// Shows a clickable trail (Library > ... > current item) on item detail pages: Movie (with an
// optional Collection segment), Series/Season/Episode, and MusicArtist/MusicAlbum/Audio. A single
// switch on item.Type builds the segment list -- no per-type file/duplication, per the roadmap's own
// "pas de duplication par type" instruction.
//
// Page-hook idiom: same as jux-series-flatten.js/jux-collections.js -- JuxCore.watchDetailPages reads
// the item id from location.hash, observes the DOM only while a detail page is shown and re-syncs on
// hash changes. Kept for consistency with the rest of the project rather than adopting KefinTweaks'
// window.Emby.Page monkey-patch (its breadcrumbs.js is used only as a reference for hierarchy-resolution logic, e.g.
// which BaseItemDto fields are already populated -- SeriesName/SeasonId/AlbumArtists -- so no extra
// per-level network round trip is needed beyond the single ApiClient.getItem call).
//
// Anchor: unlike the Phase 7 sections (anchored inside the detail page's own content, which Jellyfin
// Web's page DOM cache can duplicate across in-app navigations -- see the #moviesTab bug documented
// in jux-card-hooks.js), the breadcrumb is anchored in the persistent header shell
// (.skinHeader .headerLeft), confirmed live to be a singleton that survives page navigation rather
// than being recreated. The single <nav class="jux-breadcrumb"> element this file creates is reused
// (not recreated) across every navigation, and its own dataset is what tracks in-flight/rendered
// item ids -- this sidesteps the duplicate-element trap entirely, since there is only ever one.
(function () {
    if (typeof window.juxBreadcrumb !== 'undefined') {
        return;
    }

    var _supportedTypes = ['Movie', 'Series', 'Season', 'Episode', 'MusicArtist', 'MusicAlbum', 'Audio'];

    window.juxBreadcrumb = {
        init: function () {
            // Leaving a detail page still renders once, which hides the breadcrumb.
            window.JuxCore.watchDetailPages(_tryRenderBreadcrumb, _tryRenderBreadcrumb);
        }
    };

    function _isDetailPage(hash) {
        return /^#\/details([/?]|$)/i.test(hash || '');
    }

    function _isSupportedType(item) {
        return !!item && _supportedTypes.indexOf(item.Type) !== -1;
    }

    function _pad(num) {
        var str = String(num);
        return str.length < 2 ? '0' + str : str;
    }

    function _seasonFallbackName(item) {
        var lang = window.JuxCore.resolveLang();
        var number = item && item.IndexNumber != null ? item.IndexNumber : 1;
        return window.JuxCore.labels('breadcrumb', lang).seasonFallback.replace('{number}', String(number));
    }

    function _episodeLabel(item) {
        if (item.ParentIndexNumber != null && item.IndexNumber != null) {
            return item.ParentIndexNumber + 'x' + _pad(item.IndexNumber) + ' - ' + (item.Name || '');
        }
        return item.Name || '';
    }

    // Maps an item's type to the library route its ancestor CollectionFolder should link to.
    // Movie/Series/Season/Episode/Music* all resolve through the same ancestor lookup
    // (_resolveAncestorLibrary) -- only the URL prefix differs by media kind.
    function _libraryHashFor(itemType, topParentId) {
        if (!topParentId) {
            return null;
        }

        var prefix;
        if (itemType === 'Movie') {
            prefix = '#/movies';
        } else if (itemType === 'Series' || itemType === 'Season' || itemType === 'Episode') {
            prefix = '#/tv';
        } else if (itemType === 'MusicArtist' || itemType === 'MusicAlbum' || itemType === 'Audio') {
            prefix = '#/music';
        } else {
            return null;
        }

        return prefix + '?topParentId=' + topParentId;
    }

    // Pure: builds the ordered breadcrumb segment list for a given item. No DOM/network access --
    // ancestorLibrary ({Id, Name} or null) and collectionRef ({CollectionId, CollectionName} or
    // null, Movies only) are already resolved by the caller. Returns [] for unsupported types.
    function _buildBreadcrumbSegments(item, ancestorLibrary, collectionRef) {
        if (!_isSupportedType(item)) {
            return [];
        }

        var segments = [];
        if (ancestorLibrary) {
            segments.push({ text: ancestorLibrary.Name, url: _libraryHashFor(item.Type, ancestorLibrary.Id) });
        }

        switch (item.Type) {
            case 'Movie':
                if (collectionRef) {
                    segments.push({ text: collectionRef.CollectionName, url: '#/details?id=' + collectionRef.CollectionId });
                }
                segments.push({ text: item.Name, url: null });
                break;

            case 'Series':
                segments.push({ text: item.Name, url: null });
                break;

            case 'Season':
                if (item.SeriesId) {
                    segments.push({ text: item.SeriesName || '', url: '#/details?id=' + item.SeriesId });
                }
                segments.push({ text: item.Name || _seasonFallbackName(item), url: null });
                break;

            case 'Episode':
                if (item.SeriesId) {
                    segments.push({ text: item.SeriesName || '', url: '#/details?id=' + item.SeriesId });
                }
                if (item.SeasonId) {
                    segments.push({ text: item.SeasonName || '', url: '#/details?id=' + item.SeasonId });
                }
                segments.push({ text: _episodeLabel(item), url: null });
                break;

            case 'MusicArtist':
                segments.push({ text: item.Name, url: null });
                break;

            case 'MusicAlbum': {
                var albumArtist = item.AlbumArtists && item.AlbumArtists[0];
                if (albumArtist) {
                    segments.push({ text: albumArtist.Name, url: '#/details?id=' + albumArtist.Id });
                }
                segments.push({ text: item.Name, url: null });
                break;
            }

            case 'Audio': {
                var trackArtist = item.AlbumArtists && item.AlbumArtists[0];
                if (trackArtist) {
                    segments.push({ text: trackArtist.Name, url: '#/details?id=' + trackArtist.Id });
                }
                if (item.AlbumId) {
                    segments.push({ text: item.Album || '', url: '#/details?id=' + item.AlbumId });
                }
                segments.push({ text: item.Name, url: null });
                break;
            }

            default:
                return [];
        }

        return segments;
    }

    // Finds the ancestor CollectionFolder (the actual library, e.g. "Movies"/"Films") via the
    // standard GET /Items/{itemId}/Ancestors endpoint -- confirmed via the Jellyfin OpenAPI spec and
    // the KefinTweaks reference implementation (breadcrumbs.js). Falls back to UserRootFolder if no
    // CollectionFolder ancestor is found (matches the same fallback in that reference).
    function _resolveAncestorLibrary(itemId) {
        return window.ApiClient.getAncestorItems(itemId).then(function (ancestors) {
            ancestors = ancestors || [];
            var collectionFolder = null;
            var userRootFolder = null;
            for (var i = 0; i < ancestors.length; i++) {
                if (!collectionFolder && ancestors[i].Type === 'CollectionFolder') { collectionFolder = ancestors[i]; }
                if (!userRootFolder && ancestors[i].Type === 'UserRootFolder') { userRootFolder = ancestors[i]; }
            }
            var lib = collectionFolder || userRootFolder;
            return lib ? { Id: lib.Id, Name: lib.Name } : null;
        }).catch(function () {
            return null;
        });
    }

    // Reuses the Collections/IncludedIn endpoint already added in Phase 7.2 -- no new backend call.
    // If a movie belongs to more than one collection, only the first is shown (a breadcrumb is a
    // single linear trail, not a tree); documented as expected in the Phase 8 manual test.
    function _fetchCollectionRef(itemId) {
        return window.JuxCore.getIncludedIn(itemId).then(function (refs) {
            return (refs && refs[0]) || null;
        }).catch(function () {
            return null;
        });
    }

    function _findOrCreateContainer() {
        var existing = document.querySelector('.jux-breadcrumb');
        if (existing) {
            return existing;
        }

        var headerLeft = document.querySelector('.skinHeader .headerLeft');
        if (!headerLeft) {
            return null;
        }

        var nav = document.createElement('nav');
        nav.className = 'jux-breadcrumb';
        nav.style.display = 'none';
        headerLeft.appendChild(nav);
        return nav;
    }

    function _renderBreadcrumb(container, segments) {
        if (!segments || segments.length === 0) {
            container.innerHTML = '';
            container.style.display = 'none';
            return;
        }

        var html = segments.map(function (seg, i) {
            var isLast = i === segments.length - 1;
            var text = window.JuxCore.escHtml(seg.text || '');
            if (isLast || !seg.url) {
                return '<span class="jux-breadcrumb-current">' + text + '</span>';
            }
            return '<a class="jux-breadcrumb-link" href="' + window.JuxCore.escHtml(seg.url) + '">' + text + '</a>';
        }).join('<span class="jux-breadcrumb-sep">›</span>');

        container.innerHTML = html;
        container.style.display = '';
    }

    function _hideBreadcrumb() {
        var el = document.querySelector('.jux-breadcrumb');
        if (el) {
            el.style.display = 'none';
        }
    }

    function _tryRenderBreadcrumb() {
        if (typeof location === 'undefined' || typeof document === 'undefined') {
            return;
        }

        if (!_isDetailPage(location.hash)) {
            _hideBreadcrumb();
            return;
        }

        var itemId = window.JuxCore.currentDetailItemId();
        if (!itemId) {
            _hideBreadcrumb();
            return;
        }

        var container = _findOrCreateContainer();
        if (!container) {
            // .skinHeader .headerLeft not in the DOM yet -- the body MutationObserver will retry.
            return;
        }

        if (container.dataset.juxBreadcrumbItemId === itemId) {
            return;
        }

        if (!window.ApiClient) {
            return;
        }

        var userId = window.ApiClient.getCurrentUserId();
        if (!userId) {
            return;
        }

        // In-flight guard (JuxCore.runInFlight), as in jux-collections.js and jux-series-flatten.js.
        window.JuxCore.runInFlight(container, 'juxBreadcrumbPending', itemId, function () {
            return _renderFor(container, userId, itemId);
        });
    }

    function _renderFor(container, userId, itemId) {
        return window.JuxCore.getItem(userId, itemId).then(function (item) {
            if (!_isSupportedType(item)) {
                // Final: an item's Type never changes, so no point re-checking this same id again.
                if (container.dataset.juxBreadcrumbPending === itemId) {
                    container.dataset.juxBreadcrumbItemId = itemId;
                    _hideBreadcrumb();
                }
                return;
            }

            return _resolveAncestorLibrary(itemId).then(function (ancestorLibrary) {
                var collectionPromise = item.Type === 'Movie' ? _fetchCollectionRef(itemId) : Promise.resolve(null);

                return collectionPromise.then(function (collectionRef) {
                    // Staleness guard: if the hash changed again while these calls were in flight,
                    // container.dataset.juxBreadcrumbPending now holds a different, newer item id --
                    // discard this now-outdated result rather than overwriting the newer render.
                    if (container.dataset.juxBreadcrumbPending !== itemId) {
                        return;
                    }

                    var segments = _buildBreadcrumbSegments(item, ancestorLibrary, collectionRef);
                    _renderBreadcrumb(container, segments);
                    container.dataset.juxBreadcrumbItemId = itemId;
                });
            });
        }).catch(function (err) {
            console.error('[JellyUX] Breadcrumb render failed:', err);
        });
    }

    window.juxBreadcrumb.init();

    // Guarded UMD-lite export (same convention as the rest of the project), so Vitest can exercise
    // the pure functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _currentDetailItemId: window.JuxCore.currentDetailItemId,
            _isDetailPage: _isDetailPage,
            _isSupportedType: _isSupportedType,
            _libraryHashFor: _libraryHashFor,
            _buildBreadcrumbSegments: _buildBreadcrumbSegments,
            _seasonFallbackName: _seasonFallbackName,
            _episodeLabel: _episodeLabel,
        };
    }
})();
