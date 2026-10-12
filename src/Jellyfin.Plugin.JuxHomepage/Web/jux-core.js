'use strict';

// JellyUX shared core (TODO_V4.md 5.1, F-35): the helpers every client module needs, loaded first
// whenever the plugin is enabled, so a defect fixed here is fixed in every module. It holds no UI:
// the tab widgets live in jux-ui.js, which builds on this file.
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

    function resolveLang() {
        return (document.documentElement.lang || 'en').toLowerCase().indexOf('fr') === 0 ? 'fr' : 'en';
    }

    window.JuxCore = {
        escHtml: escHtml,
        cssUrl: cssUrl,
        iconName: iconName,
        cached: cached,
        getItem: getItem,
        getIncludedIn: getIncludedIn,
        observeCardImages: observeCardImages,
        resolveLang: resolveLang
    };

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = window.JuxCore;
    }
})();
