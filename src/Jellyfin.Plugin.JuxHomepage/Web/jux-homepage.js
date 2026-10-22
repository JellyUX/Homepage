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

            // Graceful disable on TV layout only (D-02 a). layoutManager sets "layout-tv" on the html
            // element, not on body (jellyfin-web 10.11.10: documentElement.classList.add("layout-"+t)).
            // Windows/Android native apps also expose window.NativeShell, but they run the exact same
            // jellyfin-web bundle in an embedded WebView, loaded live from the connected server -- same
            // DOM, same loadSections chunk, same FileTransformation patches -- so there is no technical
            // reason to bail out there.
            if (document.documentElement.classList.contains('layout-tv')) {
                return _fallback(self, elem, apiClient, user, userSettings);
            }

            // Turned off for this user (TODO_V4.md 8.3): Jellyfin renders the home screen, without a
            // Sections request.
            if (!_isHomePage() || window.JuxCore.isOffForUser()) {
                return _fallback(self, elem, apiClient, user, userSettings);
            }

            return _renderHome(self, elem, apiClient, user, userSettings);
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
    // Skeleton (TODO_V4.md 5.3, F-41)
    // -------------------------------------------------------------------------

    // Grey placeholders shown before the first request, in place of the native spinner, until the
    // first page of sections arrives. Built from Jellyfin's own card classes, so each placeholder has
    // the size of a real backdrop card at every screen width (measured on 8096: 359 px at 1920 px).
    var SKELETON_SECTIONS = 3;
    var SKELETON_CARDS = 8;

    function _skeletonCardsHtml() {
        return new Array(SKELETON_CARDS + 1).join(
            '<div class="card overflowBackdropCard jux-skeleton-card">' +
            '<div class="cardBox cardBox-bottompadded">' +
            '<div class="cardScalable">' +
            '<div class="cardPadder cardPadder-overflowBackdrop"></div>' +
            '<div class="cardImageContainer jux-skeleton-block"></div>' +
            '</div>' +
            '<div class="cardText jux-skeleton-text"><div class="jux-skeleton-bar"></div></div>' +
            '</div>' +
            '</div>');
    }

    function _skeletonHtml() {
        var section =
            '<div class="verticalSection jux-skeleton-section">' +
            '<div class="sectionTitleContainer sectionTitleContainer-cards padded-left">' +
            '<div class="jux-skeleton-bar jux-skeleton-title"></div>' +
            '</div>' +
            '<div class="jux-skeleton-row padded-left">' + _skeletonCardsHtml() + '</div>' +
            '</div>';
        return '<div class="jux-skeleton" aria-busy="true">' + new Array(SKELETON_SECTIONS + 1).join(section) + '</div>';
    }

    function _clearSkeleton(elem) {
        var skeleton = elem.querySelector('.jux-skeleton');
        if (skeleton) {
            skeleton.remove();
        }
    }

    // -------------------------------------------------------------------------
    // Home rendering
    // -------------------------------------------------------------------------

    function _renderHome(self, elem, apiClient, user, userSettings) {
        var userId = apiClient.getCurrentUserId();

        // Jellyfin calls loadSections once per mount, on a new container or on one it has just
        // emptied (destroySections), so every call starts from a blank container.
        elem.innerHTML = _skeletonHtml();
        elem.classList.add('homeSectionsContainer', 'jux-homepage-active');
        var rowObserver = _rowObservers && _rowObservers.get(elem);
        if (rowObserver) {
            rowObserver.disconnect();
            _rowObservers.delete(elem);
        }

        // Only the first page load falls back to native rendering on failure: at that point
        // nothing has been rendered yet, so a permanently blank home screen would otherwise result.
        // Later lazy-loaded pages (see _setupLazyLoader) intentionally do NOT fall back -- by then
        // JUX has already rendered a working layout, and swapping to native mid-page would break it.
        return _loadPage(elem, apiClient, userId, userSettings, 0, function () {
            elem.classList.remove('jux-homepage-active', 'homeSectionsContainer');
            return _fallback(self, elem, apiClient, user, userSettings);
        }).then(function (result) {
            // TODO_V3.md Phase 7.3 fix: the lazy loader used to be armed above, before this first page
            // had actually rendered anything -- on a short layout its sentinel sat in a still-empty
            // container and intersected almost immediately, firing page=1 in a near-simultaneous race
            // with page=0. Arming it only here, after page 0 has resolved, removes that race; a short
            // (but non-empty) first page still correctly triggering an immediate page=1 fill afterward
            // is the intended lazy-fill-viewport behavior, not a bug. The class check guards against
            // arming it when page 0 failed and fell back to native rendering above (that failure path
            // strips the "jux-homepage-active" class).
            // Armed only when page 0 reported a later page: a layout that fits in one page never
            // requests an empty one.
            if (elem.classList.contains('jux-homepage-active') && elem.dataset.juxHasMore === 'true') {
                _setupLazyLoader(elem, apiClient, userId, userSettings);
            }
            return result;
        });
    }

    // A page that has not answered within 15 s fails like any other (FIAB-08): page 0 then hands the
    // home screen back to Jellyfin instead of leaving the placeholders up for good.
    function _loadPage(elem, apiClient, userId, userSettings, page, onFirstPageFailure) {
        return window.JuxCore.fetchWithTimeout(apiClient,
            apiClient.getUrl('JuxHomepage/Sections', {
                userId: userId,
                page: page,
                lang: document.documentElement.lang || 'en'
            })
        ).then(function (response) {
            _clearSkeleton(elem);
            // The plugin was disabled after this page loaded: the layout comes back empty with
            // Enabled = false, and the first page hands the home screen back to Jellyfin.
            if (response && response.Enabled === false) {
                window.JuxCore.setUserEnabled(userId, false);
                elem.dataset.juxHasMore = 'false';
                return typeof onFirstPageFailure === 'function' ? onFirstPageFailure() : undefined;
            }
            window.JuxCore.setUserEnabled(userId, true);
            elem.dataset.juxHasMore = String(_hasMorePages(response));
            var descriptors = (response && response.Sections) || [];
            if (descriptors.length === 0) {
                // A first page with no section (no library, every section turned off) would leave the
                // home screen blank: Jellyfin renders it instead (Annexe.b, DEC-P04-1).
                return typeof onFirstPageFailure === 'function' ? onFirstPageFailure() : undefined;
            }

            // A descriptor that throws loses its own section only (FIAB-08), never the whole page.
            var tasks = descriptors.map(function (descriptor) {
                try {
                    return _buildSection(elem, apiClient, userId, userSettings, descriptor);
                } catch (err) {
                    console.error('[JellyUX] Section build failed (' + (descriptor && descriptor.WidgetType) + '):', err);
                    return null;
                }
            });

            return Promise.all(tasks).then(function () {
                var containers = elem.querySelectorAll('.jux-items-container:not([data-jux-loaded])');
                Array.prototype.forEach.call(containers, function (c) {
                    c.setAttribute('data-jux-loaded', '1');
                    if (typeof c.resume === 'function') {
                        _resumeWhenNear(elem, c);
                    }
                });
            });
        }).catch(function (err) {
            console.error('[JellyUX] Sections fetch failed (page ' + page + '):', err);
            _clearSkeleton(elem);
            if (typeof onFirstPageFailure === 'function') {
                return onFirstPageFailure();
            }
            // A later page reports its failure, so the lazy loader asks for it again (FIAB-08).
            throw err;
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
        // only the Seasonal widget's presets set one). No style reads it today (TODO_V4.md 7.4): it
        // stays as a stable anchor for a future per-theme look, scoped to this one section only.
        if (descriptor.Theme) {
            sectionEl.setAttribute('data-jux-theme', descriptor.Theme);
        }

        // Title block
        var titleContainer = document.createElement('div');
        titleContainer.className = 'sectionTitleContainer sectionTitleContainer-cards padded-left';

        var seeAll = descriptor.Route && api && !api.layoutManager.tv
            ? _seeAllTarget(apiClient, userId, descriptor)
            : null;
        var titleHtml = '<h2 class="sectionTitle sectionTitle-cards jux-section-title">' +
            window.JuxCore.escHtml(descriptor.DisplayName) + '</h2>';
        if (seeAll) {
            titleContainer.innerHTML =
                '<a is="emby-linkbutton" href="' + window.JuxCore.escHtml(seeAll.href || '#') + '" ' +
                'class="button-flat button-flat-mini sectionTitleTextButton">' + titleHtml +
                '<span class="material-icons chevron_right" aria-hidden="true"></span>' +
                '</a>';
            if (seeAll.resolve) {
                titleContainer.firstChild.addEventListener('click', function (e) {
                    e.preventDefault();
                    seeAll.resolve().then(function (href) {
                        if (href) {
                            window.location.hash = href;
                        }
                    }).catch(function (err) {
                        console.error('[JellyUX] See all of ' + descriptor.WidgetType + ' failed:', err);
                    });
                });
            }
        } else {
            titleContainer.innerHTML = titleHtml;
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

        itemsContainer.fetchData = _guardFetchData(_makeFetchData(apiClient, userId, descriptor), descriptor);
        itemsContainer.getItemsHtml = _guardGetItemsHtml(_makeGetItemsHtml(descriptor, shapeFn, useEpisodeImages));
        itemsContainer.parentContainer = sectionEl;
        itemsContainer.addEventListener('click', _onSectionRetry);

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
    // Recently Added reads Items/Latest, the native "Latest" route, so the user's home preferences
    // apply as on the native home: libraries excluded from latest media, and played items hidden
    // (TODO_V4.md 8.3, F-36). Shows are episodes grouped by series, as the native Latest of a shows
    // library: Latest filtered on Series took over 150 s on a 200-series library (measured on 8096).
    // Latest counts its limit in episodes, so the shows read more episodes than cards, and Recently Added
    // Episodes caps the cards of one series (TODO_V4.md 9.3, F-65): both values come from the section's
    // descriptor (LimitFactor, MaxPerSeries), the ones its server widget probes with (PERF-03.c).
    var NATIVE_ROUTES = {
        'jux.native.continue-watching': { path: 'UserItems/Resume', params: { mediaTypes: 'Video' } },
        'jux.native.next-up': { path: 'Shows/NextUp', params: {} },
        'jux.native.recently-added-movies': { path: 'Items/Latest', params: { includeItemTypes: 'Movie' } },
        'jux.native.recently-added-shows': { path: 'Items/Latest', params: { includeItemTypes: 'Episode' } },
        'jux.native.recently-added-episodes': { path: 'Items/Latest', params: { includeItemTypes: 'Episode', groupItems: false } }
    };

    // Keeps at most perSeries items of each series, in order; every item when perSeries is unset.
    function _capPerSeries(items, perSeries) {
        if (!perSeries) {
            return items;
        }
        var seen = {};
        return items.filter(function (item) {
            if (!item.SeriesId) {
                return true;
            }
            seen[item.SeriesId] = (seen[item.SeriesId] || 0) + 1;
            return seen[item.SeriesId] <= perSeries;
        });
    }

    function _makeFetchData(apiClient, userId, descriptor) {
        var nativeRoute = NATIVE_ROUTES[descriptor.WidgetType];
        if (nativeRoute) {
            return function () {
                var maxItems = descriptor.MaxItems || 20;
                var params = {
                    userId: userId,
                    startIndex: 0,
                    limit: maxItems * (descriptor.LimitFactor || 1),
                    fields: 'PrimaryImageAspectRatio,DateCreated',
                    imageTypeLimit: 1,
                    enableImageTypes: 'Primary,Thumb,Backdrop',
                    enableTotalRecordCount: false
                };
                Object.keys(nativeRoute.params).forEach(function (key) {
                    params[key] = nativeRoute.params[key];
                });
                // Items/Latest answers a bare array; the container reads { Items }.
                return apiClient.getJSON(apiClient.getUrl(nativeRoute.path, params)).then(function (result) {
                    return Array.isArray(result)
                        ? { Items: _capPerSeries(result, descriptor.MaxPerSeries).slice(0, maxItems) }
                        : result;
                });
            };
        }

        return function () {
            return apiClient.getJSON(
                apiClient.getUrl('JuxHomepage/Section/' + descriptor.WidgetType, {
                    userId: userId,
                    additionalData: descriptor.AdditionalData || undefined,
                    sectionKey: descriptor.SectionKey || undefined,
                    startIndex: 0,
                    limit: descriptor.MaxItems || 20
                })
            );
        };
    }

    // A failed section request (TODO_V4.md 5.2, F-40). The native itemscontainer hides its section
    // when it receives no item and never catches a rejected fetchData (one unhandledrejection per
    // section): the failure resolves to a single marker row instead, so the section stays visible
    // and renders the error, with a retry button, in place of its cards.
    var SECTION_ERROR = { juxSectionError: true };

    // Leaving the home page runs Jellyfin's destroySections, which nulls fetchData and getItemsHtml on
    // every container it removes; a section still loading then answered into a dead container, whose
    // native render threw "this.getItemsHtml is not a function" (A-02, jellyfin-web 10.11.10). Its
    // answer is dropped instead: the promise never settles (test: jux-homepage.test.js "destroyed").
    function _guardFetchData(fetchData, descriptor) {
        return function () {
            var container = this;
            var destroyed = function () {
                return !!container && typeof container.getItemsHtml !== 'function';
            };
            return fetchData().then(function (result) {
                return destroyed() ? new Promise(function () {}) : result;
            }, function (err) {
                if (destroyed()) {
                    return new Promise(function () {});
                }
                console.error('[JellyUX] Section fetch failed (' + descriptor.WidgetType + '):', err);
                return { Items: [SECTION_ERROR] };
            });
        };
    }

    function _guardGetItemsHtml(getItemsHtml) {
        return function (items) {
            if (items.length === 1 && items[0] === SECTION_ERROR) {
                return window.JuxCore.errorHtml({ compact: true });
            }
            return getItemsHtml(items);
        };
    }

    function _onSectionRetry(event) {
        var button = event.target.closest('.jux-error-retry');
        if (!button) {
            return;
        }
        button.disabled = true;
        _resumeContainer(this);
    }

    function _resumeContainer(container) {
        // A container already removed from the page (the user left the home page) is not refreshed.
        if (!container.isConnected) {
            return;
        }
        Promise.resolve(container.resume({ refresh: true })).catch(function (err) {
            console.error('[JellyUX] Section render failed:', err);
        });
    }

    // A row is fetched once it comes within one screen below the viewport (PERF-09.a), not with
    // every other row of the page. Until then grey cards hold it at a row's height, so the rows
    // further down stay out of reach; the native render replaces them (test: jux-homepage.test.js
    // "sections loaded near the viewport"). The TV layout never gets here: it keeps the native home.
    var _rowObservers = typeof WeakMap === 'function' ? new WeakMap() : null;

    function _resumeWhenNear(elem, container) {
        if (typeof IntersectionObserver === 'undefined' || !_rowObservers) {
            _resumeContainer(container);
            return;
        }
        var observer = _rowObservers.get(elem);
        if (!observer) {
            observer = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        observer.unobserve(entry.target);
                        _resumeContainer(entry.target);
                    }
                });
            }, { rootMargin: '0px 0px 100% 0px' });
            _rowObservers.set(elem, observer);
        }
        container.innerHTML = _skeletonCardsHtml();
        observer.observe(container);
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

    // The loader of each home container: Jellyfin reuses a container it emptied for the next render,
    // and the loader of the previous render is disconnected then (Annexe.a; test: jux-homepage.test.js
    // "disconnects the previous loader").
    var _lazyLoaders = typeof WeakMap === 'function' ? new WeakMap() : null;

    function _setupLazyLoader(elem, apiClient, userId, userSettings) {
        if (typeof IntersectionObserver === 'undefined') {
            return;
        }

        var previous = _lazyLoaders && _lazyLoaders.get(elem);
        if (previous) {
            previous.observer.disconnect();
            previous.sentinel.remove();
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
            var nextPage = currentPage + 1;

            // The page counter moves on success only: a failed page is asked for again on the next
            // intersection instead of being skipped (FIAB-08).
            _loadPage(elem, apiClient, userId, userSettings, nextPage).then(function () {
                currentPage = nextPage;
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
        if (_lazyLoaders) {
            _lazyLoaders.set(elem, { observer: observer, sentinel: sentinel });
        }
    }

    // Pure lazy-load pagination decision: whether a /Sections response announces a later page.
    function _hasMorePages(response) {
        return !!(response && response.HasMore);
    }

    // -------------------------------------------------------------------------
    // Routing helpers
    // -------------------------------------------------------------------------

    // The "see all" of a section (TODO_V4.md 9.3, F-56), from its server Route (WidgetRoutes.cs) and
    // its AdditionalData. Every target was checked live on Jellyfin 10.11 (8097): the native list and
    // detail pages take ids, never names (a genre name in genreId opens an empty list), #/nextup.html
    // does not exist, and #/list ignores a year. A target the descriptor fully describes is a plain
    // link ({ href }); one that needs an id, the user's library or a JellyUX tab is resolved when the
    // link is clicked ({ resolve }), so the home screen itself sends no request for it.
    function _seeAllTarget(apiClient, userId, descriptor) {
        var value = descriptor.AdditionalData;
        var server = '&serverId=' + encodeURIComponent(apiClient.serverId());

        function byName(path, page, param) {
            return {
                resolve: function () {
                    return apiClient.getJSON(apiClient.getUrl(path + '/' + encodeURIComponent(value), { userId: userId }))
                        .then(function (item) {
                            return item && item.Id ? '#/' + page + '?' + param + '=' + encodeURIComponent(item.Id) + server : null;
                        });
                }
            };
        }

        // The native latest movies and shows links open the library on its Suggestions tab.
        function library(collectionType, page) {
            return {
                resolve: function () {
                    return apiClient.getJSON(apiClient.getUrl('UserViews', { userId: userId })).then(function (views) {
                        var view = ((views && views.Items) || []).filter(function (v) {
                            return v.CollectionType === collectionType;
                        })[0];
                        return view
                            ? '#/' + page + '?topParentId=' + encodeURIComponent(view.Id) + '&collectionType=' + collectionType + '&tab=1'
                            : null;
                    });
                }
            };
        }

        switch (descriptor.Route) {
            case 'nextup':
                return { href: '#/list?type=nextup' + server };
            case 'movies':
                return library('movies', 'movies');
            case 'tvshows':
                return library('tvshows', 'tv');
            case 'watchlist':
                return {
                    resolve: function () {
                        var tab = document.getElementById('jux-tabbtn-watchlist');
                        if (tab) {
                            tab.click();
                        }
                        return Promise.resolve(null);
                    }
                };
            default:
                break;
        }

        if (!value) {
            return null;
        }

        switch (descriptor.Route) {
            case 'tag':
                return { href: '#/list?tag=' + encodeURIComponent(value) + server };
            case 'item':
                return { href: '#/details?id=' + encodeURIComponent(value) + server };
            case 'genre':
                return byName('Genres', 'list', 'genreId');
            case 'studio':
                return byName('Studios', 'list', 'studioId');
            case 'person':
                return byName('Persons', 'details', 'id');
            default:
                return null;
        }
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Stale chunk guard (TODO_V4.md 4.2, F-13)
    // -------------------------------------------------------------------------

    // A chunk fetched before JellyUX was installed only carries Last-Modified, so the browser may
    // keep serving that native copy from its heuristic cache; Ctrl+Shift+R does not refetch it
    // because it is loaded dynamically. Only fetch(url, { cache: 'reload' }) replaces it (E8-04).
    // The guard does that once per browser session, then reloads; if the home page is still native
    // after that reload, an intermediate cache (CDN, proxy) serves it and a message says so. A native
    // home page is also what a Jellyfin Web update the server cannot patch gives: the guard asks the
    // server first and reloads only for a chunk it patches (FIAB-04; tests: jux-homepage.test.js
    // "asks the server before reloading", "neither reloads nor blames a cache"). It polls
    // only while the home page is shown, at most CHUNK_GUARD_MAX_TICKS seconds there; on another page
    // it pauses until the next view is shown (Annexe.a: it used to poll 300 s on any page; test:
    // jux-homepage.test.js "does not poll while another page is shown").
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
        var lang = doc.documentElement.lang;

        var notice = doc.getElementById('jux-chunk-notice');
        if (!notice) {
            notice = doc.createElement('div');
            notice.id = 'jux-chunk-notice';
            notice.className = 'jux-chunk-notice';
            notice.setAttribute('role', 'status');
            doc.body.appendChild(notice);
        }

        notice.textContent = window.JuxCore.t('client.chunk.' + key, null, lang);
        if (persistent) {
            var close = doc.createElement('button');
            close.type = 'button';
            close.className = 'jux-chunk-notice-close';
            close.setAttribute('aria-label', window.JuxCore.t('client.chunk.close', null, lang));
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

        // Without the flag nothing would stop a reload loop: the message is shown instead (Annexe.a).
        try {
            win.sessionStorage.setItem(CHUNK_RELOAD_KEY, '1');
        } catch (e) {
            _showChunkNotice(doc, 'stale', true);
            return Promise.resolve('notice');
        }
        _showChunkNotice(doc, 'reloading', false);
        var refetches = _cachedChunkUrls(win).map(function (url) {
            return win.fetch(url, { cache: 'reload' }).catch(function () { return null; });
        });
        return Promise.all(refetches).then(function () {
            win.location.reload();
            return 'reload';
        });
    }

    // The server's patch state, or null when it does not answer (no reload then, FIAB-04.b).
    function _serverPatchStatus(win) {
        var api = win.ApiClient;
        if (!api) {
            return Promise.resolve(null);
        }
        return window.JuxCore.fetchWithTimeout(api, api.getUrl('JuxHomepage/PatchStatus')).catch(function (err) {
            console.error('[JellyUX] Patch state unavailable, the page is not reloaded:', err);
            return null;
        });
    }

    // Returns what was done: 'drift' (the server did not patch what is missing, message shown),
    // 'unknown' (no answer, nothing done) or the outcome of _repairStaleChunks.
    function _handleStaleChunks(win, doc) {
        var tabsMissing = !!win.juxTabInjector && !doc.getElementById('jux-tab-watchlist');
        return _serverPatchStatus(win).then(function (status) {
            // Unknown: the server could not read the chunk, which says nothing of a Jellyfin Web change.
            var unchecked = tabsMissing ? status && status.TabsAnchor == null : status && status.LoadSections === 'Unknown';
            if (!status || unchecked) {
                if (unchecked) { console.error('[JellyUX] The server could not check the home chunk, the page is not reloaded.'); }
                return 'unknown';
            }
            var patched = tabsMissing ? status.TabsAnchor === true : status.LoadSections === 'Patched';
            if (!patched) {
                _showChunkNotice(doc, 'drift', true);
                return 'drift';
            }
            return _repairStaleChunks(win, doc);
        });
    }

    function _armChunkGuard(win, doc) {
        var ticks = 0;
        var timer = null;
        var done = false;

        function pause() {
            if (timer !== null) {
                win.clearInterval(timer);
                timer = null;
            }
        }

        function tick() {
            var state = _chunkState(win, doc);
            if (state === 'ok') {
                try { win.sessionStorage.removeItem(CHUNK_RELOAD_KEY); } catch (e) { /* no storage, nothing to clear */ }
            } else if (state === 'stale') {
                _handleStaleChunks(win, doc);
            }

            if (state !== null) {
                done = true;
                pause();
                return;
            }
            var home = doc.getElementById('indexPage');
            if (!home || home.classList.contains('hide')) {
                pause();
                return;
            }
            ticks++;
            if (ticks >= CHUNK_GUARD_MAX_TICKS) {
                done = true;
                pause();
            }
        }

        function start() {
            if (!done && timer === null) {
                timer = win.setInterval(tick, CHUNK_GUARD_INTERVAL_MS);
            }
        }

        doc.addEventListener('viewshow', start);
        start();
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
            _seeAllTarget: _seeAllTarget,
            _fallback: _fallback,
            _isHomePage: _isHomePage,
            _hasMorePages: _hasMorePages,
            _setupLazyLoader: _setupLazyLoader,
            _buildSection: _buildSection,
            _renderHome: _renderHome,
            _skeletonHtml: _skeletonHtml,
            _makeFetchData: _makeFetchData,
            _guardFetchData: _guardFetchData,
            _guardGetItemsHtml: _guardGetItemsHtml,
            _chunkState: _chunkState,
            _repairStaleChunks: _repairStaleChunks,
            _armChunkGuard: _armChunkGuard
        };
    } else {
        _armChunkGuard(window, document);
    }
})();
