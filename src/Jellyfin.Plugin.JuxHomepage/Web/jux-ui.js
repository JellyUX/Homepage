'use strict';

// JellyUX shared UI helpers for the internal tabs (Watchlist/Progress/History/Statistics).
// TODO_V3.md Phase 6 bis. Centralizes what a fresh-eyes audit of Sources/KefinTweaks-main found
// missing from our own jux-watchlist.js/jux-progress.js/jux-history.js/jux-statistics.js: an empty
// state with an icon (not just plain text), a loading indicator, a visual progress bar, a styled
// stat tile, and a native-looking sort dialog instead of a bare <select>. KefinTweaks centralizes
// these same concerns via its own modal.js/cardBuilder.js/toaster.js so every tab inherits the same
// polish -- this file plays that role for JellyUX, loaded once before the 4 tab scripts.
//
// The sort dialog reproduces Jellyfin's own native dialog/radio-button DOM (confirmed live: no
// reference plugin in Sources/ calls a native `dialogHelper`/`Loading`/`ActionSheet` module off
// `window` -- every one of them hand-builds this DOM with the native class names instead, which is
// the approach followed here too).
//
// Live verification on jellyux-test (Phase 6 bis manual test 1) found this dialog rendering fully
// broken: no dialog box, no dimensions, and giant unstyled radio SVGs. Root cause, confirmed by
// inspecting Jellyfin's own native library sort dialog (Movies > sort icon) live: the CSS for
// .centeredDialog/.mdl-radio__* genuinely exists in this Jellyfin Web build, but only inside a
// lazily-loaded webpack CSS chunk that Jellyfin fetches on demand when its own native sort dialog
// component renders -- our tabs never trigger that chunk to load, so the classes are present in our
// markup but have no matching CSS anywhere in the page. Same class of fragility as the loadSections
// chunk-hash risk documented in CLAUDE.md, just for a CSS chunk instead of a JS one. Depending on a
// second unstable chunk hash would only compound that risk, so instead of trying to force-load it,
// the small set of rules actually needed (dialog box sizing/background/shadow, a fixed+flex centering
// wrapper, and radio-circle sizing) are self-hosted in jux-ui.css under our own
// .jux-sort-dialog/.jux-sort-dialog-container classes -- copied from the real computed values of
// Jellyfin's native dialog during that same live inspection, so this renders correctly regardless of
// whether Jellyfin's own chunk happens to be loaded.
(function () {
    if (typeof window.JuxUI !== 'undefined') {
        return;
    }

    var _radioUid = 0;

    window.JuxUI = {
        showEmpty: showEmpty,
        showLoading: showLoading,
        hideLoading: hideLoading,
        buildProgressBar: buildProgressBar,
        buildStatCard: buildStatCard,
        openSortDialog: openSortDialog,
        observeCardImages: observeCardImages
    };

    // -------------------------------------------------------------------------
    // Deferred card images (TODO_V4.md 2.3, F-10)
    // -------------------------------------------------------------------------

    // cardBuilder.getCardsHtml emits ".cardImageContainer.lazy[data-src]", but a JUX tab's plain
    // <div> container never gets Jellyfin's native lazy loading, so the image has to be applied by
    // hand. Each image is applied when its card comes near the viewport, instead of all at once on
    // every tab click. One observer per container: re-rendering a container drops the observer of
    // its previous, now detached cards.
    var _imageObservers = typeof WeakMap === 'function' ? new WeakMap() : null;

    function _applyCardImage(el) {
        var src = el.getAttribute('data-src');
        if (!src) { return; }
        el.style.backgroundImage = 'url(\'' + src + '\')';
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

    // -------------------------------------------------------------------------
    // Empty state
    // -------------------------------------------------------------------------

    function showEmpty(container, options) {
        if (!container) {
            return;
        }

        var icon = (options && options.icon) || 'info';
        var title = (options && options.title) || '';
        var subtitle = (options && options.subtitle) || '';

        container.innerHTML =
            '<div class="jux-empty-state">' +
            '<div class="jux-empty-icon"><span class="material-icons ' + _escHtml(icon) + '" aria-hidden="true"></span></div>' +
            '<div class="jux-empty-title">' + _escHtml(title) + '</div>' +
            (subtitle ? '<div class="jux-empty-subtitle">' + _escHtml(subtitle) + '</div>' : '') +
            '</div>';
    }

    // -------------------------------------------------------------------------
    // Loading indicator
    // -------------------------------------------------------------------------

    function showLoading(container) {
        if (!container) {
            return;
        }

        container.innerHTML = '<div class="jux-loading" data-jux-loading="1"><div class="jux-spinner"></div></div>';
    }

    function hideLoading(container) {
        if (!container) {
            return;
        }

        var loadingEl = container.querySelector('[data-jux-loading]');
        if (loadingEl) {
            loadingEl.remove();
        }
    }

    // -------------------------------------------------------------------------
    // Progress bar (one chunk per episode, matching Sources/KefinTweaks-main's
    // createBinaryProgressBar pattern)
    // -------------------------------------------------------------------------

    // Above this many episodes, one chunk per episode would cost one DOM node each: the bar becomes a
    // single fill at the watched percentage (TODO_V4.md 2.3, F-10).
    var MAX_PROGRESS_CHUNKS = 50;

    function buildProgressBar(watched, total) {
        watched = Math.max(0, watched || 0);
        total = Math.max(0, total || 0);

        if (total === 0) {
            return '<div class="jux-progress-bar" role="progressbar" aria-valuenow="0" aria-valuemin="0" aria-valuemax="0"></div>';
        }

        var chunks = '';
        if (total > MAX_PROGRESS_CHUNKS) {
            var percent = Math.min(100, Math.round(watched / total * 1000) / 10);
            chunks = '<div class="jux-progress-fill" style="width:' + percent + '%"></div>';
        } else {
            for (var i = 0; i < total; i++) {
                chunks += '<div class="jux-progress-chunk ' + (i < watched ? 'watched' : 'unwatched') + '"></div>';
            }
        }

        var completed = watched >= total;
        var barClass = 'jux-progress-bar' +
            (total > MAX_PROGRESS_CHUNKS ? ' jux-progress-bar-continuous' : '') +
            (completed ? ' jux-progress-bar-completed' : '');

        return '<div class="' + barClass + '" role="progressbar" aria-valuenow="' + watched + '" aria-valuemin="0" aria-valuemax="' + total + '">' +
            chunks +
            '</div>' +
            (completed ? '<span class="jux-progress-complete-badge">' + _escHtml(_completeLabel()) + '</span>' : '');
    }

    function _completeLabel() {
        return _resolveLang() === 'fr' ? 'Terminé' : 'Complete';
    }

    // -------------------------------------------------------------------------
    // Stat tile
    // -------------------------------------------------------------------------

    function buildStatCard(icon, value, label) {
        return '<div class="jux-stat-card">' +
            '<span class="material-icons jux-stat-icon ' + _escHtml(icon) + '" aria-hidden="true"></span>' +
            '<div class="jux-stat-body">' +
            '<div class="jux-stat-value">' + _escHtml(String(value)) + '</div>' +
            '<div class="jux-stat-label">' + _escHtml(label) + '</div>' +
            '</div>' +
            '</div>';
    }

    // -------------------------------------------------------------------------
    // Native-style sort dialog (emby-radio, native dialog/backdrop classes)
    // -------------------------------------------------------------------------

    // options: { title, sortOptions: [{value, label}], currentSortBy, orderOptions: [{value, label}],
    //            currentSortOrder, onChange: function(sortBy, sortOrder) }
    function openSortDialog(options) {
        var sortOptions = (options && options.sortOptions) || [];
        var orderOptions = (options && options.orderOptions) || _defaultOrderOptions();
        var onChange = (options && options.onChange) || function () {};

        var backdrop = document.createElement('div');
        backdrop.className = 'dialogBackdrop dialogBackdropOpened';

        var container = document.createElement('div');
        container.className = 'jux-sort-dialog-container';

        var dialog = document.createElement('div');
        dialog.className = 'focuscontainer dialog formDialog centeredDialog opened jux-sort-dialog';
        dialog.setAttribute('role', 'dialog');

        var sortGroupHtml = _buildRadioGroup(sortOptions, options.currentSortBy, 'jux-sort-by');
        var orderGroupHtml = _buildRadioGroup(orderOptions, options.currentSortOrder, 'jux-sort-order');

        dialog.innerHTML =
            '<div class="formDialogHeader">' +
            '<button is="paper-icon-button-light" class="btnCloseDialog autoSize" tabindex="-1">' +
            '<span class="material-icons arrow_back" aria-hidden="true"></span>' +
            '</button>' +
            '<h3 class="formDialogHeaderTitle">' + _escHtml((options && options.title) || '') + '</h3>' +
            '</div>' +
            '<div class="formDialogContent smoothScrollY jux-sort-dialog-content">' +
            sortGroupHtml +
            orderGroupHtml +
            '</div>';

        container.appendChild(dialog);
        document.body.appendChild(backdrop);
        document.body.appendChild(container);

        function close() {
            backdrop.remove();
            container.remove();
            document.removeEventListener('keydown', onKeyDown);
        }

        function onKeyDown(event) {
            if (event.key === 'Escape') {
                close();
            }
        }

        backdrop.addEventListener('click', close);
        container.addEventListener('click', function (event) {
            if (event.target === container) {
                close();
            }
        });
        dialog.querySelector('.btnCloseDialog').addEventListener('click', close);
        document.addEventListener('keydown', onKeyDown);

        dialog.querySelectorAll('input[name="jux-sort-by"]').forEach(function (input) {
            input.addEventListener('change', function () {
                if (input.checked) {
                    onChange(input.value, _currentChecked(dialog, 'jux-sort-order') || options.currentSortOrder);
                }
            });
        });
        dialog.querySelectorAll('input[name="jux-sort-order"]').forEach(function (input) {
            input.addEventListener('change', function () {
                if (input.checked) {
                    onChange(_currentChecked(dialog, 'jux-sort-by') || options.currentSortBy, input.value);
                }
            });
        });

        return { close: close };
    }

    function _currentChecked(dialog, name) {
        var checked = dialog.querySelector('input[name="' + name + '"]:checked');
        return checked ? checked.value : null;
    }

    function _defaultOrderOptions() {
        var lang = _resolveLang();
        return lang === 'fr'
            ? [{ value: 'Descending', label: 'Décroissant' }, { value: 'Ascending', label: 'Croissant' }]
            : [{ value: 'Descending', label: 'Descending' }, { value: 'Ascending', label: 'Ascending' }];
    }

    function _buildRadioGroup(options, currentValue, groupName) {
        var html = '<div class="jux-sort-group">';
        for (var i = 0; i < options.length; i++) {
            html += _buildRadio(groupName, options[i].value, options[i].label, options[i].value === currentValue);
        }
        html += '</div>';
        return html;
    }

    function _buildRadio(groupName, value, label, checked) {
        _radioUid += 1;
        var clipId = 'jux-radio-cutoff-' + _radioUid;

        return '<label class="radio-label-block mdl-radio mdl-js-radio mdl-js-ripple-effect show-focus jux-radio-label">' +
            '<input type="radio" is="emby-radio" name="' + _escHtml(groupName) + '" value="' + _escHtml(value) + '" ' +
            'class="mdl-radio__button"' + (checked ? ' checked' : '') + '>' +
            '<div class="mdl-radio__circles">' +
            '<svg><defs><clipPath id="' + clipId + '"><circle cx="50%" cy="50%" r="50%"></circle></clipPath></defs>' +
            '<circle class="mdl-radio__outer-circle" cx="50%" cy="50%" r="50%" fill="none" stroke="currentcolor" stroke-width="0.26em" clip-path="url(#' + clipId + ')"></circle>' +
            '<circle class="mdl-radio__inner-circle" cx="50%" cy="50%" r="25%" fill="currentcolor"></circle>' +
            '</svg>' +
            '<div class="mdl-radio__focus-circle"></div>' +
            '</div>' +
            '<span class="radioButtonLabel mdl-radio__label">' + _escHtml(label) + '</span>' +
            '</label>';
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    function _resolveLang() {
        return (document.documentElement.lang || 'en').toLowerCase().indexOf('fr') === 0 ? 'fr' : 'en';
    }

    function _escHtml(str) {
        if (!str) { return ''; }
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Guarded UMD-lite export (same convention as the other Web/ scripts), so Vitest can exercise the
    // pure functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            showEmpty: showEmpty,
            buildProgressBar: buildProgressBar,
            buildStatCard: buildStatCard,
            observeCardImages: observeCardImages,
            _escHtml: _escHtml,
            _buildRadio: _buildRadio,
            _buildRadioGroup: _buildRadioGroup
        };
    }
})();
