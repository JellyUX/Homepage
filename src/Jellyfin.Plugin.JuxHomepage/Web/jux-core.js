'use strict';

// JellyUX shared core (TODO_V4.md 5.1, F-35): the helpers every client module needs, loaded first
// whenever the plugin is enabled, so a defect fixed here is fixed in every module. Its only UI is
// the error state the home sections share with the tabs; the tab widgets live in jux-ui.js, which
// builds on this file.
(function () {
    if (typeof window.JuxCore !== 'undefined') {
        return;
    }

    // Escapes a value for an HTML text node or a quoted attribute, apostrophe included, so a value
    // is safe in both '...' and "..." attributes.
    function escHtml(value) {
        if (value === null || value === undefined) { return ''; }
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // A CSS url() value for an image address. encodeURI covers spaces, quotes and backslashes but
    // keeps existing %xx escapes; the characters encodeURI leaves alone and that would end the
    // url( ) token or its quotes are encoded by hand.
    function cssUrl(src) {
        var encoded = encodeURI(String(src || ''))
            .replace(/%25([0-9A-Fa-f]{2})/g, '%$1')
            .replace(/\(/g, '%28')
            .replace(/\)/g, '%29')
            .replace(/'/g, '%27');
        return 'url("' + encoded + '")';
    }

    // A Material icon name inserted in a class attribute: ligature names only, anything else falls
    // back so a caller can never add arbitrary classes.
    function iconName(name, fallback) {
        return /^[a-z0-9_]+$/.test(String(name || '')) ? String(name) : (fallback || 'info');
    }

    // Network results of the current detail item, shared by every detail-page module (collections,
    // breadcrumb, flattened seasons): one Promise per key, so the same request is sent at most once
    // per item however many modules and mutation batches ask for it. A failed request stays cached
    // (no retry loop on every DOM mutation) until another item is opened.
    var _itemCache = { itemId: null };

    function cached(itemId, key, request) {
        if (_itemCache.itemId !== itemId) {
            _itemCache = { itemId: itemId };
        }
        if (!_itemCache[key]) {
            _itemCache[key] = request();
        }
        return _itemCache[key];
    }

    function getItem(userId, itemId) {
        return cached(itemId, 'item', function () {
            return window.ApiClient.getItem(userId, itemId);
        });
    }

    // The JellyUX collection references of an item ({ CollectionId, ... } rows).
    function getIncludedIn(itemId) {
        return cached(itemId, 'includedIn', function () {
            return window.ApiClient.getJSON(window.ApiClient.getUrl('JuxHomepage/Collections/IncludedIn/' + itemId));
        });
    }

    // A JSON request that rejects when the server has not answered within ms (FIAB-08, default 15 s):
    // a server that accepts the connection but never answers would otherwise keep the page waiting
    // for good. A late answer is dropped (test: jux-core.test.js "fetchWithTimeout").
    var REQUEST_TIMEOUT_MS = 15000;

    function fetchWithTimeout(apiClient, url, ms) {
        var limit = ms || REQUEST_TIMEOUT_MS;
        return new Promise(function (resolve, reject) {
            var timer = setTimeout(function () {
                reject(new Error('No answer within ' + limit + ' ms: ' + url));
            }, limit);
            Promise.resolve().then(function () {
                return apiClient.getJSON(url);
            }).then(function (value) {
                clearTimeout(timer);
                resolve(value);
            }, function (err) {
                clearTimeout(timer);
                reject(err);
            });
        });
    }

    // Deferred card images (TODO_V4.md 2.3, F-10). cardBuilder.getCardsHtml emits
    // ".cardImageContainer.lazy[data-src]", but a JellyUX container never gets Jellyfin's native lazy
    // loading, so each image is applied when its card comes near the viewport. One observer per
    // container: re-rendering a container drops the observer of its previous, now detached cards.
    var _imageObservers = typeof WeakMap === 'function' ? new WeakMap() : null;

    function _applyCardImage(el) {
        var src = el.getAttribute('data-src');
        if (!src) { return; }
        el.style.backgroundImage = cssUrl(src);
        el.classList.remove('lazy');
        el.classList.add('lazy-image-fadein-fast');
    }

    function observeCardImages(container) {
        if (!container) {
            return;
        }

        var lazyImages = container.querySelectorAll('.cardImageContainer.lazy[data-src]');
        if (typeof IntersectionObserver === 'undefined' || !_imageObservers) {
            Array.prototype.forEach.call(lazyImages, _applyCardImage);
            return;
        }

        // Every still-lazy image of the container is selected again, so cards appended to it (infinite
        // scroll) and cards not reached yet are both observed by the new observer.
        var previous = _imageObservers.get(container);
        if (previous) {
            previous.disconnect();
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    observer.unobserve(entry.target);
                    _applyCardImage(entry.target);
                }
            });
        }, { rootMargin: '300px' });
        _imageObservers.set(container, observer);
        Array.prototype.forEach.call(lazyImages, function (el) {
            observer.observe(el);
        });
    }

    // Labels (TODO_V4.md 9.4, F-64): the client strings live in Localization/<lang>.json under
    // "client.*" keys, served by jux-i18n.js (window.JuxI18n, one dictionary per language), injected
    // before this script. A key missing from the user's language falls back to English.
    function _dictionaries() {
        return window.JuxI18n || {};
    }

    // The language of the labels: the web client's language when JellyUX has a dictionary for it,
    // English otherwise. lang overrides the page language (a document other than this one).
    function resolveLang(lang) {
        var code = String(lang === undefined ? document.documentElement.lang || 'en' : lang || 'en')
            .toLowerCase().split('-')[0];
        return Object.prototype.hasOwnProperty.call(_dictionaries(), code) ? code : 'en';
    }

    // One label, its {name} placeholders filled from params; the key itself when no language has it.
    function t(key, params, lang) {
        var dictionaries = _dictionaries();
        var own = dictionaries[resolveLang(lang)] || {};
        var english = dictionaries.en || {};
        var text = Object.prototype.hasOwnProperty.call(own, key) ? own[key]
            : Object.prototype.hasOwnProperty.call(english, key) ? english[key] : key;
        Object.keys(params || {}).forEach(function (name) {
            text = text.split('{' + name + '}').join(String(params[name]));
        });
        return text;
    }

    // Every label of a script, "client.<scope>.<name>" read as labels(scope).name.
    function labels(scope, lang) {
        var prefix = 'client.' + scope + '.';
        var dictionaries = _dictionaries();
        var result = {};
        [dictionaries.en || {}, dictionaries[resolveLang(lang)] || {}].forEach(function (dictionary) {
            Object.keys(dictionary).forEach(function (key) {
                if (key.indexOf(prefix) === 0) {
                    result[key.slice(prefix.length)] = dictionary[key];
                }
            });
        });
        return result;
    }

    // Error state (TODO_V4.md 5.2, F-40): what a failed request shows instead of an empty state, so
    // an outage never reads as "nothing here". Here rather than in jux-ui.js because the home
    // sections need it with the tabs module off. The retry button reuses Jellyfin's own raised
    // button (emby-button), measured live on 10.11.10.
    // options: { name } for a full-pane error naming what failed, or { compact: true } for one line
    // inside a home section.
    function errorHtml(options) {
        var text = labels('error');
        var compact = !!(options && options.compact);
        var title = compact ? text.section : text.title.replace('{name}', (options && options.name) || '');
        return '<div class="jux-error-state' + (compact ? ' jux-error-state-compact' : '') + '" role="alert">' +
            '<span class="material-icons error jux-error-icon" aria-hidden="true"></span>' +
            '<div class="jux-error-text">' +
            '<div class="jux-error-title">' + escHtml(title) + '</div>' +
            (compact ? '' : '<div class="jux-error-subtitle">' + escHtml(text.subtitle) + '</div>') +
            '</div>' +
            '<button is="emby-button" type="button" class="raised emby-button jux-error-retry"><span>' + escHtml(text.retry) + '</span></button>' +
            '</div>';
    }

    // Renders the full-pane error into a container; the retry button calls onRetry once.
    function showError(container, options) {
        if (!container) {
            return;
        }

        container.innerHTML = errorHtml(options);
        var button = container.querySelector('.jux-error-retry');
        button.addEventListener('click', function () {
            button.disabled = true;
            if (options && typeof options.onRetry === 'function') {
                options.onRetry();
            }
        });
    }

    // "Turn JellyUX off for me" (TODO_V4.md 8.3, F-36). The server reports it in answers the
    // client already reads (Watchlist/Ids on every page, Sections on the home page); the answer is
    // kept for the browser session, per user, and every module reads it before it arms. Until an
    // answer arrives (the first page of a session), the modules arm as usual.
    var USER_OFF_KEY = 'jux-user-off:';

    function _currentUserId() {
        return window.ApiClient && typeof window.ApiClient.getCurrentUserId === 'function'
            ? window.ApiClient.getCurrentUserId()
            : null;
    }

    function isOffForUser() {
        var userId = _currentUserId();
        if (!userId) { return false; }
        try {
            return window.sessionStorage.getItem(USER_OFF_KEY + userId) === '1';
        } catch (e) {
            return false;
        }
    }

    function setUserEnabled(userId, enabled) {
        if (!userId) { return; }
        try {
            if (enabled) {
                window.sessionStorage.removeItem(USER_OFF_KEY + userId);
            } else {
                window.sessionStorage.setItem(USER_OFF_KEY + userId, '1');
            }
        } catch (e) {
            // Storage blocked: the answer is simply read again on the next page.
        }
    }

    // The item id of the detail page shown (#/details?id=...), or null on any other page.
    function currentDetailItemId() {
        var match = /[#&?]id=([a-f0-9-]+)/i.exec(location.hash);
        return match ? match[1] : null;
    }

    // One MutationObserver on <body> shared by every module (QUAL-04): each subscriber receives the
    // mutation records of every batch; the observer is connected while at least one subscriber is
    // left. A subscriber that throws does not stop the others (test: jux-core.test.js "onDomMutation").
    var _domSubscribers = [];
    var _domObserver = null;

    function _dispatchMutations(mutations) {
        _domSubscribers.slice().forEach(function (subscriber) {
            try {
                subscriber(mutations);
            } catch (err) {
                console.error('[JellyUX] DOM mutation handler failed:', err);
            }
        });
    }

    // Subscribes callback(mutations) to the shared observer; returns the function that unsubscribes it.
    function onDomMutation(callback) {
        _domSubscribers.push(callback);
        if (!_domObserver) {
            _domObserver = new MutationObserver(_dispatchMutations);
            _domObserver.observe(document.body, { childList: true, subtree: true });
        }
        return function () {
            var index = _domSubscribers.indexOf(callback);
            if (index !== -1) {
                _domSubscribers.splice(index, 1);
            }
            if (_domSubscribers.length === 0 && _domObserver) {
                _domObserver.disconnect();
                _domObserver = null;
            }
        };
    }

    // In-flight guard of the detail-page renders: the mutation handlers run many times in quick
    // succession while the DOM settles, each well before the previous call's requests resolve. The
    // element is marked with dataset[key] = id synchronously, start() runs only when no call for the
    // same id is in flight, and the mark is cleared when the promise start() returns settles, unless
    // a newer id took it meanwhile. Returns that promise, or null when a call was already in flight
    // (test: jux-collections.test.js "in-flight guard").
    function runInFlight(el, key, id, start) {
        if (el.dataset[key] === id) {
            return null;
        }
        el.dataset[key] = id;

        function release() {
            if (el.dataset[key] === id) {
                delete el.dataset[key];
            }
        }

        var result;
        try {
            result = start();
        } catch (err) {
            release();
            throw err;
        }
        return Promise.resolve(result).finally(release);
    }

    // Runs onChange now and on every DOM mutation while a detail page is shown and JellyUX is on for
    // the user; it subscribes to the shared observer only then, so on every other page a detail-page
    // module costs nothing. onLeave (optional) runs on every other page. Returns the sync function,
    // run again 400 ms after each hash change (the time Jellyfin takes to swap the view).
    function watchDetailPages(onChange, onLeave) {
        var unsubscribe = null;

        function sync() {
            if (!currentDetailItemId() || isOffForUser()) {
                if (unsubscribe) {
                    unsubscribe();
                    unsubscribe = null;
                }
                if (onLeave) {
                    onLeave();
                }
                return;
            }

            if (!unsubscribe) {
                unsubscribe = onDomMutation(onChange);
            }

            onChange();
        }

        window.addEventListener('hashchange', function () {
            setTimeout(sync, 400);
        });
        sync();
        return sync;
    }

    window.JuxCore = {
        currentDetailItemId: currentDetailItemId,
        watchDetailPages: watchDetailPages,
        onDomMutation: onDomMutation,
        runInFlight: runInFlight,
        isOffForUser: isOffForUser,
        setUserEnabled: setUserEnabled,
        escHtml: escHtml,
        cssUrl: cssUrl,
        iconName: iconName,
        cached: cached,
        getItem: getItem,
        getIncludedIn: getIncludedIn,
        fetchWithTimeout: fetchWithTimeout,
        observeCardImages: observeCardImages,
        resolveLang: resolveLang,
        t: t,
        labels: labels,
        errorHtml: errorHtml,
        showError: showError
    };

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = window.JuxCore;
    }
})();
