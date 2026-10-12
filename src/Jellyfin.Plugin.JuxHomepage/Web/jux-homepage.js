'use strict';

// JellyUX Homepage - home screen override.
// This script is injected into Jellyfin's index.html by the plugin.
// window.JellyfinAPI is populated by the loadSections splice fragment (jux-loadsections-inject.js)
// before control reaches window.JUXHomepage.loadSections.

(function () {
    if (typeof window.JUXHomepage !== 'undefined') {
        return;
    }

    window.JUXHomepage = {
        originalLoadSections: null,

        // Called by the splice fragment to replace Jellyfin's home rendering.
        loadSections: function (elem, apiClient, user, userSettings, page) {
            var self = this;

            // Graceful disable on TV layout only. Windows/Android native apps also expose
            // window.NativeShell, but they run the exact same jellyfin-web bundle in an embedded
            // WebView, loaded live from the connected server -- same DOM, same loadSections chunk,
            // same FileTransformation patches -- so there is no technical reason to bail out there.
            if (document.body.classList.contains('layout-tv')) {
                return _fallback(self, elem, apiClient, user, userSettings);
            }

            if (!_isHomePage()) {
                return _fallback(self, elem, apiClient, user, userSettings);
            }

            return _fetchMeta(apiClient).then(function (meta) {
                if (!meta || !meta.Enabled) {
                    return _fallback(self, elem, apiClient, user, userSettings);
                }
                return _renderHome(self, elem, apiClient, user, userSettings);
            }).catch(function (err) {
                console.error('[JellyUX] meta fetch failed, falling back:', err);
                return _fallback(self, elem, apiClient, user, userSettings);
            });
        }
    };

    // -------------------------------------------------------------------------
    // Fallback
    // -------------------------------------------------------------------------

    function _fallback(self, elem, apiClient, user, userSettings) {
        if (typeof window.JUXHomepage.originalLoadSections === 'function') {
            return window.JUXHomepage.originalLoadSections.call(self, elem, apiClient, user, userSettings);
        }
    }

    // -------------------------------------------------------------------------
    // Home-page detection (ported from HSS reference implementation)
    // -------------------------------------------------------------------------

    function _isHomePage() {
        var href = (location.href || '').toLowerCase();
        var hash = (location.hash || '').toLowerCase();

        var isHomeRoute =
            /(^#?!?\/?)(home)(\.html)?([/?&]|$)/.test(hash) ||
            hash.indexOf('home.html') !== -1 ||
            href.indexOf('/web/index.html#!/home') !== -1;

        var isHomeDom =
            document.querySelector('.sections') !== null &&
            (
                document.querySelector('.homePage') !== null ||
                document.querySelector('.page.homePage') !== null ||
                document.getElementById('indexPage') !== null
            );

        return !!(isHomeRoute || isHomeDom);
    }

    // -------------------------------------------------------------------------
    // Meta check
    // -------------------------------------------------------------------------

    function _fetchMeta(apiClient) {
        return apiClient.getJSON(apiClient.getUrl('JuxHomepage/meta'));
    }

    // -------------------------------------------------------------------------
    // Home rendering
    // -------------------------------------------------------------------------

    function _renderHome(self, elem, apiClient, user, userSettings) {
        var userId = apiClient.getCurrentUserId();
        var isFirstActivation = !elem.classList.contains('jux-homepage-active');

        if (isFirstActivation) {
            elem.innerHTML = '';
            elem.classList.add('homeSectionsContainer', 'jux-homepage-active');
        }

        // Only the very first page load falls back to native rendering on failure: at that point
        // nothing has been rendered yet, so a permanently blank home screen would otherwise result.
        // Later lazy-loaded pages (see _setupLazyLoader) intentionally do NOT fall back -- by then
        // JUX has already rendered a working layout, and swapping to native mid-page would break it.
        return _loadPage(elem, apiClient, userId, userSettings, 0, isFirstActivation ? function () {
            elem.classList.remove('jux-homepage-active', 'homeSectionsContainer');
            return _fallback(self, elem, apiClient, user, userSettings);
        } : undefined).then(function (result) {
            // TODO_V3.md Phase 7.3 fix: the lazy loader used to be armed above, before this first page
            // had actually rendered anything -- on a short layout its sentinel sat in a still-empty
            // container and intersected almost immediately, firing page=1 in a near-simultaneous race
            // with page=0. Arming it only here, after page 0 has resolved, removes that race; a short
            // (but non-empty) first page still correctly triggering an immediate page=1 fill afterward
            // is the intended lazy-fill-viewport behavior, not a bug. The isFirstActivation re-check
            // guards against arming it when page 0 failed and fell back to native rendering above
            // (that failure path strips the "jux-homepage-active" class).
            // Armed only when page 0 reported a later page: a layout that fits in one page never
            // requests an empty one.
            if (isFirstActivation && elem.classList.contains('jux-homepage-active') && elem.dataset.juxHasMore === 'true') {
                _setupLazyLoader(elem, apiClient, userId, userSettings);
            }
            return result;
        });
    }

    function _loadPage(elem, apiClient, userId, userSettings, page, onFirstPageFailure) {
        return apiClient.getJSON(
            apiClient.getUrl('JuxHomepage/Sections', {
                userId: userId,
                page: page,
                lang: document.documentElement.lang || 'en'
            })
        ).then(function (response) {
            elem.dataset.juxHasMore = String(_hasMorePages(response));
            var descriptors = (response && response.Sections) || [];
            if (descriptors.length === 0) {
                return;
            }

            var tasks = descriptors.map(function (descriptor) {
                return _buildSection(elem, apiClient, userId, userSettings, descriptor);
            });

            return Promise.all(tasks).then(function () {
                // Trigger native lazy fetch on all new itemsContainers
                var containers = elem.querySelectorAll('.jux-items-container:not([data-jux-loaded])');
                Array.prototype.forEach.call(containers, function (c) {
                    c.setAttribute('data-jux-loaded', '1');
                    if (typeof c.resume === 'function') {
                        c.resume({ refresh: true });
                    }
                });
            });
        }).catch(function (err) {
            console.error('[JellyUX] Sections fetch failed (page ' + page + '):', err);
            if (typeof onFirstPageFailure === 'function') {
                return onFirstPageFailure();
            }
        });
    }

    // -------------------------------------------------------------------------
    // Section DOM building
    // -------------------------------------------------------------------------

    function _buildSection(elem, apiClient, userId, userSettings, descriptor) {
        var api = window.JellyfinAPI;
        var sectionEl = document.createElement('div');
        sectionEl.className =
            'verticalSection jux-widget-section jux-widget-' +
            descriptor.WidgetType.replace(/\./g, '-');

        // TODO_V3.md Phase 9.1: generic hook for any widget instance carrying a Theme (currently
        // only the Seasonal widget's presets set one) -- jux-homepage.css keys visual effects (e.g.
        // the Christmas snow overlay) off this attribute, scoped to this one section only.
        if (descriptor.Theme) {
            sectionEl.setAttribute('data-jux-theme', descriptor.Theme);
        }

        // Title block
        var titleContainer = document.createElement('div');
        titleContainer.className = 'sectionTitleContainer sectionTitleContainer-cards padded-left';

        if (descriptor.Route && api && !api.layoutManager.tv) {
            var href = _buildSeeAllHref(descriptor.Route);
            if (href) {
                titleContainer.innerHTML =
                    '<a is="emby-linkbutton" href="' + _escHtml(href) + '" ' +
                    'class="button-flat button-flat-mini sectionTitleTextButton">' +
                    '<h2 class="sectionTitle sectionTitle-cards jux-section-title">' +
                    _escHtml(descriptor.DisplayName) + '</h2>' +
                    '<span class="material-icons chevron_right" aria-hidden="true"></span>' +
                    '</a>';
            } else {
                titleContainer.innerHTML =
                    '<h2 class="sectionTitle sectionTitle-cards jux-section-title">' +
                    _escHtml(descriptor.DisplayName) + '</h2>';
            }
        } else {
            titleContainer.innerHTML =
                '<h2 class="sectionTitle sectionTitle-cards jux-section-title">' +
                _escHtml(descriptor.DisplayName) + '</h2>';
        }

        sectionEl.appendChild(titleContainer);

        // Scroller + items container
        var scroller = document.createElement('div');
        scroller.setAttribute('is', 'emby-scroller');
        scroller.className = 'padded-top-focusscale padded-bottom-focusscale';
        scroller.setAttribute('data-centerfocus', 'true');

        var itemsContainer = document.createElement('div');
        itemsContainer.setAttribute('is', 'emby-itemscontainer');
        itemsContainer.className =
            'itemsContainer scrollSlider focuscontainer-x jux-items-container';
        itemsContainer.setAttribute('data-monitor', 'videoplayback,markplayed');

        // Attach native fetch + render hooks
        var shapeFn = _getShapeFn(descriptor.ViewMode);
        var useEpisodeImages = userSettings && typeof userSettings.useEpisodeImagesInNextUpAndResume === 'function'
            ? userSettings.useEpisodeImagesInNextUpAndResume()
            : false;

        itemsContainer.fetchData = _makeFetchData(apiClient, userId, descriptor);
        itemsContainer.getItemsHtml = _makeGetItemsHtml(descriptor, shapeFn, useEpisodeImages);
        itemsContainer.parentContainer = sectionEl;

        scroller.appendChild(itemsContainer);
        sectionEl.appendChild(scroller);
        elem.appendChild(sectionEl);

        return Promise.resolve();
    }

    function _getShapeFn(viewMode) {
        var api = window.JellyfinAPI;
        if (!api) {
            return function (overflow) {
                return overflow ? 'overflowBackdropCard' : 'backdropCard';
            };
        }
        if (viewMode === 'Portrait') { return api.getPortraitShape; }
        if (viewMode === 'Square') { return api.getSquareShape; }
        return api.getBackdropShape;
    }

    // The native sections read the same Jellyfin routes as the native home page, with the query
    // their server widget mirrors (identical items and order, measured on 8096): other plugins hook
    // their filters on these routes and never see /JuxHomepage/Section, so Jellyfin Enhanced hidden
    // content, Remove from Continue Watching and Spoiler Guard apply here too (TODO_V4.md 4.3, F-15).
    var NATIVE_ROUTES = {
        'jux.native.continue-watching': { path: 'UserItems/Resume', params: { mediaTypes: 'Video' } },
        'jux.native.next-up': { path: 'Shows/NextUp', params: {} },
        'jux.native.recently-added-movies': { path: 'Items', params: _latestParams('Movie') },
        'jux.native.recently-added-shows': { path: 'Items', params: _latestParams('Series') },
        'jux.native.recently-added-episodes': { path: 'Items', params: _latestParams('Episode') }
    };

    function _latestParams(itemType) {
        return {
            includeItemTypes: itemType,
            sortBy: 'DateCreated',
            sortOrder: 'Descending',
            recursive: true,
            isMissing: false
        };
    }

    function _makeFetchData(apiClient, userId, descriptor) {
        var nativeRoute = NATIVE_ROUTES[descriptor.WidgetType];
        if (nativeRoute) {
            return function () {
                var params = {
                    userId: userId,
                    startIndex: 0,
                    limit: 20,
                    fields: 'PrimaryImageAspectRatio,DateCreated',
                    imageTypeLimit: 1,
                    enableImageTypes: 'Primary,Thumb,Backdrop',
                    enableTotalRecordCount: false
                };
                Object.keys(nativeRoute.params).forEach(function (key) {
                    params[key] = nativeRoute.params[key];
                });
                return apiClient.getJSON(apiClient.getUrl(nativeRoute.path, params));
            };
        }

        return function () {
            return apiClient.getJSON(
                apiClient.getUrl('JuxHomepage/Section/' + descriptor.WidgetType, {
                    userId: userId,
                    additionalData: descriptor.AdditionalData || undefined,
                    startIndex: 0,
                    limit: 20
                })
            );
        };
    }

    function _makeGetItemsHtml(descriptor, shapeFn, useEpisodeImages) {
        return function (items) {
            var api = window.JellyfinAPI;
            if (!api || !api.cardBuilder) { return ''; }
            return api.cardBuilder.getCardsHtml({
                items: items,
                shape: shapeFn(true),
                preferThumb: descriptor.ViewMode === 'Portrait' ? null : 'auto',
                inheritThumb: !useEpisodeImages,
                overlayText: false,
                showTitle: true,
                showParentTitle: true,
                lazy: true,
                overlayPlayButton: true,
                context: 'home',
                centerText: true,
                allowBottomPadding: false,
                cardLayout: false,
                showYear: true,
                lines: 2
            });
        };
    }

    // -------------------------------------------------------------------------
    // Lazy loading via IntersectionObserver
    // -------------------------------------------------------------------------

    function _setupLazyLoader(elem, apiClient, userId, userSettings) {
        if (typeof IntersectionObserver === 'undefined') {
            return;
        }

        var currentPage = 0;
        var loading = false;
        var finished = false;

        var sentinel = document.createElement('div');
        sentinel.className = 'jux-lazy-sentinel';
        sentinel.style.height = '1px';
        elem.appendChild(sentinel);

        var observer = new IntersectionObserver(function (entries) {
            if (finished || loading || !entries[0].isIntersecting) { return; }
            loading = true;
            currentPage++;

            _loadPage(elem, apiClient, userId, userSettings, currentPage).then(function () {
                loading = false;

                // The server says whether a later page exists: stop as soon as it does not, so the
                // end of the layout never costs an extra, empty request.
                if (elem.dataset.juxHasMore !== 'true') {
                    finished = true;
                    observer.disconnect();
                    sentinel.remove();
                    return;
                }
                elem.appendChild(sentinel);
            }).catch(function () {
                loading = false;
            });
        }, { rootMargin: '200px' });

        observer.observe(sentinel);
    }

    // Pure lazy-load pagination decision: whether a /Sections response announces a later page.
    function _hasMorePages(response) {
        return !!(response && response.HasMore);
    }

    // -------------------------------------------------------------------------
    // Routing helpers
    // -------------------------------------------------------------------------

    // Maps JUX route keys to Jellyfin hash-based page URLs.
    // appRouter.getRouteUrl cannot be used for library pages (movies, tvshows, music…)
    // because those strings are not registered named routes - the router tries to resolve
    // them as item references, producing /Items/undefined errors.
    var _ROUTE_HREF_MAP = {
        'movies':  '#/movies.html',
        'tvshows': '#/tv.html',
        'nextup':  '#/nextup.html',
        'music':   '#/music.html',
        'livetv':  '#/livetv.html',
        'books':   '#/books.html',
        'photos':  '#/photos.html'
    };

    function _buildSeeAllHref(route) {
        return _ROUTE_HREF_MAP[route] || null;
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    function _escHtml(str) {
        if (!str) { return ''; }
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // -------------------------------------------------------------------------
    // Stale chunk guard (TODO_V4.md 4.2, F-13)
    // -------------------------------------------------------------------------

    // A chunk fetched before JellyUX was installed only carries Last-Modified, so the browser may
    // keep serving that native copy from its heuristic cache; Ctrl+Shift+R does not refetch it
    // because it is loaded dynamically. Only fetch(url, { cache: 'reload' }) replaces it (E8-04).
    // The guard does that once per browser session, then reloads; if the home page is still native
    // after that reload, an intermediate cache (CDN, proxy) serves it and a message says so.
    var CHUNK_RELOAD_KEY = 'jux-chunk-reload';
    var CHUNK_GUARD_INTERVAL_MS = 1000;
    var CHUNK_GUARD_MAX_TICKS = 300;

    // 'ok' once the patched chunks are seen at work, 'stale' when the home page was rendered by a
    // native chunk, null while the home page is not there or not rendered yet.
    function _chunkState(win, doc) {
        var view = doc.getElementById('indexPage');
        if (!view) {
            return null;
        }

        if (win.juxTabInjector && !doc.getElementById('jux-tab-watchlist')) {
            return 'stale';
        }

        if (win.JuxLoadSectionsPatched === true) {
            return 'ok';
        }

        var sections = view.querySelector('.tabContent .sections');
        return sections && sections.children.length > 0 ? 'stale' : null;
    }

    function _readSession(win, key) {
        try {
            return win.sessionStorage.getItem(key);
        } catch (e) {
            return undefined;
        }
    }

    function _cachedChunkUrls(win) {
        var entries = win.performance && win.performance.getEntriesByType
            ? win.performance.getEntriesByType('resource')
            : [];
        return entries
            .filter(function (entry) { return /\.chunk\.js(\?|$)/.test(entry.name) && entry.transferSize === 0; })
            .map(function (entry) { return entry.name; });
    }

    function _showChunkNotice(doc, key, persistent) {
        var fr = (doc.documentElement.lang || '').toLowerCase().indexOf('fr') === 0;
        var texts = {
            reloading: fr
                ? 'JellyUX : mise à jour des fichiers en cache, rechargement de la page.'
                : 'JellyUX: updating cached files, reloading the page.',
            stale: fr
                ? 'JellyUX ne peut pas afficher son accueil : un cache entre ce navigateur et le serveur (CDN ou proxy) sert encore une ancienne copie de la page. L\'administrateur du serveur doit le purger.'
                : 'JellyUX cannot show its home page: a cache between this browser and the server (CDN or proxy) still serves an old copy of the page. The server administrator should purge it.'
        };

        var notice = doc.getElementById('jux-chunk-notice');
        if (!notice) {
            notice = doc.createElement('div');
            notice.id = 'jux-chunk-notice';
            notice.className = 'jux-chunk-notice';
            notice.setAttribute('role', 'status');
            doc.body.appendChild(notice);
        }

        notice.textContent = texts[key];
        if (persistent) {
            var close = doc.createElement('button');
            close.type = 'button';
            close.className = 'jux-chunk-notice-close';
            close.setAttribute('aria-label', fr ? 'Fermer' : 'Close');
            close.textContent = '×';
            close.addEventListener('click', function () { notice.remove(); });
            notice.appendChild(close);
        }
    }

    // Returns what was done: 'reload' (cached chunks refetched, page reloaded) or 'notice' (already
    // reloaded once this session, or no session storage to guard a reload loop with).
    function _repairStaleChunks(win, doc) {
        var flag = _readSession(win, CHUNK_RELOAD_KEY);
        if (flag !== null) {
            _showChunkNotice(doc, 'stale', true);
            return Promise.resolve('notice');
        }

        win.sessionStorage.setItem(CHUNK_RELOAD_KEY, '1');
        _showChunkNotice(doc, 'reloading', false);
        var refetches = _cachedChunkUrls(win).map(function (url) {
            return win.fetch(url, { cache: 'reload' }).catch(function () { return null; });
        });
        return Promise.all(refetches).then(function () {
            win.location.reload();
            return 'reload';
        });
    }

    function _armChunkGuard(win, doc) {
        var ticks = 0;
        var timer = win.setInterval(function () {
            ticks++;
            var state = _chunkState(win, doc);
            if (state === 'ok') {
                try { win.sessionStorage.removeItem(CHUNK_RELOAD_KEY); } catch (e) { /* no storage, nothing to clear */ }
            } else if (state === 'stale') {
                _repairStaleChunks(win, doc);
            }

            if (state !== null || ticks >= CHUNK_GUARD_MAX_TICKS) {
                win.clearInterval(timer);
            }
        }, CHUNK_GUARD_INTERVAL_MS);
        return timer;
    }

    // Guarded UMD-lite export (TODO_V2.md Phase 15.3): `module` is undefined in a browser, so this
    // branch never executes in production -- zero behavior change there. It lets a Node-based test
    // runner (Vitest) `require()`/`import` this file and exercise its pure functions directly,
    // without needing to convert this script to a real ES module (which would work fine here, since
    // it's injected via a plain <script src> tag -- see TransformationPatches.cs -- rather than the
    // AJAX-evaluated fragment mechanism that ruled out ES modules for config.js in Phase 11; this
    // file simply doesn't need that conversion for testing purposes).
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            _escHtml: _escHtml,
            _buildSeeAllHref: _buildSeeAllHref,
            _fallback: _fallback,
            _isHomePage: _isHomePage,
            _hasMorePages: _hasMorePages,
            _setupLazyLoader: _setupLazyLoader,
            _buildSection: _buildSection,
            _makeFetchData: _makeFetchData,
            _chunkState: _chunkState,
            _repairStaleChunks: _repairStaleChunks,
            _armChunkGuard: _armChunkGuard
        };
    } else {
        _armChunkGuard(window, document);
    }
})();
