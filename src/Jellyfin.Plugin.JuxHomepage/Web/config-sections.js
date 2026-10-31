// JellyUX Homepage admin page: the Sections tab (the row cards, their order, preview and value lookups).
// Imported by config.js as ./configurationpage?name=juxhomepageconfigsectionsjs, a plugin page of its own
// (Plugin.GetPages), so it is served like config.js, without cache headers.

import { escHtml, getExtraParam, CHECKBOX_OUTLINE_HTML, selectHtml } from './configurationpage?name=juxhomepageconfigeditorsjs';

// Returns a copy of rows with the row at index from moved to index to (clamped to the list), the
// other rows keeping their relative order.
export function moveRow(rows, from, to) {
    var copy = rows.slice();
    if (from < 0 || from >= copy.length) { return copy; }
    var target = Math.max(0, Math.min(copy.length - 1, to));
    var moved = copy.splice(from, 1)[0];
    copy.splice(target, 0, moved);
    return copy;
}

// Where a row dragged from index from lands when dropped on the row at index over, above it or
// (after = true) below it, as the target index of moveRow.
export function dropIndex(from, over, after) {
    var to = after ? over + 1 : over;
    return from < to ? to - 1 : to;
}

// The card orders of a library section (TODO_V4.md 8.1, F-53): stored as SortBy + SortOrder, offered
// as one list. Random has no direction: it is drawn once a day per user.
export var SORT_OPTIONS = [
    ['SortName', 'Ascending', 'admin.sort.name.asc'],
    ['SortName', 'Descending', 'admin.sort.name.desc'],
    ['DateCreated', 'Descending', 'admin.sort.added.desc'],
    ['DateCreated', 'Ascending', 'admin.sort.added.asc'],
    ['PremiereDate', 'Descending', 'admin.sort.release.desc'],
    ['PremiereDate', 'Ascending', 'admin.sort.release.asc'],
    ['CommunityRating', 'Descending', 'admin.sort.rating.desc'],
    ['CommunityRating', 'Ascending', 'admin.sort.rating.asc'],
    ['Random', 'Ascending', 'admin.sort.random']
];

// A new instance id for a Discover, Rewards or Seasonal row.
export function newInstanceId() {
    if (window.crypto && window.crypto.randomUUID) {
        return window.crypto.randomUUID();
    }
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0;
        return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
}

// Search-as-you-type on a field with a datalist: 300 ms after the last keystroke, asks
// options.url(query) for {Value, Label} pairs, lists their labels and adds them to options.cache(),
// read back on save to turn a label into its value. Only the latest request may fill the list: an
// older one answering late is dropped (QUAL-03.b, test "lists the answer of the latest search").
// An empty field asks nothing unless options.allowEmpty.
export function bindSearch(inputEl, options) {
    var timer = null;
    var latest = 0;
    inputEl.addEventListener('input', function () {
        clearTimeout(timer);
        var query = inputEl.value.trim();
        if (!query && !options.allowEmpty) { return; }
        timer = setTimeout(function () {
            var request = ++latest;
            ApiClient.getJSON(options.url(query)).then(function (vals) {
                var dl = inputEl.parentNode.querySelector('datalist');
                if (request !== latest || !dl) { return; }
                dl.innerHTML = (vals || []).map(function (v) {
                    return '<option value="' + escHtml(v.Label) + '"></option>';
                }).join('');
                var cache = options.cache();
                (vals || []).forEach(function (v) {
                    if (!cache.some(function (c) { return c.Value === v.Value; })) { cache.push(v); }
                });
            }).catch(function () {});
        }, 300);
    });
}

// The SORT_OPTIONS entry of a row, alphabetical when it has none or an unknown one.
export function sortOption(widget) {
    var by = widget.SortBy;
    var order = by === 'Random' ? 'Ascending' : (widget.SortOrder === 'Descending' ? 'Descending' : 'Ascending');
    return SORT_OPTIONS.filter(function (o) { return o[0] === by && o[1] === order; })[0] || SORT_OPTIONS[0];
}

// The minimum ratings a featured row may require besides 0, none (WidgetConfig.FeaturedMinRatings, KTW-02).
export var FEATURED_MIN_RATINGS = [6, 7, 8];

// The configuration row stored for a widget row at index i of the page: what Save writes and
// what Preview sends, so a preview always shows what saving would store. A field the page does not
// edit, such as the frozen per-user override settings (QUAL-02), is saved as it was loaded.
export function toConfigRow(widget, i) {
    return Object.assign({}, widget, {
        WidgetType: widget.WidgetType,
        Enabled: widget.Enabled,
        CustomDisplayName: widget.CustomDisplayName || null,
        Order: i * 10,
        MinItems: Math.max(0, widget.MinItems),
        MaxItems: Math.max(1, widget.MaxItems),
        ViewMode: widget.ViewMode,
        Featured: !!widget.Featured,
        FeaturedMinRating: FEATURED_MIN_RATINGS.indexOf(Number(widget.FeaturedMinRating)) === -1 ? 0 : Number(widget.FeaturedMinRating),
        SortBy: sortOption(widget)[0],
        SortOrder: sortOption(widget)[1],
        ExtraParams: widget.ExtraParams || []
    });
}

// The preview of a row (POST JuxHomepage/Preview, TODO_V4.md 6.5): a poster grid of its own, so
// it renders whether or not the home screen was opened in this session. imageUrl(item) returns the
// poster URL of an item, or '' when it has none.
export function previewHtml(preview, imageUrl, t) {
    var items = preview.Items || [];
    var html = '<div class="jux-admin-preview-head">';
    html += '<span class="jux-admin-preview-count">' + escHtml(t('admin.preview.count', { count: preview.TotalRecordCount })) + '</span>';
    if (preview.HiddenReason === 'MinItems') {
        html += '<span class="jux-admin-preview-hidden">' + escHtml(t('admin.preview.hidden.minitems', { min: preview.MinItems })) + '</span>';
    } else if (preview.HiddenReason === 'NoValue') {
        html += '<span class="jux-admin-preview-hidden">' + escHtml(t('admin.preview.hidden.novalue')) + '</span>';
    }
    html += '</div>';
    if (!items.length) {
        return html + '<p class="fieldDescription">' + escHtml(t('admin.preview.empty')) + '</p>';
    }
    html += '<ul class="jux-admin-preview-grid">';
    items.forEach(function (item) {
        var src = imageUrl(item);
        html += '<li class="jux-admin-preview-item">';
        html += src
            ? '<img class="jux-admin-preview-poster" src="' + escHtml(src) + '" alt="" loading="lazy" />'
            : '<span class="jux-admin-preview-poster jux-admin-preview-noposter" aria-hidden="true"></span>';
        html += '<span class="jux-admin-preview-name">' + escHtml(item.Name) + '</span>';
        html += '</li>';
    });
    return html + '</ul>';
}

// The one-line summary of a folded widget row: its chosen value or date window, view mode and
// item range, so the admin can scan every section without unfolding it.
export function widgetSummary(widget, valueLabel, t) {
    var parts = [];
    if (widget.CustomDisplayName) { parts.push(widget.CustomDisplayName); }
    if (valueLabel && parts.indexOf(valueLabel) === -1) { parts.push(valueLabel); }
    var start = getExtraParam(widget, 'startDate');
    var end = getExtraParam(widget, 'endDate');
    if (start && end) { parts.push(t('admin.summary.window', { start: start, end: end })); }
    parts.push(widget.Featured ? t('admin.featured.label') : t('admin.viewmode.' + String(widget.ViewMode || 'Portrait').toLowerCase()));
    parts.push(t('admin.summary.items', { min: widget.MinItems, max: widget.MaxItems }));
    if (widget.Enabled === false) { parts.push(t('admin.summary.disabled')); }
    return parts.join(' \u00b7 ');
}

// The Sections tab's members, merged into the page object: they read their elements in its view.
export function sectionsTab(view, byId) {
    return {
        // Binds the tab's Add button, once per view (init).
        bindSections: function () {
            var self = this;
            var addBtn = byId('juxBtnAddWidget');
            if (addBtn) {
                addBtn.addEventListener('click', function () {
                    var sel = byId('juxAddWidgetType');
                    var errEl = byId('juxAddWidgetError');
                    var widgetType = sel ? sel.value : '';
                    if (!widgetType) {
                        if (errEl) { errEl.hidden = false; }
                        return;
                    }
                    if (errEl) { errEl.hidden = true; }

                    // The row the server describes for this type (Admin/Catalog DefaultRow, QUAL-03.c); an
                    // empty instance id asks for a new one.
                    self.readDomIntoState();
                    var row = JSON.parse(JSON.stringify(self.getDescriptor(widgetType).DefaultRow));
                    row.ExtraParams.forEach(function (p) {
                        if (p.Key === 'value' && !p.Value) { p.Value = newInstanceId(); }
                    });
                    self.state.sortedWidgets.push(row);
                    self._expandedRows.add(self.state.sortedWidgets[self.state.sortedWidgets.length - 1]);
                    self.populateAddSelect();
                    self.renderWidgets();
                    var added = view.querySelector('.jux-admin-widget-card[data-index="' + (self.state.sortedWidgets.length - 1) + '"]');
                    if (added && added.scrollIntoView) { added.scrollIntoView({ block: 'center' }); }
                });
            }
        },

        // Per-field autocomplete cache for the Discover widget's person/keyword/company inputs.
        // Keyed by field name ("person"|"keyword"|"company"), each an array of {Value,Label}.
        _discoverSearchCache: { person: [], keyword: [], company: [] },

        // Same mechanism as _discoverSearchCache, for the Rewards widget's Ceremony/Category
        // fields (Value is a Wikidata Q-id, e.g. "Q102427").
        _rewardsSearchCache: { ceremony: [], category: [] },

        getDiscoverLabelForValue: function (field, value) {
            var vals = this._discoverSearchCache[field] || [];
            for (var i = 0; i < vals.length; i++) {
                if (vals[i].Value === value) { return vals[i].Label; }
            }
            return value;
        },

        getDiscoverValueForLabel: function (field, label) {
            var vals = this._discoverSearchCache[field] || [];
            for (var i = 0; i < vals.length; i++) {
                if (vals[i].Label === label) { return vals[i].Value; }
            }
            return '';
        },

        getRewardsLabelForValue: function (field, value) {
            var vals = this._rewardsSearchCache[field] || [];
            for (var i = 0; i < vals.length; i++) {
                if (vals[i].Value === value) { return vals[i].Label; }
            }
            return value;
        },

        getRewardsValueForLabel: function (field, label) {
            var vals = this._rewardsSearchCache[field] || [];
            for (var i = 0; i < vals.length; i++) {
                if (vals[i].Label === label) { return vals[i].Value; }
            }
            return '';
        },

        // An id field shows a label only once a search has cached it, so after a page load it shows the
        // raw id with an empty cache: keep the id set at render while the text is the one rendered
        // (FIAB-02; test: config.test.js "saves a loaded Discover and Rewards row unchanged"). The rendered id
        // comes first: the cache is shared by every row and two results can share a label (test: "keeps each
        // row its own id when two people found by a search share a name").
        readIdField: function (el, valueForLabel) {
            var label = el.value.trim();
            if (!label) { return ''; }
            var rendered = el.getAttribute('data-value');
            if (rendered && label === el.defaultValue.trim()) { return rendered; }
            return valueForLabel(label);
        },

        // Look up the Label for a Value from the loaded values cache.
        getLabelForValue: function (widgetType, value) {
            var vals = this._valuesCache[widgetType];
            if (!vals) { return value; }
            for (var k = 0; k < vals.length; k++) {
                if (vals[k].Value === value) { return vals[k].Label; }
            }
            return value;
        },

        // Look up the Value for a Label from the loaded values cache (reverse map, for collections).
        getValueForLabel: function (widgetType, label) {
            var vals = this._valuesCache[widgetType];
            if (!vals) { return label; }
            for (var k = 0; k < vals.length; k++) {
                if (vals[k].Label === label) { return vals[k].Value; }
            }
            return label; // fallback: treat label as value (for non-collection types)
        },

        // Fetch available values for a widget type and cache them.
        fetchValues: function (widgetType, userId) {
            var self = this;
            if (self._valuesCache[widgetType]) {
                return Promise.resolve(self._valuesCache[widgetType]);
            }
            var url = ApiClient.getUrl('JuxHomepage/Widget/' + encodeURIComponent(widgetType) + '/values', { userId: userId });
            return ApiClient.getJSON(url).then(function (vals) {
                self._valuesCache[widgetType] = vals || [];
                return self._valuesCache[widgetType];
            }).catch(function () {
                self._valuesCache[widgetType] = [];
                return [];
            });
        },

        // The id of a datalist of this page view: the browser looks up list="..." in the whole document,
        // where a hidden earlier view of the page has the same rows (A-13).
        listId: function (base) {
            return base + '_' + this._listKey;
        },

        // Populate all datalists for admin cards from the cache.
        populateDatalist: function (widgetType, datalistId) {
            var vals = this._valuesCache[widgetType] || [];
            var dl = byId(datalistId);
            if (!dl) { return; }
            var html = '';
            for (var i = 0; i < vals.length; i++) {
                html += '<option value="' + this.escHtml(vals[i].Label) + '"></option>';
            }
            dl.innerHTML = html;
        },

        renderWidgets: function () {
            var self = this;
            var list = byId('juxWidgetList');
            var widgets = this.state.sortedWidgets;
            var html = '';
            var userId = ApiClient.getCurrentUserId ? ApiClient.getCurrentUserId() : '';
            var adminTypes = [];

            if (widgets.length === 0) {
                list.innerHTML = '<p class="fieldDescription">' + self.escHtml(self.t('admin.widgets.empty')) + '</p>';
            } else {
                widgets.forEach(function (w, i) {
                    var desc = self.getDescriptor(w.WidgetType);
                    // A preset row is named by a translated key (TODO_V4.md 9.4, F-64).
                    var nameKey = self.getExtraParam(w, 'nameKey');
                    var defaultName = nameKey ? self.t(nameKey) : self.translatedWidgetName(w.WidgetType, desc);
                    var category = desc ? desc.Category : '';
                    var isFirst = i === 0;
                    var isLast = i === widgets.length - 1;
                    var editor = self.editorOf(w.WidgetType);
                    var isAdm = self.isAdmin(w.WidgetType);
                    var isPers = self.isPersonalized(w.WidgetType);
                    var isConn = self.isConnected(w.WidgetType);
                    var currentValue = self.getExtraValue(w);
                    var currentLabel = currentValue ? self.getLabelForValue(w.WidgetType, currentValue) : '';

                    var isExpanded = self._expandedRows.has(w);
                    var bodyId = 'juxWidgetBody_' + i;
                    html += '<div class="jux-admin-widget-card' + (isExpanded ? '' : ' jux-admin-widget-card-collapsed') + '" data-index="' + i + '" data-is-admin="' + (isAdm ? '1' : '0') + '" data-is-personalized="' + (isPers ? '1' : '0') + '">';

                    html += '<div class="jux-admin-widget-header">';
                    html += '<span class="material-icons jux-admin-drag-handle" draggable="true" data-index="' + i + '" aria-hidden="true"';
                    html += ' title="' + self.escHtml(self.t('admin.order.drag')) + '">drag_indicator</span>';
                    html += '<label class="emby-checkbox-label jux-admin-enabled-label">';
                    html += '<input type="checkbox" id="widgetEnabled_' + i + '" class="emby-checkbox jux-admin-enabled"';
                    html += (w.Enabled ? ' checked' : '') + ' />';
                    html += '<span class="checkboxLabel">' + self.escHtml(defaultName) + '</span>';
                    html += CHECKBOX_OUTLINE_HTML;
                    html += '</label>';
                    // Two sections of one type (two Rewards) tell apart by their own name (annex A).
                    var instanceName = w.CustomDisplayName && w.CustomDisplayName !== defaultName ? w.CustomDisplayName : '';
                    if (instanceName) { html += '<span class="jux-admin-widget-instance">' + self.escHtml(instanceName) + '</span>'; }

                    if (category !== undefined && category !== null && category !== '') {
                        var categoryLabels = {};
                        categoryLabels[self._categoryIndex.Native] = self.t('admin.category.native');
                        categoryLabels[self._categoryIndex.Admin] = self.t('admin.category.admin');
                        categoryLabels[self._categoryIndex.Personalized] = self.t('admin.category.personalized');
                        categoryLabels[self._categoryIndex.Connected] = self.t('admin.category.connected');
                        var catLabel = categoryLabels[category] || String(category);
                        html += '<span class="jux-admin-category-badge">' + self.escHtml(catLabel) + '</span>';
                    }

                    html += '<div class="jux-admin-order-buttons">';
                    html += '<input type="number" class="emby-input jux-admin-position" data-index="' + i + '" min="1" max="' + widgets.length + '" step="1"';
                    html += ' value="' + (i + 1) + '" aria-label="' + self.escHtml(self.t('admin.order.position', { name: defaultName })) + '"';
                    html += ' title="' + self.escHtml(self.t('admin.order.position', { name: defaultName })) + '" />';
                    html += '<button type="button" class="emby-button paper-icon-button-light jux-admin-btn-up"';
                    html += ' data-index="' + i + '"' + (isFirst ? ' disabled' : '') + ' title="' + self.escHtml(self.t('admin.order.up')) + '">';
                    html += '<span class="material-icons">arrow_upward</span></button>';
                    html += '<button type="button" class="emby-button paper-icon-button-light jux-admin-btn-down"';
                    html += ' data-index="' + i + '"' + (isLast ? ' disabled' : '') + ' title="' + self.escHtml(self.t('admin.order.down')) + '">';
                    html += '<span class="material-icons">arrow_downward</span></button>';
                    if (isAdm || isPers || isConn) {
                        html += '<button type="button" class="emby-button paper-icon-button-light jux-admin-btn-remove"';
                        html += ' data-index="' + i + '" title="' + self.escHtml(self.t('admin.order.remove')) + '">';
                        html += '<span class="material-icons">delete</span></button>';
                    } else {
                        // Keeps the button columns aligned on rows that cannot be removed.
                        html += '<span class="emby-button paper-icon-button-light jux-admin-btn-placeholder" aria-hidden="true"><span class="material-icons">delete</span></span>';
                    }
                    var toggleLabel = self.t(isExpanded ? 'admin.card.collapse' : 'admin.card.expand');
                    html += '<button type="button" class="emby-button paper-icon-button-light jux-admin-btn-toggle"';
                    html += ' data-index="' + i + '" aria-expanded="' + (isExpanded ? 'true' : 'false') + '" aria-controls="' + bodyId + '"';
                    html += ' title="' + self.escHtml(toggleLabel) + '" aria-label="' + self.escHtml(toggleLabel) + '">';
                    html += '<span class="material-icons" aria-hidden="true">' + (isExpanded ? 'expand_less' : 'expand_more') + '</span></button>';
                    html += '</div>';

                    html += '</div>';

                    var summarized = instanceName ? Object.assign({}, w, { CustomDisplayName: null }) : w;
                    var summaryValue = isAdm && currentLabel !== instanceName ? currentLabel : '';
                    html += '<div class="fieldDescription jux-admin-widget-summary">' + self.escHtml(widgetSummary(summarized, summaryValue, self.t.bind(self))) + '</div>';
                    html += '<div class="jux-admin-widget-body" id="' + bodyId + '">';

                    html += editor.render(self, w, i, defaultName);

                    html += '<div class="jux-admin-row">';

                    html += '<div class="inputContainer jux-admin-half">';
                    html += '<label class="inputLabel inputLabelUnfocused" for="widgetMin_' + i + '">' + self.escHtml(self.t('admin.minitems.label')) + '</label>';
                    html += '<input id="widgetMin_' + i + '" type="number" min="0" class="emby-input jux-admin-min-items" value="' + w.MinItems + '" />';
                    html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.minitems.help')) + '</div>';
                    html += '</div>';

                    html += '<div class="inputContainer jux-admin-half">';
                    html += '<label class="inputLabel inputLabelUnfocused" for="widgetMax_' + i + '">' + self.escHtml(self.t('admin.maxitems.label')) + '</label>';
                    html += '<input id="widgetMax_' + i + '" type="number" min="1" class="emby-input jux-admin-max-items" value="' + w.MaxItems + '" />';
                    html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.maxitems.help')) + '</div>';
                    html += '</div>';

                    html += '</div>';

                    html += '<div class="inputContainer">';
                    html += '<label class="inputLabel inputLabelUnfocused" for="widgetView_' + i + '">' + self.escHtml(self.t('admin.viewmode.label')) + '</label>';
                    html += '<select id="widgetView_' + i + '" class="emby-select-withcolor emby-select jux-admin-view-mode">';
                    // The persisted value must stay the English canonical WidgetViewMode constant
                    // (read server-side); only the visible option text is translated.
                    [
                        ['Landscape', self.t('admin.viewmode.landscape')],
                        ['Portrait', self.t('admin.viewmode.portrait')],
                        ['Square', self.t('admin.viewmode.square')]
                    ].forEach(function (vm) {
                        html += '<option value="' + vm[0] + '"' + (w.ViewMode === vm[0] ? ' selected' : '') + '>' + self.escHtml(vm[1]) + '</option>';
                    });
                    html += '</select>';
                    html += '</div>';

                    if (self.canFeature(w.WidgetType)) {
                        html += '<div class="jux-admin-featured">';
                        html += '<div class="checkboxContainer">';
                        html += '<label class="emby-checkbox-label">';
                        html += '<input type="checkbox" id="widgetFeatured_' + i + '" class="emby-checkbox jux-admin-featured-toggle"' + (w.Featured ? ' checked' : '') + ' />';
                        html += '<span class="checkboxLabel">' + self.escHtml(self.t('admin.featured.label')) + '</span>';
                        html += CHECKBOX_OUTLINE_HTML;
                        html += '</label>';
                        html += '</div>';
                        html += '<div class="inputContainer jux-admin-featured-rating-field">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="widgetFeaturedRating_' + i + '">' + self.escHtml(self.t('admin.featured.minrating.label')) + '</label>';
                        html += selectHtml(self, 'widgetFeaturedRating_' + i, 'jux-admin-featured-rating',
                            [['0', self.t('admin.featured.minrating.none')]].concat(FEATURED_MIN_RATINGS.map(function (r) {
                                return [String(r), self.t('admin.featured.minrating.value', { value: r })];
                            })), String(w.FeaturedMinRating || 0));
                        html += '</div>';
                        html += '</div>';
                    }

                    if (self.isSortable(w.WidgetType)) {
                        var currentSort = sortOption(w);
                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="widgetSort_' + i + '">' + self.escHtml(self.t('admin.sort.label')) + '</label>';
                        html += '<select id="widgetSort_' + i + '" class="emby-select-withcolor emby-select jux-admin-sort">';
                        SORT_OPTIONS.forEach(function (opt) {
                            html += '<option value="' + opt[0] + ':' + opt[1] + '"' + (opt === currentSort ? ' selected' : '') + '>' + self.escHtml(self.t(opt[2])) + '</option>';
                        });
                        html += '</select>';
                        html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.sort.help')) + '</div>';
                        html += '</div>';
                    }

                    if (editor.tail) { html += editor.tail(self, w, i); }

                    html += '<div class="jux-admin-preview-actions">';
                    html += '<button type="button" class="emby-button raised jux-admin-btn-preview" data-index="' + i + '" aria-controls="juxPreview_' + i + '">';
                    html += '<span>' + self.escHtml(self.t('admin.preview.button')) + '</span></button>';
                    html += '</div>';
                    html += '<div class="jux-admin-preview" id="juxPreview_' + i + '" aria-live="polite"></div>';

                    html += '</div>';
                    html += '</div>';
                });

                list.innerHTML = html;

                // Bind up/down/remove buttons and pre-fetch values for admin cards.
                widgets.forEach(function (w, i) {
                    if (self.isAdmin(w.WidgetType)) {
                        adminTypes.push(w.WidgetType);
                        var datalistId = self.listId('juxVals_' + i);
                        self.fetchValues(w.WidgetType, userId).then(function () {
                            self.populateDatalist(w.WidgetType, datalistId);
                            // Update the input when the stored Value differs from its Label
                            // (e.g. a collection GUID should display as the collection name).
                            var inputEl = byId('widgetValue_' + i);
                            if (inputEl) {
                                var rawValue = self.getExtraValue(w);
                                if (rawValue) {
                                    var lbl = self.getLabelForValue(w.WidgetType, rawValue);
                                    if (lbl !== rawValue) { inputEl.value = lbl; }
                                }
                            }
                        });
                    }
                });
            }

            list.querySelectorAll('.jux-admin-btn-preview').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    self.previewRow(parseInt(btn.getAttribute('data-index'), 10));
                });
            });

            list.querySelectorAll('.jux-admin-btn-toggle').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = parseInt(btn.getAttribute('data-index'), 10);
                    self.readDomIntoState();
                    var row = self.state.sortedWidgets[idx];
                    if (self._expandedRows.has(row)) {
                        self._expandedRows.delete(row);
                    } else {
                        self._expandedRows.add(row);
                    }
                    self.renderWidgets();
                    var again = list.querySelector('.jux-admin-btn-toggle[data-index="' + idx + '"]');
                    if (again) { again.focus(); }
                });
            });

            // Typing a position moves the row there in one step (keyboard and touch alternative to
            // dragging); an out-of-range or empty value puts the current position back.
            list.querySelectorAll('.jux-admin-position').forEach(function (input) {
                var applied = false;
                function applyPosition() {
                    // Each field moves its row once. The re-render below removes the focused field,
                    // and Chromium then fires a change on it at once, from inside the re-render: its
                    // index names another row by then, so that second call must not move anything.
                    if (applied) { return; }
                    var idx = parseInt(input.getAttribute('data-index'), 10);
                    var target = parseInt(input.value, 10);
                    if (isNaN(target) || target < 1 || target > self.state.sortedWidgets.length || target === idx + 1) {
                        input.value = idx + 1;
                        return;
                    }
                    applied = true;
                    self.readDomIntoState();
                    self.state.sortedWidgets = moveRow(self.state.sortedWidgets, idx, target - 1);
                    self.renderWidgets();
                    var moved = list.querySelector('.jux-admin-position[data-index="' + (target - 1) + '"]');
                    if (moved) { moved.focus(); }
                }
                input.addEventListener('change', applyPosition);
                // Enter would submit the whole form (implicit submission) and save the page: it
                // applies the typed position instead.
                input.addEventListener('keydown', function (e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        applyPosition();
                    }
                });
            });

            // Mouse reordering: drag a card by its handle and drop it above or below another card.
            list.querySelectorAll('.jux-admin-drag-handle').forEach(function (handle) {
                var dragCard = handle.closest('.jux-admin-widget-card');
                handle.addEventListener('dragstart', function (e) {
                    self._dragIndex = parseInt(handle.getAttribute('data-index'), 10);
                    e.dataTransfer.effectAllowed = 'move';
                    e.dataTransfer.setData('text/plain', String(self._dragIndex));
                    if (e.dataTransfer.setDragImage) { e.dataTransfer.setDragImage(dragCard, 16, 16); }
                    dragCard.classList.add('jux-admin-dragging');
                });
                handle.addEventListener('dragend', function () {
                    self._dragIndex = null;
                    list.querySelectorAll('.jux-admin-widget-card').forEach(function (c) {
                        c.classList.remove('jux-admin-dragging', 'jux-admin-drop-before', 'jux-admin-drop-after');
                    });
                });
            });
            list.querySelectorAll('.jux-admin-widget-card').forEach(function (dropCard) {
                function isAfter(e) {
                    var rect = dropCard.getBoundingClientRect();
                    return e.clientY > rect.top + rect.height / 2;
                }
                dropCard.addEventListener('dragover', function (e) {
                    if (self._dragIndex === null || self._dragIndex === undefined) { return; }
                    e.preventDefault();
                    e.dataTransfer.dropEffect = 'move';
                    var after = isAfter(e);
                    dropCard.classList.toggle('jux-admin-drop-after', after);
                    dropCard.classList.toggle('jux-admin-drop-before', !after);
                });
                dropCard.addEventListener('dragleave', function () {
                    dropCard.classList.remove('jux-admin-drop-before', 'jux-admin-drop-after');
                });
                dropCard.addEventListener('drop', function (e) {
                    if (self._dragIndex === null || self._dragIndex === undefined) { return; }
                    e.preventDefault();
                    var from = self._dragIndex;
                    var over = parseInt(dropCard.getAttribute('data-index'), 10);
                    self._dragIndex = null;
                    self.readDomIntoState();
                    self.state.sortedWidgets = moveRow(self.state.sortedWidgets, from, dropIndex(from, over, isAfter(e)));
                    self.renderWidgets();
                });
            });

            list.querySelectorAll('.jux-admin-btn-up').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = parseInt(btn.getAttribute('data-index'), 10);
                    self.readDomIntoState();
                    self.state.sortedWidgets = moveRow(self.state.sortedWidgets, idx, idx - 1);
                    self.renderWidgets();
                });
            });

            list.querySelectorAll('.jux-admin-btn-down').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = parseInt(btn.getAttribute('data-index'), 10);
                    self.readDomIntoState();
                    self.state.sortedWidgets = moveRow(self.state.sortedWidgets, idx, idx + 1);
                    self.renderWidgets();
                });
            });

            list.querySelectorAll('.jux-admin-btn-remove').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = parseInt(btn.getAttribute('data-index'), 10);
                    self.readDomIntoState();
                    self.state.sortedWidgets.splice(idx, 1);
                    self.populateAddSelect();
                    self.renderWidgets();
                });
            });

            // Search-as-you-type on the value fields: the admin value picker, then the Discover and
            // Rewards fields, proxied server-side (config.js/browsers cannot set the User-Agent header
            // Wikidata's 2026 access policy requires -- see JuxHomepageAdminController.SearchRewardsCeremony).
            list.querySelectorAll('[data-is-admin="1"]').forEach(function (card) {
                var w = self.state.sortedWidgets[parseInt(card.getAttribute('data-index'), 10)];
                var inputEl = card.querySelector('.jux-admin-widget-value');
                if (!w || !inputEl) { return; }
                var widgetType = w.WidgetType;
                bindSearch(inputEl, {
                    allowEmpty: true,
                    url: function (search) {
                        return ApiClient.getUrl('JuxHomepage/Widget/' + encodeURIComponent(widgetType) + '/values', { userId: userId, search: search });
                    },
                    cache: function () { return self._valuesCache[widgetType] || (self._valuesCache[widgetType] = []); }
                });
            });
            [
                ['.jux-discover-person, .jux-discover-keyword, .jux-discover-company', self._discoverSearchCache,
                    { person: 'TMDb/Search/Person', keyword: 'TMDb/Search/Keyword', company: 'TMDb/Search/Company' }],
                ['.jux-rewards-ceremony, .jux-rewards-category', self._rewardsSearchCache,
                    { ceremony: 'Rewards/Search/Ceremony', category: 'Rewards/Search/Category' }]
            ].forEach(function (family) {
                list.querySelectorAll(family[0]).forEach(function (inputEl) {
                    var fieldName = inputEl.getAttribute('data-field');
                    bindSearch(inputEl, {
                        url: function (query) { return ApiClient.getUrl('JuxHomepage/' + family[2][fieldName], { query: query }); },
                        cache: function () { return family[1][fieldName]; }
                    });
                });
            });
            self.updateDirty(false);
        },

        // Renders the row at index as it is on the page, unsaved, through POST JuxHomepage/Preview.
        previewRow: function (index) {
            var self = this;
            var target = byId('juxPreview_' + index);
            if (!target) { return Promise.resolve(); }
            self.readDomIntoState();
            var w = self.state.sortedWidgets[index];
            target.innerHTML = '<p class="fieldDescription">' + self.escHtml(self.t('admin.preview.loading')) + '</p>';
            return ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('JuxHomepage/Preview', { lang: self._currentLang() }),
                data: JSON.stringify(toConfigRow(w, index)),
                contentType: 'application/json',
                dataType: 'json'
            }).then(function (preview) {
                target.innerHTML = previewHtml(preview || {}, function (item) {
                    var tag = item.ImageTags && item.ImageTags.Primary;
                    return tag ? ApiClient.getUrl('Items/' + item.Id + '/Images/Primary', { tag: tag, maxHeight: 300, quality: 90 }) : '';
                }, self.t.bind(self));
            }).catch(function (err) {
                console.error('[JellyUX Config] Preview failed:', err);
                target.innerHTML = '<p class="fieldDescription jux-admin-field-error">' + self.escHtml(self.t('admin.preview.error')) + '</p>'
                    + '<button type="button" class="emby-button jux-admin-btn-preview-retry">' + self.escHtml(self.t('admin.preview.retry')) + '</button>';
                target.querySelector('.jux-admin-btn-preview-retry').addEventListener('click', function () {
                    self.previewRow(index);
                });
            });
        },

        readDomIntoState: function () {
            var list = byId('juxWidgetList');
            if (!list) { return; }
            var self = this;
            this.state.sortedWidgets.forEach(function (w, i) {
                var card = list.querySelector('.jux-admin-widget-card[data-index="' + i + '"]');
                if (!card) { return; }
                var enabledEl = card.querySelector('.jux-admin-enabled');
                var nameEl = card.querySelector('.jux-admin-widget-name');
                var minEl = card.querySelector('.jux-admin-min-items');
                var maxEl = card.querySelector('.jux-admin-max-items');
                var viewEl = card.querySelector('.jux-admin-view-mode');
                var featuredEl = card.querySelector('.jux-admin-featured-toggle');
                var ratingEl = card.querySelector('.jux-admin-featured-rating');
                var sortEl = card.querySelector('.jux-admin-sort');
                if (featuredEl) { w.Featured = featuredEl.checked; }
                if (ratingEl) { w.FeaturedMinRating = parseInt(ratingEl.value, 10) || 0; }
                if (sortEl) {
                    w.SortBy = sortEl.value.split(':')[0];
                    w.SortOrder = sortEl.value.split(':')[1];
                }
                if (enabledEl) { w.Enabled = enabledEl.checked; }
                if (nameEl) { w.CustomDisplayName = nameEl.value.trim() || null; }
                if (minEl) { w.MinItems = Math.max(0, parseInt(minEl.value, 10) || 0); }
                if (maxEl) { w.MaxItems = Math.max(1, parseInt(maxEl.value, 10) || 1); }
                if (viewEl) { w.ViewMode = viewEl.value; }
                self.editorOf(w.WidgetType).read(self, card, w);
            });
        },

        populateAddSelect: function () {
            var self = this;
            var sel = byId('juxAddWidgetType');
            if (!sel) { return; }
            var html = '<option value="">' + self.escHtml(self.t('admin.addsection.type.placeholder')) + '</option>';
            // Native sections are single-instance: offered only when absent, so an install that
            // lost them (or an admin who removed one) can add them back without duplicates.
            var presentTypes = (self.state.sortedWidgets || []).map(function (w) { return w.WidgetType; });
            self.state.descriptors.forEach(function (d) {
                var missingNative = self.isNative(d.WidgetType) && presentTypes.indexOf(d.WidgetType) === -1;
                if (missingNative || self.isAdmin(d.WidgetType) || self.isPersonalized(d.WidgetType) || self.isConnected(d.WidgetType)) {
                    html += '<option value="' + self.escHtml(d.WidgetType) + '">' + self.escHtml(self.translatedWidgetName(d.WidgetType, d)) + '</option>';
                }
            });
            sel.innerHTML = html;
        }
    };
}
