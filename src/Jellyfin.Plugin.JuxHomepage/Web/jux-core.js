'use strict';

// JellyUX shared core (F-35): the helpers of every client module, loaded first. Its only UI is the
// error state shared by the home sections and the tabs (tab widgets: jux-ui.js).
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

    // One Promise per key for the detail item shown, shared by the detail-page modules; a failure
    // stays cached (no retry on every DOM mutation) until another item is opened.
    var _itemCache = { itemId: null };

    // A failed request is not kept: the next look at the same item asks again (R11, phase 06).
    function cached(itemId, key, request) {
        if (_itemCache.itemId !== itemId) {
            _itemCache = { itemId: itemId };
        }
        if (!_itemCache[key]) {
            var entries = _itemCache;
            var promise = Promise.resolve(request());
            entries[key] = promise;
            promise.catch(function () {
                if (entries[key] === promise) {
                    delete entries[key];
                }
            });
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
            return fetchWithTimeout(window.ApiClient, window.ApiClient.getUrl('JuxHomepage/Collections/IncludedIn/' + itemId));
        });
    }

    // The History tiles and the Statistics tab read the same route: one request serves both for a
    // minute (Annexe.a); a failure is not kept (test: jux-core.test.js "getStatistics").
    var STATISTICS_TTL_MS = 60000;
    var _statistics = null;

    function getStatistics(userId) {
        if (_statistics && _statistics.userId === userId && Date.now() - _statistics.at < STATISTICS_TTL_MS) {
            return _statistics.promise;
        }
        var entry = { userId: userId, at: Date.now() };
        entry.promise = fetchWithTimeout(window.ApiClient, window.ApiClient.getUrl('JuxHomepage/Statistics', { userId: userId }))
            .catch(function (err) {
                if (_statistics === entry) {
                    _statistics = null;
                }
                throw err;
            });
        _statistics = entry;
        return entry.promise;
    }

    // A JSON request that rejects after ms without an answer (FIAB-08, 15 s); a late answer is dropped
    // (test: jux-core.test.js "fetchWithTimeout"). The request leaves at once, as a bare getJSON would, and
    // a client that throws rejects like a failed request.
    var REQUEST_TIMEOUT_MS = 15000;

    function fetchWithTimeout(apiClient, url, ms) {
        var limit = ms || REQUEST_TIMEOUT_MS;
        var request;
        try {
            request = apiClient.getJSON(url);
        } catch (err) {
            request = Promise.reject(err);
        }
        return new Promise(function (resolve, reject) {
            var timer = setTimeout(function () {
                reject(new Error('No answer within ' + limit + ' ms: ' + url));
            }, limit);
            Promise.resolve(request).then(function (value) {
                clearTimeout(timer);
                resolve(value);
            }, function (err) {
                clearTimeout(timer);
                reject(err);
            });
        });
    }

    // Deferred card images (F-10): a JellyUX container gets no native lazy loading, so each
    // ".lazy[data-src]" image is applied near the viewport. One observer per container, replaced on
    // each render.
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

    // Labels (F-64): the "client.*" keys of Localization/<lang>.json, served as window.JuxI18n by
    // jux-i18n.js before this script; a key missing from a language falls back to English.
    function _dictionaries() {
        return window.JuxI18n || {};
    }

    function _has(object, key) {
        return Object.prototype.hasOwnProperty.call(object, key);
    }

    // The page language (or lang) when JellyUX has a dictionary for it, English otherwise.
    function resolveLang(lang) {
        var code = String(lang === undefined ? document.documentElement.lang || 'en' : lang || 'en')
            .toLowerCase().split('-')[0];
        return _has(_dictionaries(), code) ? code : 'en';
    }

    // One label, its {name} placeholders filled from params; the key itself when no language has it.
    function t(key, params, lang) {
        var dictionaries = _dictionaries();
        var own = dictionaries[resolveLang(lang)] || {};
        var english = dictionaries.en || {};
        var text = _has(own, key) ? own[key] : _has(english, key) ? english[key] : key;
        Object.keys(params || {}).forEach(function (name) {
            text = text.split('{' + name + '}').join(String(params[name]));
        });
        return text;
    }

    // Every label of a script, "client.<scope>.<name>" read as labels(scope).name. Built once per scope
    // and language while the same dictionaries are served (Annexe.a; test: jux-core.test.js "labels").
    var _labelCache = {};

    function labels(scope, lang) {
        var prefix = 'client.' + scope + '.';
        var dictionaries = _dictionaries();
        var code = resolveLang(lang);
        var hit = _labelCache[scope + '|' + code];
        if (hit && hit.en === dictionaries.en && hit.own === dictionaries[code]) {
            return hit.result;
        }
        var result = {};
        _labelCache[scope + '|' + code] = { en: dictionaries.en, own: dictionaries[code], result: result };
        [dictionaries.en || {}, dictionaries[code] || {}].forEach(function (dictionary) {
            Object.keys(dictionary).forEach(function (key) {
                if (key.indexOf(prefix) === 0) {
                    result[key.slice(prefix.length)] = dictionary[key];
                }
            });
        });
        return result;
    }

    // Error state (F-40), so an outage never reads as "nothing here", with Jellyfin's raised button.
    // options: { name } for a full pane, { compact: true } for one line in a home section. Missing
    // labels leave the text empty, never throw (Annexe.b).
    function errorHtml(options) {
        var text = labels('error');
        var compact = !!(options && options.compact);
        var title = compact ? text.section : String(text.title || '').replace('{name}', (options && options.name) || '');
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

    // "Turn JellyUX off for me" (F-36), read from answers the client already gets (Watchlist/Ids,
    // Sections) and kept per user for the session: '0' on, '1' off, nothing while unknown (in memory
    // when storage is blocked). Detail-page modules wait for it (H-09): for a request a module sent,
    // else one UserSettings read (tests: jux-core.test.js "on a detail page opened directly").
    var USER_OFF_KEY = 'jux-user-off:';
    var _blockedStorageStates = {};
    var _userStateRequests = {};
    var _userStateListeners = [];
    var _userStateProviders = [];
    // Users whose state could not be read in this tab: they count as on, and no request is sent again
    // until another answer arrives (test: jux-core.test.js "once per tab when it fails").
    var _userStateUnavailable = {};

    function _currentUserId() {
        return window.ApiClient && typeof window.ApiClient.getCurrentUserId === 'function'
            ? window.ApiClient.getCurrentUserId()
            : null;
    }

    function _userState(userId) {
        try {
            return window.sessionStorage.getItem(USER_OFF_KEY + userId);
        } catch (e) {
            return _blockedStorageStates[userId] || null;
        }
    }

    function isOffForUser() {
        var userId = _currentUserId();
        return !!userId && _userState(userId) === '1';
    }

    function setUserEnabled(userId, enabled) {
        if (!userId) { return; }
        var value = enabled ? '0' : '1';
        var changed = _userState(userId) !== value;
        delete _userStateUnavailable[userId];
        try {
            window.sessionStorage.setItem(USER_OFF_KEY + userId, value);
        } catch (e) {
            _blockedStorageStates[userId] = value;
        }
        if (changed) {
            _userStateListeners.slice().forEach(function (listener) { listener(); });
        }
    }

    // A module whose own request also answers the state (the card hooks, Watchlist/Ids) registers a
    // provider: whenUserStateKnown runs it before reading UserSettings, and the provider reports the
    // request it sends, so the state costs no request of its own when the card hooks are loaded.
    function provideUserState(provider) {
        _userStateProviders.push(provider);
    }

    // A module that already asked the server for the state reports its request, so
    // whenUserStateKnown waits for it instead of asking again.
    function reportUserStateRequest(userId, request) {
        if (!userId) { return; }
        _trackUserStateRequest(userId, Promise.resolve(request).catch(function () {}));
    }

    // Keeps the request in flight for a user until it settles, so a state unknown again later (a
    // sign-out, cleared storage) asks the providers again.
    function _trackUserStateRequest(userId, request) {
        _userStateRequests[userId] = request;
        request.then(function () {
            if (_userStateRequests[userId] === request) {
                delete _userStateRequests[userId];
            }
        });
        return request;
    }

    // Resolves to whether JellyUX runs for the signed-in user once the server said so. A failed read
    // resolves to true, as before the state was waited for.
    function whenUserStateKnown() {
        var userId = _currentUserId();
        if (!userId) { return Promise.resolve(true); }
        if (_userState(userId)) { return Promise.resolve(_userState(userId) !== '1'); }
        if (_userStateUnavailable[userId]) { return Promise.resolve(true); }

        if (!_userStateRequests[userId]) {
            _userStateProviders.forEach(function (provider) {
                try {
                    provider(userId);
                } catch (err) {
                    console.error('[JellyUX] User state provider failed:', err);
                }
            });
        }
        var request = _trackUserStateRequest(userId, (_userStateRequests[userId] || Promise.resolve()).then(function () {
            if (_userState(userId)) { return undefined; }
            return fetchWithTimeout(window.ApiClient,
                window.ApiClient.getUrl('JuxHomepage/UserSettings', { userId: userId })
            ).then(function (settings) {
                setUserEnabled(userId, !(settings && settings.Enabled === false));
            });
        }).catch(function (err) {
            console.error('[JellyUX] Failed to read the JellyUX settings:', err);
        }).then(function () {
            if (!_userState(userId)) {
                _userStateUnavailable[userId] = true;
            }
        }));
        return request.then(function () {
            return _userState(userId) !== '1';
        });
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

    // In-flight guard of the detail-page renders, run many times while the DOM settles: start() runs
    // only when no call for the same id is in flight (el.dataset[key] = id, cleared when its promise
    // settles unless a newer id took it). Returns that promise, or null (test: jux-collections.test.js
    // "in-flight guard").
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

    // Runs onChange on every DOM mutation while a detail page is shown and JellyUX is on for the user,
    // subscribed only then. Each run waits for a user, then for the user state (H-09). onLeave runs on
    // other pages and when the user is off. Returns sync, run again 400 ms after each hash change.
    function watchDetailPages(onChange, onLeave) {
        var unsubscribe = null;
        // One wait for the user state at a time: the mutations that arrive meanwhile are covered by
        // the single run that follows the answer (test: jux-core.test.js "runs a module once").
        var waiting = false;

        function leave() {
            if (unsubscribe) {
                unsubscribe();
                unsubscribe = null;
            }
            if (onLeave) {
                onLeave();
            }
        }

        function run() {
            var userId = _currentUserId();
            if (!userId) {
                return;
            }
            var state = _userState(userId);
            if (state === '1') {
                leave();
            } else if (state === '0') {
                onChange();
            } else if (!waiting) {
                waiting = true;
                whenUserStateKnown().then(function (enabled) {
                    waiting = false;
                    if (!enabled) {
                        leave();
                    } else if (unsubscribe) {
                        onChange();
                    }
                });
            }
        }

        function sync() {
            if (!currentDetailItemId() || isOffForUser()) {
                leave();
                return;
            }
            if (!unsubscribe) {
                unsubscribe = onDomMutation(run);
            }
            run();
        }

        window.addEventListener('hashchange', function () {
            setTimeout(sync, 400);
        });
        // An answer that turns JellyUX off for the user disarms the modules at once.
        _userStateListeners.push(function () {
            if (isOffForUser()) {
                leave();
            }
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
        reportUserStateRequest: reportUserStateRequest,
        provideUserState: provideUserState,
        whenUserStateKnown: whenUserStateKnown,
        escHtml: escHtml,
        cssUrl: cssUrl,
        iconName: iconName,
        cached: cached,
        getItem: getItem,
        getIncludedIn: getIncludedIn,
        getStatistics: getStatistics,
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
