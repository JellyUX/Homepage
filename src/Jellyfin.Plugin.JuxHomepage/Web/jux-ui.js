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
// chunk-hash risk of CONTRIBUTING.md "Jellyfin Update Procedure", just for a CSS chunk instead of a JS one. Depending on a
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
    var _dialogUid = 0;

    // Rows of a list tab are read 50 at a time (TODO_V4.md 8.2, F-54): the server caps a request at
    // 100, so the tabs used to stop at the first 100 rows without saying so.
    var PAGE_SIZE = 50;

    // The search field waits this long after the last keystroke before asking the server (F-55).
    var SEARCH_DELAY_MS = 300;

    window.JuxUI = {
        showEmpty: showEmpty,
        showLoading: showLoading,
        hideLoading: hideLoading,
        buildProgressBar: buildProgressBar,
        buildStatCard: buildStatCard,
        openSortDialog: openSortDialog,
        openOptionsDialog: openOptionsDialog,
        openDialog: openDialog,
        createListTab: createListTab,
        labelFor: labelFor
    };

    // -------------------------------------------------------------------------
    // List tab (TODO_V4.md 5.1, F-35)
    // -------------------------------------------------------------------------

    // One card-grid tab of the home page (Watchlist, Progress, History): delegated click on its tab
    // button, a title with sort and filter buttons, then the rows of one JellyUX endpoint rendered
    // by Jellyfin's cardBuilder. config:
    //   id          'watchlist' -> #jux-tabbtn-watchlist, #jux-tab-watchlist, .jux-watchlist-items
    //   endpoint    JellyUX route of the rows ({ Items: [...] })
    //   logName     name used in console errors
    //   labels      the label scope, "watchlist" for the client.watchlist.* keys (JuxCore.labels), with title, empty, emptySubtitle, sortDialogTitle
    //   state       initial { sortBy, sortOrder, <filter keys> }
    //   sortOptions function (t) -> [{ value, label }]
    //   filters     [{ name, key, icon, titleKey, options: function (t) }] (optional)
    //   emptyIcon   Material icon of the empty state
    //   cardOptions extra cardBuilder options (optional)
    //   itemOf      function (row) -> BaseItemDto (optional, rows are items by default)
    //   decorate    function (container, rows, t) called after the cards are rendered (optional)
    //   header      function (container, lang) filling a block between the title and the controls
    //               (optional)
    function createListTab(config) {
        var filters = config.filters || [];
        var itemOf = config.itemOf || function (row) { return row; };

        var tab = {
            state: config.state,

            init: function () {
                document.addEventListener('click', function (event) {
                    if (event.target.closest('#jux-tabbtn-' + config.id)) {
                        tab.render();
                    }
                });
            },

            render: function () {
                var pane = document.getElementById('jux-tab-' + config.id);
                var sections = pane && pane.querySelector('.sections');
                var userId = window.ApiClient && window.ApiClient.getCurrentUserId();
                if (!sections || !userId) {
                    return;
                }

                var lang = window.JuxCore.resolveLang();
                sections.innerHTML = tab.buildShellHtml(lang, tab.state);
                if (config.header) {
                    config.header(sections.querySelector('.jux-list-header'), lang);
                }
                _wireListControls(sections, tab, config, filters, lang);
                tab.reload(sections, userId, lang);
            },

            // Reads the list again from its first row, under the controls already on screen, so the
            // search field keeps its focus while the user types.
            reload: function (sections, userId, lang) {
                var itemsContainer = sections.querySelector('.jux-' + config.id + '-items');
                var footer = sections.querySelector('.jux-list-footer');
                if (footer) {
                    footer.innerHTML = '';
                }
                showLoading(itemsContainer);

                // A new token per load: a "load more" answer arriving after a sort, filter or search
                // change belongs to the old list and is dropped.
                tab._list = { token: {}, rows: [], total: 0, userId: userId, lang: lang, sections: sections, items: itemsContainer };
                tab.loadPage(tab._list);
            },

            loadPage: function (list) {
                var query = { userId: list.userId, sortBy: tab.state.sortBy, sortOrder: tab.state.sortOrder };
                filters.forEach(function (filter) { query[filter.key] = tab.state[filter.key]; });
                if (tab.state.searchTerm) {
                    query.searchTerm = tab.state.searchTerm;
                }
                query.startIndex = list.rows.length;
                query.limit = PAGE_SIZE;

                window.ApiClient.getJSON(window.ApiClient.getUrl(config.endpoint, query)).then(function (result) {
                    if (tab._list !== list) {
                        return;
                    }
                    var firstNew = list.rows.length;
                    list.rows = list.rows.concat((result && result.Items) || []);
                    list.total = Math.max((result && result.TotalRecordCount) || 0, list.rows.length);
                    tab.renderRows(list.items, list.rows, list.lang);
                    tab.renderFooter(list, firstNew);
                }).catch(function (err) {
                    console.error('[JellyUX] ' + config.logName + ' fetch failed:', err);
                    if (tab._list !== list) {
                        return;
                    }
                    if (list.rows.length === 0) {
                        window.JuxCore.showError(list.items, { name: window.JuxCore.labels(config.labels, list.lang).title, onRetry: tab.render });
                    } else {
                        window.JuxCore.showError(list.sections.querySelector('.jux-list-footer'), {
                            compact: true,
                            onRetry: function () { tab.loadPage(list); }
                        });
                    }
                });
            },

            // "N of M" under the cards, and "Load more" while rows remain; after a load, the focus
            // moves to the first new card so a keyboard user carries on where the list grew.
            renderFooter: function (list, firstNew) {
                var footer = list.sections.querySelector('.jux-list-footer');
                if (!footer) {
                    return;
                }
                if (list.rows.length === 0) {
                    footer.innerHTML = '';
                    return;
                }

                var t = window.JuxCore.labels('list', list.lang);
                var html = '<span class="jux-list-count">' + window.JuxCore.escHtml(
                    t.count.replace('{shown}', list.rows.length).replace('{total}', list.total)) + '</span>';
                if (list.rows.length < list.total) {
                    html += '<button is="emby-button" type="button" class="raised emby-button jux-list-more"><span>' +
                        window.JuxCore.escHtml(t.more) + '</span></button>';
                }
                footer.innerHTML = html;

                var more = footer.querySelector('.jux-list-more');
                if (more) {
                    more.addEventListener('click', function () {
                        more.disabled = true;
                        tab.loadPage(list);
                    });
                }

                if (firstNew > 0) {
                    var card = list.items.querySelectorAll('.card')[firstNew];
                    var target = card && card.querySelector('a, button');
                    if (target) {
                        target.focus();
                    }
                }
            },

            buildShellHtml: function (lang, state) {
                var t = window.JuxCore.labels(config.labels, lang);
                var html = '<h2 class="sectionTitle sectionTitle-cards jux-section-title-container padded-left padded-right">' + window.JuxCore.escHtml(t.title) + '</h2>' +
                    (config.header ? '<div class="jux-list-header jux-statistics-tiles padded-left padded-right"></div>' : '') +
                    '<div class="jux-view-controls padded-left padded-right">' +
                    _listButtonHtml('jux-' + config.id + '-sort-btn', 'sort', labelFor(config.sortOptions(t), state.sortBy));
                filters.forEach(function (filter) {
                    html += _listButtonHtml('jux-' + config.id + '-' + filter.name + '-btn', filter.icon, labelFor(filter.options(t), state[filter.key]));
                });
                var searchLabel = window.JuxCore.labels('list', lang).search;
                html += '<label class="jux-list-search">' +
                    '<span class="material-icons search" aria-hidden="true"></span>' +
                    '<input type="search" class="jux-list-search-input" id="jux-' + config.id + '-search" autocomplete="off" maxlength="100"' +
                    ' placeholder="' + window.JuxCore.escHtml(searchLabel) + '" aria-label="' + window.JuxCore.escHtml(searchLabel) + '"' +
                    ' value="' + window.JuxCore.escHtml(state.searchTerm || '') + '">' +
                    '</label>';
                return html + '</div>' +
                    '<div class="jux-' + config.id + '-items itemsContainer vertical-wrap padded-left padded-right"></div>' +
                    '<div class="jux-list-footer padded-left padded-right" aria-live="polite"></div>';
            },

            renderRows: function (itemsContainer, rows, lang) {
                if (!itemsContainer) {
                    return;
                }

                var t = window.JuxCore.labels(config.labels, lang);
                if (rows.length === 0) {
                    if (tab.state.searchTerm) {
                        var noResults = window.JuxCore.labels('list', lang).noResults.replace('{term}', tab.state.searchTerm);
                        showEmpty(itemsContainer, { icon: 'search_off', title: noResults });
                    } else {
                        showEmpty(itemsContainer, { icon: config.emptyIcon, title: t.empty, subtitle: t.emptySubtitle });
                    }
                    return;
                }

                var api = window.JellyfinAPI;
                if (!api || !api.cardBuilder) {
                    itemsContainer.innerHTML = '';
                    return;
                }

                var options = {
                    items: rows.map(itemOf),
                    shape: api.getPortraitShape(false),
                    showTitle: true,
                    overlayText: false,
                    overlayPlayButton: true,
                    centerText: true,
                    showYear: true,
                    lazy: true,
                    lines: 2
                };
                Object.keys(config.cardOptions || {}).forEach(function (key) { options[key] = config.cardOptions[key]; });
                itemsContainer.innerHTML = api.cardBuilder.getCardsHtml(options);

                window.JuxCore.observeCardImages(itemsContainer);
                if (config.decorate) {
                    config.decorate(itemsContainer, rows, t);
                }
            }
        };

        return tab;
    }

    function _listButtonHtml(id, icon, label) {
        return '<button type="button" class="jux-sort-button" id="' + id + '">' +
            '<span class="material-icons ' + window.JuxCore.iconName(icon, 'sort') + '" aria-hidden="true"></span>' +
            '<span class="jux-sort-button-label">' + window.JuxCore.escHtml(label) + '</span>' +
            '<span class="material-icons arrow_drop_down" aria-hidden="true"></span>' +
            '</button>';
    }

    function _wireListControls(sections, tab, config, filters, lang) {
        var t = window.JuxCore.labels(config.labels, lang);
        var sortBtn = sections.querySelector('#jux-' + config.id + '-sort-btn');
        if (sortBtn) {
            sortBtn.addEventListener('click', function () {
                openSortDialog({
                    title: t.sortDialogTitle,
                    sortOptions: config.sortOptions(t),
                    currentSortBy: tab.state.sortBy,
                    currentSortOrder: tab.state.sortOrder,
                    onChange: function (sortBy, sortOrder) {
                        tab.state.sortBy = sortBy;
                        tab.state.sortOrder = sortOrder;
                        tab.render();
                    }
                });
            });
        }

        var search = sections.querySelector('#jux-' + config.id + '-search');
        if (search) {
            var timer = null;
            search.addEventListener('input', function () {
                clearTimeout(timer);
                timer = setTimeout(function () {
                    var term = search.value.trim();
                    if (term === (tab.state.searchTerm || '')) {
                        return;
                    }
                    tab.state.searchTerm = term;
                    tab.reload(sections, tab._list.userId, lang);
                }, SEARCH_DELAY_MS);
            });
        }

        filters.forEach(function (filter) {
            var button = sections.querySelector('#jux-' + config.id + '-' + filter.name + '-btn');
            if (!button) {
                return;
            }
            button.addEventListener('click', function () {
                openOptionsDialog({
                    title: t[filter.titleKey],
                    groups: [{ name: filter.name, options: filter.options(t), current: tab.state[filter.key] }],
                    onChange: function (values) {
                        tab.state[filter.key] = values[filter.name];
                        tab.render();
                    }
                });
            });
        });
    }

    // The label of the current value, or of the first option when the value is unknown.
    function labelFor(options, value) {
        for (var i = 0; i < options.length; i++) {
            if (options[i].value === value) { return options[i].label; }
        }
        return options[0] && options[0].label;
    }

    // -------------------------------------------------------------------------
    // Empty state
    // -------------------------------------------------------------------------

    function showEmpty(container, options) {
        if (!container) {
            return;
        }

        var icon = window.JuxCore.iconName(options && options.icon, 'info');
        var title = (options && options.title) || '';
        var subtitle = (options && options.subtitle) || '';

        container.innerHTML =
            '<div class="jux-empty-state">' +
            '<div class="jux-empty-icon"><span class="material-icons ' + icon + '" aria-hidden="true"></span></div>' +
            '<div class="jux-empty-title">' + window.JuxCore.escHtml(title) + '</div>' +
            (subtitle ? '<div class="jux-empty-subtitle">' + window.JuxCore.escHtml(subtitle) + '</div>' : '') +
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
            return '<div class="jux-progress-bar" role="progressbar" aria-label="' + _progressLabel(0, 0) + '" aria-valuenow="0" aria-valuemin="0" aria-valuemax="0"></div>';
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

        return '<div class="' + barClass + '" role="progressbar" aria-label="' + _progressLabel(watched, total) + '" aria-valuenow="' + watched + '" aria-valuemin="0" aria-valuemax="' + total + '">' +
            chunks +
            '</div>' +
            (completed ? '<span class="jux-progress-complete-badge">' + window.JuxCore.escHtml(_completeLabel()) + '</span>' : '');
    }

    // Accessible name of the bar, e.g. "21/30 episodes" (F-49): numbers only, nothing to escape.
    function _progressLabel(watched, total) {
        return window.JuxCore.t('client.list.progress', { watched: watched, total: total });
    }

    function _completeLabel() {
        return window.JuxCore.t('client.list.complete');
    }

    // -------------------------------------------------------------------------
    // Stat tile
    // -------------------------------------------------------------------------

    function buildStatCard(icon, value, label) {
        return '<div class="jux-stat-card">' +
            '<span class="material-icons jux-stat-icon ' + window.JuxCore.iconName(icon, 'info') + '" aria-hidden="true"></span>' +
            '<div class="jux-stat-body">' +
            '<div class="jux-stat-value">' + window.JuxCore.escHtml(String(value)) + '</div>' +
            '<div class="jux-stat-label">' + window.JuxCore.escHtml(label) + '</div>' +
            '</div>' +
            '</div>';
    }

    // -------------------------------------------------------------------------
    // Native-style sort dialog (emby-radio, native dialog/backdrop classes)
    // -------------------------------------------------------------------------

    // Sort usage of the options dialog: a field group and an order group.
    // options: { title, sortOptions: [{value, label}], currentSortBy, orderOptions: [{value, label}],
    //            currentSortOrder, onChange: function(sortBy, sortOrder) }
    function openSortDialog(options) {
        var onChange = options.onChange || function () {};
        return openOptionsDialog({
            title: options.title,
            groups: [
                { name: 'sortBy', options: options.sortOptions || [], current: options.currentSortBy },
                { name: 'sortOrder', options: options.orderOptions || _defaultOrderOptions(), current: options.currentSortOrder }
            ],
            onChange: function (values) { onChange(values.sortBy, values.sortOrder); }
        });
    }

    // One dialog of radio groups, shared by sorting and filtering (TODO_V4.md 7.1, F-44).
    // options: { title, groups: [{ name, options: [{value, label}], current }],
    //            onChange: function(values) } where values maps each group name to its checked value.
    function openOptionsDialog(options) {
        var groups = options.groups || [];
        var onChange = options.onChange || function () {};

        var opened = openDialog({
            title: options.title,
            className: 'jux-sort-dialog',
            contentClassName: 'jux-sort-dialog-content',
            contentHtml: function (titleId) {
                return groups.map(function (group) {
                    return _buildRadioGroup(group.options, group.current, _groupInputName(titleId, group.name));
                }).join('');
            }
        });
        var dialog = opened.dialog;

        var firstRadio = dialog.querySelector('input:checked') || dialog.querySelector('.mdl-radio__button');
        if (firstRadio) {
            firstRadio.focus();
        }

        function currentValues() {
            var values = {};
            groups.forEach(function (group) {
                var checked = dialog.querySelector('input[name="' + _groupInputName(opened.titleId, group.name) + '"]:checked');
                values[group.name] = checked ? checked.value : group.current;
            });
            return values;
        }

        dialog.querySelectorAll('.mdl-radio__button').forEach(function (input) {
            input.addEventListener('change', function () {
                if (input.checked) {
                    onChange(currentValues());
                }
            });
        });

        return { close: opened.close };
    }

    // A native-look modal dialog (backdrop, header with a back button, scrolling content): closes on
    // Escape, on the backdrop and on the back button, then gives the focus back to its opener.
    // options: title, className (extra class of the dialog), contentClassName, contentHtml (a string,
    // or a function of the title id), onClose (optional). Returns { dialog, titleId, close }; the
    // caller moves the focus into the dialog.
    function openDialog(options) {
        var opener = document.activeElement;

        _dialogUid += 1;
        var titleId = 'jux-options-dialog-title-' + _dialogUid;

        var backdrop = document.createElement('div');
        backdrop.className = 'dialogBackdrop dialogBackdropOpened';

        var container = document.createElement('div');
        container.className = 'jux-sort-dialog-container';

        var dialog = document.createElement('div');
        dialog.className = 'focuscontainer dialog formDialog centeredDialog opened ' + (options.className || '');
        dialog.setAttribute('role', 'dialog');
        dialog.setAttribute('aria-modal', 'true');
        dialog.setAttribute('aria-labelledby', titleId);

        var content = typeof options.contentHtml === 'function' ? options.contentHtml(titleId) : (options.contentHtml || '');
        dialog.innerHTML =
            '<div class="formDialogHeader">' +
            '<button is="paper-icon-button-light" class="btnCloseDialog autoSize" tabindex="-1">' +
            '<span class="material-icons arrow_back" aria-hidden="true"></span>' +
            '</button>' +
            '<h3 class="formDialogHeaderTitle" id="' + titleId + '">' + window.JuxCore.escHtml(options.title || '') + '</h3>' +
            '</div>' +
            '<div class="formDialogContent smoothScrollY ' + (options.contentClassName || '') + '">' + content + '</div>';

        container.appendChild(dialog);
        document.body.appendChild(backdrop);
        document.body.appendChild(container);

        function close() {
            backdrop.remove();
            container.remove();
            document.removeEventListener('keydown', onKeyDown);
            if (options.onClose) {
                options.onClose();
            }
            if (opener && typeof opener.focus === 'function' && document.contains(opener)) {
                opener.focus();
            }
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

        return { dialog: dialog, titleId: titleId, close: close };
    }

    // Radio names are unique per dialog, so two dialogs never share a group.
    function _groupInputName(titleId, groupName) {
        return titleId + '-' + groupName;
    }

    function _defaultOrderOptions() {
        return [
            { value: 'Descending', label: window.JuxCore.t('client.list.descending') },
            { value: 'Ascending', label: window.JuxCore.t('client.list.ascending') }
        ];
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
            '<input type="radio" is="emby-radio" name="' + window.JuxCore.escHtml(groupName) + '" value="' + window.JuxCore.escHtml(value) + '" ' +
            'class="mdl-radio__button"' + (checked ? ' checked' : '') + '>' +
            '<div class="mdl-radio__circles">' +
            '<svg><defs><clipPath id="' + clipId + '"><circle cx="50%" cy="50%" r="50%"></circle></clipPath></defs>' +
            '<circle class="mdl-radio__outer-circle" cx="50%" cy="50%" r="50%" fill="none" stroke="currentcolor" stroke-width="0.26em" clip-path="url(#' + clipId + ')"></circle>' +
            '<circle class="mdl-radio__inner-circle" cx="50%" cy="50%" r="25%" fill="currentcolor"></circle>' +
            '</svg>' +
            '<div class="mdl-radio__focus-circle"></div>' +
            '</div>' +
            '<span class="radioButtonLabel mdl-radio__label">' + window.JuxCore.escHtml(label) + '</span>' +
            '</label>';
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    // Guarded UMD-lite export (same convention as the other Web/ scripts), so Vitest can exercise the
    // pure functions directly without a real browser/DOM.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            showEmpty: showEmpty,
            buildProgressBar: buildProgressBar,
            buildStatCard: buildStatCard,
            createListTab: createListTab,
            labelFor: labelFor,
            openSortDialog: openSortDialog,
            openOptionsDialog: openOptionsDialog,
            _buildRadio: _buildRadio,
            _buildRadioGroup: _buildRadioGroup
        };
    }
})();
