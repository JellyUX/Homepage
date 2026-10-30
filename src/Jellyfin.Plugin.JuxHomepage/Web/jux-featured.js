'use strict';

// Featured sections (KTW-01): a section whose row is Featured shows as a banner. jux-homepage.js keeps
// the native emby-itemscontainer (fetchData, error row, retry, data-monitor) and asks this script for
// the HTML of its items, then for the banner's behaviour once the container rendered it (afterRefresh).
// Play and More info are native .itemAction buttons: emby-itemscontainer handles them (H-05).

(function () {
    if (typeof window.JuxFeatured !== 'undefined') {
        return;
    }

    // A banner shows at most 8 slides (KTW-01.a) and needs 3: fewer fall back to a row of cards.
    var MAX_SLIDES = 8;
    var MIN_SLIDES = 3;
    var STRIP_SIZE = 4;
    var INTERVAL_MS = 8000;
    var SWIPE_PX = 40;

    // Backdrop widths requested from the server: the slide's real width times the pixel ratio, rounded
    // up to one of these (KTW-01.b), so the browser and the server cache share a few sizes.
    var BACKDROP_WIDTHS = [960, 1280, 1920];
    var LOGO_WIDTH = 600;
    var THUMB_WIDTH = 320;

    function _labels() {
        return window.JuxCore.labels('featured');
    }

    function _esc(value) {
        return window.JuxCore.escHtml(value == null ? '' : String(value));
    }

    function _backdropTag(item) {
        return item && item.BackdropImageTags && item.BackdropImageTags.length ? item.BackdropImageTags[0] : null;
    }

    function slidesOf(items) {
        return (items || []).filter(_backdropTag).slice(0, MAX_SLIDES);
    }

    function backdropWidth(cssWidth, pixelRatio) {
        var needed = cssWidth * (pixelRatio || 1);
        for (var i = 0; i < BACKDROP_WIDTHS.length; i++) {
            if (BACKDROP_WIDTHS[i] >= needed) {
                return BACKDROP_WIDTHS[i];
            }
        }
        return BACKDROP_WIDTHS[BACKDROP_WIDTHS.length - 1];
    }

    function _runtime(ticks, labels) {
        var minutes = Math.round((ticks || 0) / 600000000);
        if (!minutes) {
            return '';
        }
        var hours = Math.floor(minutes / 60);
        return hours
            ? window.JuxCore.t('client.featured.runtimeHours', { h: hours, m: minutes % 60 })
            : window.JuxCore.t('client.featured.runtimeMinutes', { m: minutes });
    }

    function _metaHtml(item, labels) {
        var parts = [];
        if (item.ProductionYear) {
            parts.push('<span>' + _esc(item.ProductionYear) + '</span>');
        }
        var runtime = _runtime(item.RunTimeTicks, labels);
        if (runtime) {
            parts.push('<span>' + _esc(runtime) + '</span>');
        }
        if (item.OfficialRating) {
            parts.push('<span class="jux-featured-certificate">' + _esc(item.OfficialRating) + '</span>');
        }
        if (item.CommunityRating) {
            var value = Number(item.CommunityRating).toFixed(1);
            parts.push('<span class="jux-featured-rating" aria-label="' +
                _esc(window.JuxCore.t('client.featured.rating', { value: value })) + '">' +
                '<span class="material-icons star" aria-hidden="true"></span>' + _esc(value) + '</span>');
        }
        return parts.length ? '<p class="jux-featured-meta">' + parts.join('') + '</p>' : '';
    }

    function _slideHtml(item, index, count, labels) {
        var tagline = item.Taglines && item.Taglines.length ? item.Taglines[0] : '';
        var text = tagline || item.Overview || '';
        var logoTag = item.ImageTags && item.ImageTags.Logo;
        // The title is always text: shown on a phone, where a white logo would vanish on a light theme's
        // page, and read by screen readers next to the logo elsewhere.
        var heading = '<h3 class="jux-featured-heading' + (logoTag ? ' jux-featured-has-logo' : '') + '">' +
            (logoTag ? '<img class="jux-featured-logo" alt="" data-tag="' + _esc(logoTag) + '" decoding="async">' : '') +
            '<span class="jux-featured-title">' + _esc(item.Name) + '</span></h3>';
        var tag = _backdropTag(item);
        var blurhash = item.ImageBlurHashes && item.ImageBlurHashes.Backdrop && item.ImageBlurHashes.Backdrop[tag];
        var userData = item.UserData || {};

        return '<div class="jux-featured-slide' + (index === 0 ? ' jux-featured-active' : '') + '"' +
            ' role="group" aria-roledescription="slide"' +
            ' aria-label="' + _esc(window.JuxCore.t('client.featured.slide', { index: index + 1, count: count })) + '"' +
            (index === 0 ? '' : ' aria-hidden="true" inert') +
            ' data-index="' + index + '"' +
            ' data-id="' + _esc(item.Id) + '"' +
            ' data-serverid="' + _esc(item.ServerId) + '"' +
            ' data-type="' + _esc(item.Type) + '"' +
            ' data-mediatype="' + _esc(item.MediaType) + '"' +
            ' data-isfolder="' + (item.IsFolder ? 'true' : 'false') + '"' +
            ' data-positionticks="' + _esc(userData.PlaybackPositionTicks || 0) + '">' +
            '<div class="jux-featured-media">' +
            (blurhash ? '<canvas class="jux-featured-blur" width="32" height="18" data-blurhash="' + _esc(blurhash) + '" aria-hidden="true"></canvas>' : '') +
            '<img class="jux-featured-backdrop" alt="" data-tag="' + _esc(tag) + '" decoding="async">' +
            '<div class="jux-featured-scrim"></div>' +
            '</div>' +
            '<div class="jux-featured-copy">' +
            heading +
            _metaHtml(item, labels) +
            (text ? '<p class="jux-featured-text">' + _esc(text) + '</p>' : '') +
            '<div class="jux-featured-actions">' +
            '<button type="button" class="itemAction jux-featured-button jux-featured-button-play" data-action="resume">' +
            '<span class="material-icons play_arrow" aria-hidden="true"></span><span>' + _esc(labels.play) + '</span></button>' +
            '<button type="button" class="itemAction jux-featured-button" data-action="link">' +
            '<span class="material-icons info_outline" aria-hidden="true"></span><span>' + _esc(labels.info) + '</span></button>' +
            '</div>' +
            '</div>' +
            '</div>';
    }

    // The icon classes are written out, so the native-token inventory sees each one (R11 constat 5).
    function _iconButton(className, iconHtml, label) {
        return '<button type="button" class="jux-featured-control ' + className + '" aria-label="' + _esc(label) + '">' +
            iconHtml + '</button>';
    }

    // The banner of a featured section, or null when it has fewer than MIN_SLIDES slides.
    function html(items, descriptor) {
        var slides = slidesOf(items);
        if (slides.length < MIN_SLIDES) {
            return null;
        }
        var labels = _labels();
        return '<div class="jux-featured" role="region" aria-roledescription="carousel"' +
            ' aria-label="' + _esc(descriptor && descriptor.DisplayName) + '">' +
            '<div class="jux-featured-stage" aria-live="off">' +
            slides.map(function (item, index) { return _slideHtml(item, index, slides.length, labels); }).join('') +
            '</div>' +
            '<div class="jux-featured-rail">' +
            '<div class="jux-featured-controls">' +
            _iconButton('jux-featured-previous', '<span class="material-icons chevron_left" aria-hidden="true"></span>', labels.previous) +
            _iconButton('jux-featured-toggle', '<span class="material-icons pause" aria-hidden="true"></span>', labels.pause) +
            _iconButton('jux-featured-next', '<span class="material-icons chevron_right" aria-hidden="true"></span>', labels.next) +
            '<span class="jux-featured-counter" aria-hidden="true"></span>' +
            '</div>' +
            '<div class="jux-featured-strip"></div>' +
            '</div>' +
            '</div>';
    }

    // -------------------------------------------------------------------------
    // Blurhash (https://github.com/woltapp/blurhash, MIT): the colour of a slide while its backdrop loads
    // -------------------------------------------------------------------------

    var BASE83 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~';

    function _decode83(text, start, end) {
        var value = 0;
        for (var i = start; i < end; i++) {
            value = value * 83 + BASE83.indexOf(text.charAt(i));
        }
        return value;
    }

    function _toLinear(value) {
        var v = value / 255;
        return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    }

    function _toSrgb(value) {
        var v = Math.max(0, Math.min(1, value));
        return Math.round((v <= 0.0031308 ? v * 12.92 : 1.055 * Math.pow(v, 1 / 2.4) - 0.055) * 255);
    }

    function _signPow(value, exponent) {
        return (value < 0 ? -1 : 1) * Math.pow(Math.abs(value), exponent);
    }

    // RGBA pixels of a blurhash, or null for a malformed one.
    function decodeBlurhash(hash, width, height) {
        if (!hash || hash.length < 6) {
            return null;
        }
        var size = _decode83(hash, 0, 1);
        var nx = (size % 9) + 1;
        var ny = Math.floor(size / 9) + 1;
        if (hash.length !== 4 + 2 * nx * ny) {
            return null;
        }
        var maxValue = (_decode83(hash, 1, 2) + 1) / 166;
        var colors = [];
        for (var c = 0; c < nx * ny; c++) {
            if (c === 0) {
                var dc = _decode83(hash, 2, 6);
                colors.push([_toLinear(dc >> 16), _toLinear((dc >> 8) & 255), _toLinear(dc & 255)]);
            } else {
                var ac = _decode83(hash, 4 + c * 2, 6 + c * 2);
                colors.push([
                    _signPow((Math.floor(ac / 361) - 9) / 9, 2) * maxValue,
                    _signPow((Math.floor(ac / 19) % 19 - 9) / 9, 2) * maxValue,
                    _signPow((ac % 19 - 9) / 9, 2) * maxValue
                ]);
            }
        }
        var pixels = new Uint8ClampedArray(width * height * 4);
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var r = 0, g = 0, b = 0;
                for (var j = 0; j < ny; j++) {
                    for (var i = 0; i < nx; i++) {
                        var basis = Math.cos(Math.PI * x * i / width) * Math.cos(Math.PI * y * j / height);
                        var color = colors[i + j * nx];
                        r += color[0] * basis;
                        g += color[1] * basis;
                        b += color[2] * basis;
                    }
                }
                var p = 4 * (x + y * width);
                pixels[p] = _toSrgb(r);
                pixels[p + 1] = _toSrgb(g);
                pixels[p + 2] = _toSrgb(b);
                pixels[p + 3] = 255;
            }
        }
        return pixels;
    }

    function _paintBlur(canvas) {
        if (!canvas || canvas.dataset.painted) {
            return;
        }
        canvas.dataset.painted = 'true';
        var pixels = decodeBlurhash(canvas.dataset.blurhash, canvas.width, canvas.height);
        var context = pixels && canvas.getContext && canvas.getContext('2d');
        if (context && typeof ImageData === 'function') {
            context.putImageData(new ImageData(pixels, canvas.width, canvas.height), 0, 0);
        }
    }

    // -------------------------------------------------------------------------
    // Behaviour
    // -------------------------------------------------------------------------

    function _reducedMotion() {
        return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    // The phone layout of jux-homepage.css (max-width: 50em).
    function _phoneLayout() {
        return typeof window.matchMedia === 'function' && window.matchMedia('(max-width: 50em)').matches;
    }

    function _image(apiClient, itemId, options) {
        return apiClient.getScaledImageUrl(itemId, options);
    }

    // Loads the backdrop and logo of one slide, once: only the active slide and the next one are
    // loaded (KTW-01.b), the others keep their blurhash until they come next. The logo waits while the
    // phone layout hides it (its title is shown instead), and loads once a slide comes up wider.
    function _loadSlide(state, slide) {
        if (!slide) {
            return;
        }
        var id = slide.dataset.id;
        var logo = slide.querySelector('.jux-featured-logo');
        if (logo && !logo.getAttribute('src') && !_phoneLayout()) {
            // A logo the server cannot send gives its place back to the title, never asked again (FIAB-03).
            logo.addEventListener('error', function () {
                logo.parentNode.classList.remove('jux-featured-has-logo');
                logo.remove();
            }, { once: true });
            logo.src = _image(state.apiClient, id, { type: 'Logo', tag: logo.dataset.tag, maxWidth: LOGO_WIDTH, quality: 90 });
        }
        if (slide.dataset.loaded) {
            return;
        }
        slide.dataset.loaded = 'true';
        _paintBlur(slide.querySelector('.jux-featured-blur'));
        var backdrop = slide.querySelector('.jux-featured-backdrop');
        if (backdrop) {
            backdrop.addEventListener('load', function () {
                backdrop.classList.add('jux-featured-loaded');
            }, { once: true });
            var media = slide.querySelector('.jux-featured-media');
            backdrop.src = _image(state.apiClient, id, {
                type: 'Backdrop',
                index: 0,
                tag: backdrop.dataset.tag,
                maxWidth: backdropWidth((media && media.clientWidth) || state.root.clientWidth, window.devicePixelRatio),
                quality: 80
            });
        }
    }

    function _stripHtml(state) {
        var labels = _labels();
        var html = '';
        for (var offset = 0; offset < Math.min(STRIP_SIZE, state.slides.length); offset++) {
            var index = (state.index + offset) % state.slides.length;
            var slide = state.slides[index];
            var name = slide.querySelector('.jux-featured-title').textContent;
            html += '<button type="button" class="jux-featured-thumb' + (offset === 0 ? ' jux-featured-active' : '') + '"' +
                ' data-index="' + index + '"' + (offset === 0 ? ' aria-current="true"' : '') +
                ' aria-label="' + _esc(window.JuxCore.t('client.featured.goTo', { name: name })) + '">' +
                '<img alt="" decoding="async" loading="lazy" src="' + _esc(_image(state.apiClient, slide.dataset.id, {
                    type: 'Backdrop',
                    index: 0,
                    tag: slide.querySelector('.jux-featured-backdrop').dataset.tag,
                    maxWidth: THUMB_WIDTH,
                    quality: 70
                })) + '"></button>';
        }
        return html;
    }

    function _render(state) {
        var count = state.slides.length;
        state.slides.forEach(function (slide, index) {
            var active = index === state.index;
            slide.classList.toggle('jux-featured-active', active);
            if (active) {
                slide.removeAttribute('aria-hidden');
                slide.removeAttribute('inert');
            } else {
                slide.setAttribute('aria-hidden', 'true');
                slide.setAttribute('inert', '');
            }
        });
        _loadSlide(state, state.slides[state.index]);
        _loadSlide(state, state.slides[(state.index + 1) % count]);
        state.strip.innerHTML = _stripHtml(state);
        state.counter.textContent = (state.index + 1) + ' / ' + count;
    }

    function _running(state) {
        return !state.userPaused && !state.hovered && state.visible && !document.hidden && state.root.isConnected;
    }

    function _schedule(state) {
        clearTimeout(state.timer);
        state.timer = null;
        var running = _running(state);
        state.root.classList.toggle('jux-featured-running', running);
        state.stage.setAttribute('aria-live', running ? 'off' : 'polite');
        if (running) {
            state.timer = setTimeout(function () {
                if (!state.root.isConnected) {
                    destroy(state.container);
                    return;
                }
                go(state, state.index + 1);
            }, INTERVAL_MS);
        }
    }

    // The control the focus was on when the slide changed: the same button of the new slide, or the active
    // thumbnail of the rebuilt strip, so the keyboard stays in the banner (the old slide turns inert).
    function _focusTarget(state) {
        var focused = document.activeElement;
        if (!focused || !state.root.contains(focused)) {
            return null;
        }
        if (focused.closest('.jux-featured-strip')) {
            return function () { return state.strip.querySelector('.jux-featured-thumb'); };
        }
        var action = focused.closest('.jux-featured-slide') && focused.getAttribute('data-action');
        return action ? function () {
            return state.slides[state.index].querySelector('.itemAction[data-action="' + action + '"]');
        } : null;
    }

    function go(state, index) {
        var count = state.slides.length;
        var refocus = _focusTarget(state);
        state.index = ((index % count) + count) % count;
        _render(state);
        var target = refocus && refocus();
        if (target) {
            target.focus();
        }
        _schedule(state);
    }

    function _setPaused(state, paused) {
        var labels = _labels();
        state.userPaused = paused;
        state.toggle.setAttribute('aria-label', paused ? labels.resume : labels.pause);
        var icon = state.toggle.querySelector('.material-icons');
        icon.classList.toggle('pause', !paused);
        icon.classList.toggle('play_arrow', paused);
        _schedule(state);
    }

    // Every armed banner. Jellyfin empties the home without telling the banners it held: each one is
    // released on the next attach or visibility change once it has left the page (R11 constat 1; test:
    // jux-featured.test.js "releases a stopped banner").
    var _live = [];

    function destroy(container) {
        var state = container && container._juxFeatured;
        if (!state) {
            return;
        }
        clearTimeout(state.timer);
        if (state.observer) {
            state.observer.disconnect();
        }
        _live = _live.filter(function (other) { return other !== state; });
        container._juxFeatured = null;
    }

    function _sweep() {
        _live.slice().forEach(function (state) {
            if (!state.root.isConnected) {
                destroy(state.container);
            }
        });
    }

    var _listening = false;

    function _listenVisibility() {
        if (_listening) {
            return;
        }
        _listening = true;
        document.addEventListener('visibilitychange', function () {
            _sweep();
            _live.forEach(_schedule);
        });
    }

    // Keyboard focus pauses the banner, a click does not (R11 constat 6); a browser without
    // :focus-visible counts every focus.
    function _keyboardFocus(element) {
        try {
            return !!element && element.matches(':focus-visible');
        } catch (e) {
            return !!element;
        }
    }

    // Arms the banner the container just rendered; a container showing cards or an error is left alone.
    function attach(container, apiClient) {
        destroy(container);
        _sweep();
        var root = container.querySelector('.jux-featured');
        if (!root) {
            return null;
        }
        var state = {
            container: container,
            apiClient: apiClient,
            root: root,
            stage: root.querySelector('.jux-featured-stage'),
            slides: Array.prototype.slice.call(root.querySelectorAll('.jux-featured-slide')),
            strip: root.querySelector('.jux-featured-strip'),
            counter: root.querySelector('.jux-featured-counter'),
            toggle: root.querySelector('.jux-featured-toggle'),
            index: 0,
            timer: null,
            visible: typeof IntersectionObserver === 'undefined',
            hovered: false,
            userPaused: false
        };
        container._juxFeatured = state;
        _live.push(state);
        _listenVisibility();

        root.addEventListener('click', function (event) {
            var target = event.target.closest('.jux-featured-control, .jux-featured-thumb');
            if (!target) {
                return;
            }
            if (target.classList.contains('jux-featured-thumb')) {
                go(state, Number(target.dataset.index));
            } else if (target.classList.contains('jux-featured-previous')) {
                go(state, state.index - 1);
            } else if (target.classList.contains('jux-featured-next')) {
                go(state, state.index + 1);
            } else {
                _setPaused(state, !state.userPaused);
            }
        });
        root.addEventListener('keydown', function (event) {
            if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
                event.preventDefault();
                go(state, state.index + (event.key === 'ArrowRight' ? 1 : -1));
            }
        });

        // Rotation stops while the pointer or the focus is in the banner (WCAG 2.2.2).
        root.addEventListener('mouseenter', function () { state.hovered = true; _schedule(state); });
        root.addEventListener('mouseleave', function () {
            state.hovered = root.contains(document.activeElement) && _keyboardFocus(document.activeElement);
            _schedule(state);
        });
        root.addEventListener('focusin', function (event) {
            state.hovered = _keyboardFocus(event.target) || root.matches(':hover');
            _schedule(state);
        });
        root.addEventListener('focusout', function (event) {
            state.hovered = root.contains(event.relatedTarget) || root.matches(':hover');
            _schedule(state);
        });

        var swipeX = null;
        state.stage.addEventListener('pointerdown', function (event) { swipeX = event.clientX; });
        state.stage.addEventListener('pointerup', function (event) {
            if (swipeX !== null && Math.abs(event.clientX - swipeX) >= SWIPE_PX) {
                go(state, state.index + (event.clientX < swipeX ? 1 : -1));
            }
            swipeX = null;
        });

        if (!state.visible) {
            state.observer = new IntersectionObserver(function (entries) {
                state.visible = entries[entries.length - 1].intersectionRatio >= 0.5;
                _schedule(state);
            }, { threshold: [0, 0.5, 1] });
            state.observer.observe(root);
        }

        _render(state);
        // Never rotates on its own under prefers-reduced-motion (KTW-01.c, D-03): paused until asked.
        _setPaused(state, _reducedMotion());
        return state;
    }

    window.JuxFeatured = {
        html: html,
        attach: attach,
        destroy: destroy
    };

    // Exported for the Vitest suite only: `module` is undefined in a browser.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            slidesOf: slidesOf,
            html: html,
            attach: attach,
            destroy: destroy,
            go: go,
            backdropWidth: backdropWidth,
            decodeBlurhash: decodeBlurhash,
            INTERVAL_MS: INTERVAL_MS,
            MAX_SLIDES: MAX_SLIDES
        };
    }
})();
