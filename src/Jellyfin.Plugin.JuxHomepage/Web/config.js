// JellyUX Homepage configuration page controller (TODO_V4.md 6.2, F-33).
// jellyfin-web loads this file as an ES module through the data-controller attribute of
// config.html ("__plugin/juxhomepageconfigjs", served by Plugin.GetPages) and creates one controller
// per view. The pure helpers are exported for the Vitest suite.

export function escHtml(str) {
    if (!str) { return ''; }
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// Generic ExtraParams[key] lookup, used for admin "value" and personalized settings such as
// "excludeWatched".
export function getExtraParam(widget, key) {
    if (!widget.ExtraParams || !widget.ExtraParams.length) { return ''; }
    for (var j = 0; j < widget.ExtraParams.length; j++) {
        if (widget.ExtraParams[j].Key === key) { return widget.ExtraParams[j].Value || ''; }
    }
    return '';
}

// A TMDb key is optional; when set, it is a 32-character hexadecimal v3 key or a v4 bearer token.
export function validateTmdb(value) {
    if (!value || value.trim() === '') { return true; }
    var v = value.trim();
    if (/^[a-f0-9]{32}$/i.test(v)) { return true; }
    if (/^ey[\w-]+\.[\w-]+\.[\w-]+$/.test(v)) { return true; }
    return false;
}

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

// Widget types whose row gets an editor of its own, whatever their category.
var DEDICATED_EDITORS = {
    'jux.connected.discover-movies': 'discover',
    'jux.connected.rewards': 'rewards',
    'jux.connected.seasonal': 'seasonal'
};

var CATEGORY_EDITORS = ['Native', 'Admin', 'Personalized', 'Connected'];

// Chooses the editor of a widget row: a dedicated editor first, then the one of its category. A
// widget's category names its data source (IWidget.Category), so it alone never picks the editor
// (TODO_V4.md 6.4, F-37). Returns '' for an unknown category.
export function editorKind(widgetType, category, categoryIndex) {
    if (Object.prototype.hasOwnProperty.call(DEDICATED_EDITORS, widgetType)) {
        return DEDICATED_EDITORS[widgetType];
    }
    for (var i = 0; i < CATEGORY_EDITORS.length; i++) {
        var name = CATEGORY_EDITORS[i];
        if (category === categoryIndex[name] || category === name) {
            return name.toLowerCase();
        }
    }
    return '';
}

// Where a row dragged from index from lands when dropped on the row at index over, above it or
// (after = true) below it, as the target index of moveRow.
export function dropIndex(from, over, after) {
    var to = after ? over + 1 : over;
    return from < to ? to - 1 : to;
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
    parts.push(t('admin.viewmode.' + String(widget.ViewMode || 'Portrait').toLowerCase()));
    parts.push(t('admin.summary.items', { min: widget.MinItems, max: widget.MaxItems }));
    if (widget.Enabled === false) { parts.push(t('admin.summary.disabled')); }
    return parts.join(' \u00b7 ');
}

function createConfigPage() {
    return {
        PLUGIN_ID: '3adf1f1f-1541-4e47-b9e3-34d2d2968af6',
        _submitBound: false,
        // Cache of available values per widgetType, loaded on demand.
        _valuesCache: {},
        // Widget rows the admin unfolded; every other row shows as a one-line summary. Rows are
        // kept by reference, so reordering keeps each row's state.
        _expandedRows: new Set(),
        // Index of the row being dragged by its handle, null otherwise.
        _dragIndex: null,
        // Flat key -> translated string dictionary, loaded once via GET JuxHomepage/Localizations.
        _i18n: {},
        state: {
            config: null,
            descriptors: [],
            sortedWidgets: [],
            // Map of widgetType -> { Value: label } for collection-type widgets (Value != Label).
            valueLabelMap: {}
        },

        // Jellyfin's own display language, kept in sync on <html lang="..."> by jellyfin-web's
        // Globalize.updateCurrentCulture() every time the user changes it in their profile
        // settings. Reading it here (rather than the browser's Accept-Language header) is the
        // only reliable way to know the language actually selected in Jellyfin, since that
        // choice is stored client-side only (userSettings.language(), never synced to the server).
        _currentLang: function () {
            return (document.documentElement.lang || 'en').split('-')[0].toLowerCase();
        },

        // Fetches the full translation dictionary for the current language once per page load.
        loadLocalizations: function () {
            var self = this;
            return ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/Localizations', { lang: self._currentLang() }))
                .then(function (dict) {
                    self._i18n = dict || {};
                }).catch(function (err) {
                    console.error('[JellyUX Config] Localizations load failed:', err);
                    self._i18n = {};
                });
        },

        // Translates a key, optionally substituting named placeholders (e.g. "{count}") with
        // the values in params. Falls back to the raw key if it's missing from the dictionary,
        // matching the server-side LocalizationService's own fallback behavior.
        t: function (key, params) {
            var self = this;
            var s = (self._i18n && self._i18n[key]) || key;
            if (params) {
                Object.keys(params).forEach(function (name) {
                    s = s.replace('{' + name + '}', params[name]);
                });
            }
            return s;
        },

        // Translates a widget's default display name client-side (WidgetType is used directly
        // as the translation key, see fr.json/en.json), falling back to the English
        // DisplayName already returned by GET JuxHomepage/Widgets if the key is missing.
        translatedWidgetName: function (widgetType, desc) {
            var self = this;
            if (self._i18n && self._i18n[widgetType]) { return self._i18n[widgetType]; }
            return desc ? desc.DisplayName : widgetType;
        },

        // Sweeps every static element tagged with data-i18n and replaces its text with the
        // translated value. Safe to call multiple times (idempotent).
        translateStaticText: function () {
            var self = this;
            document.querySelectorAll('[data-i18n]').forEach(function (el) {
                el.textContent = self.t(el.getAttribute('data-i18n'));
            });
        },

        validateTmdb: function (value) {
            return validateTmdb(value);
        },

        // Category name -> numeric index, derived from GET JuxHomepage/Admin/Catalog's WidgetCategories
        // (matches WidgetCategory's declaration order server-side: Native, Admin, Personalized,
        // Connected). Populated once in loadConfig, before renderWidgets/isAdmin/etc. are ever
        // called. Never hardcode the numeric values here -- read them from _categoryIndex instead.
        _categoryIndex: {},

        editorKindOf: function (widgetType) {
            var desc = this.getDescriptor(widgetType);
            return desc ? editorKind(widgetType, desc.Category, this._categoryIndex) : '';
        },

        isAdmin: function (widgetType) {
            return this.editorKindOf(widgetType) === 'admin';
        },

        isPersonalized: function (widgetType) {
            return this.editorKindOf(widgetType) === 'personalized';
        },

        // Connected rows and the dedicated editors share the same card layout.
        isConnected: function (widgetType) {
            return ['connected', 'discover', 'rewards', 'seasonal'].indexOf(this.editorKindOf(widgetType)) !== -1;
        },

        isNative: function (widgetType) {
            return this.editorKindOf(widgetType) === 'native';
        },

        DISCOVER_MOVIES_TYPE: 'jux.connected.discover-movies',

        isDiscover: function (widgetType) {
            return widgetType === this.DISCOVER_MOVIES_TYPE;
        },

        REWARDS_TYPE: 'jux.connected.rewards',

        isRewards: function (widgetType) {
            return widgetType === this.REWARDS_TYPE;
        },

        SEASONAL_TYPE: 'jux.connected.seasonal',

        isSeasonal: function (widgetType) {
            return widgetType === this.SEASONAL_TYPE;
        },

        BECAUSE_YOU_WATCHED_TYPE: 'jux.personalized.because-you-watched',

        isBecauseYouWatched: function (widgetType) {
            return widgetType === this.BECAUSE_YOU_WATCHED_TYPE;
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

        getDescriptor: function (widgetType) {
            for (var i = 0; i < this.state.descriptors.length; i++) {
                if (this.state.descriptors[i].WidgetType === widgetType) {
                    return this.state.descriptors[i];
                }
            }
            return null;
        },

        escHtml: function (str) {
            return escHtml(str);
        },

        // Resolve the ExtraParams["value"] entry for a widget config row.
        getExtraValue: function (w) {
            return this.getExtraParam(w, 'value');
        },

        // Generic ExtraParams[key] lookup, used for admin "value" and personalized settings
        // such as "excludeWatched".
        getExtraParam: function (w, key) {
            return getExtraParam(w, key);
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

        // Populate all datalists for admin cards from the cache.
        populateDatalist: function (widgetType, datalistId) {
            var vals = this._valuesCache[widgetType] || [];
            var dl = document.getElementById(datalistId);
            if (!dl) { return; }
            var html = '';
            for (var i = 0; i < vals.length; i++) {
                html += '<option value="' + this.escHtml(vals[i].Label) + '"></option>';
            }
            dl.innerHTML = html;
        },

        renderWidgets: function () {
            var self = this;
            var list = document.getElementById('juxWidgetList');
            var widgets = this.state.sortedWidgets;
            var html = '';
            var userId = ApiClient.getCurrentUserId ? ApiClient.getCurrentUserId() : '';
            var adminTypes = [];

            if (widgets.length === 0) {
                list.innerHTML = '<p class="fieldDescription">' + self.escHtml(self.t('admin.widgets.empty')) + '</p>';
            } else {
                widgets.forEach(function (w, i) {
                    var desc = self.getDescriptor(w.WidgetType);
                    var defaultName = self.translatedWidgetName(w.WidgetType, desc);
                    var category = desc ? desc.Category : '';
                    var isFirst = i === 0;
                    var isLast = i === widgets.length - 1;
                    var customName = w.CustomDisplayName || '';
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
                    html += '<span class="checkboxOutline">';
                    html += '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>';
                    html += '<span class="material-icons checkboxIcon checkboxIcon-unchecked " aria-hidden="true"></span>';
                    html += '</span>';
                    html += '</label>';

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

                    html += '<div class="fieldDescription jux-admin-widget-summary">' + self.escHtml(widgetSummary(w, isAdm ? currentLabel : '', self.t.bind(self))) + '</div>';
                    html += '<div class="jux-admin-widget-body" id="' + bodyId + '">';

                    // Admin-only: value picker with datalist autocomplete.
                    if (isAdm) {
                        var datalistId = 'juxVals_' + i;
                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="widgetValue_' + i + '">' + self.escHtml(self.t('admin.value.label')) + '</label>';
                        html += '<input id="widgetValue_' + i + '" type="text" list="' + datalistId + '"';
                        html += ' class="emby-input jux-admin-widget-value"';
                        html += ' placeholder="' + self.escHtml(self.t('admin.value.placeholder')) + '"';
                        html += ' value="' + self.escHtml(currentLabel || currentValue) + '" />';
                        html += '<datalist id="' + datalistId + '"></datalist>';
                        html += '<div class="fieldDescription jux-admin-value-error" style="display:none;" id="juxValError_' + i + '">' + self.escHtml(self.t('admin.value.error')) + '</div>';
                        html += '</div>';
                    }

                    if (isPers) {
                        html += '<p class="fieldDescription">' + self.escHtml(self.t('admin.personalized.note')) + '</p>';
                    } else {
                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="widgetName_' + i + '">' + self.escHtml(self.t('admin.name.label')) + '</label>';
                        html += '<input id="widgetName_' + i + '" type="text" class="emby-input jux-admin-widget-name"';
                        html += ' placeholder="' + self.escHtml(defaultName) + '"';
                        html += ' value="' + self.escHtml(customName) + '" />';
                        html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.name.help')) + '</div>';
                        html += '</div>';
                    }

                    if (self.isDiscover(w.WidgetType)) {
                        var genreValue = self.getExtraParam(w, 'genreIds');
                        var personValue = self.getExtraParam(w, 'personIds');
                        var keywordValue = self.getExtraParam(w, 'keywordIds');
                        var companyValue = self.getExtraParam(w, 'companyIds');
                        var sortByValue = self.getExtraParam(w, 'sortBy') || 'popularity.desc';
                        var yearValue = self.getExtraParam(w, 'year');
                        var minRatingValue = self.getExtraParam(w, 'minRating');
                        var minVotesValue = self.getExtraParam(w, 'minVotes') || '50';
                        var discoverPagesValue = self.getExtraParam(w, 'pages') || '1';

                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="discGenre_' + i + '">' + self.escHtml(self.t('admin.discover.genre.label')) + '</label>';
                        html += '<select id="discGenre_' + i + '" class="emby-select-withcolor emby-select jux-discover-genre">';
                        html += '<option value="">' + self.escHtml(self.t('admin.discover.genre.any')) + '</option>';
                        self._tmdbGenres.forEach(function (g) {
                            html += '<option value="' + self.escHtml(g.Value) + '"' + (g.Value === genreValue ? ' selected' : '') + '>' + self.escHtml(g.Label) + '</option>';
                        });
                        html += '</select>';
                        html += '</div>';

                        var discDatalistId = 'discVals_' + i;
                        [
                            ['person', self.t('admin.discover.person'), personValue],
                            ['keyword', self.t('admin.discover.keyword'), keywordValue],
                            ['company', self.t('admin.discover.company'), companyValue]
                        ].forEach(function (field) {
                            var fieldName = field[0], fieldLabel = field[1], fieldValue = field[2];
                            var currentLabel = fieldValue ? self.getDiscoverLabelForValue(fieldName, fieldValue) : '';
                            html += '<div class="inputContainer">';
                            html += '<label class="inputLabel inputLabelUnfocused" for="disc' + fieldName + '_' + i + '">' + self.escHtml(fieldLabel) + '</label>';
                            html += '<input id="disc' + fieldName + '_' + i + '" type="text" list="' + discDatalistId + '_' + fieldName + '"';
                            html += ' class="emby-input jux-discover-' + fieldName + '" data-field="' + fieldName + '"';
                            html += ' placeholder="' + self.escHtml(self.t('admin.discover.search.placeholder')) + '" value="' + self.escHtml(currentLabel || fieldValue) + '" />';
                            html += '<datalist id="' + discDatalistId + '_' + fieldName + '"></datalist>';
                            html += '</div>';
                        });

                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="discSortBy_' + i + '">' + self.escHtml(self.t('admin.discover.sortby.label')) + '</label>';
                        html += '<select id="discSortBy_' + i + '" class="emby-select-withcolor emby-select jux-discover-sortby">';
                        [
                            ['popularity.desc', self.t('admin.discover.sortby.popularity')],
                            ['vote_average.desc', self.t('admin.discover.sortby.rating')],
                            ['vote_count.desc', self.t('admin.discover.sortby.votecount')],
                            ['primary_release_date.desc', self.t('admin.discover.sortby.releasedate')],
                            ['revenue.desc', self.t('admin.discover.sortby.revenue')],
                            ['title.asc', self.t('admin.discover.sortby.title')]
                        ].forEach(function (opt) {
                            html += '<option value="' + opt[0] + '"' + (sortByValue === opt[0] ? ' selected' : '') + '>' + self.escHtml(opt[1]) + '</option>';
                        });
                        html += '</select>';
                        html += '</div>';

                        html += '<div class="jux-admin-row">';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="discYear_' + i + '">' + self.escHtml(self.t('admin.discover.year')) + '</label>';
                        html += '<input id="discYear_' + i + '" type="number" class="emby-input jux-discover-year" value="' + self.escHtml(yearValue) + '" />';
                        html += '</div>';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="discMinRating_' + i + '">' + self.escHtml(self.t('admin.discover.minrating')) + '</label>';
                        html += '<input id="discMinRating_' + i + '" type="number" min="0" max="10" step="0.1" class="emby-input jux-discover-minrating" value="' + self.escHtml(minRatingValue) + '" />';
                        html += '</div>';
                        html += '</div>';

                        html += '<div class="jux-admin-row">';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="discMinVotes_' + i + '">' + self.escHtml(self.t('admin.discover.minvotes')) + '</label>';
                        html += '<input id="discMinVotes_' + i + '" type="number" min="0" class="emby-input jux-discover-minvotes" value="' + self.escHtml(minVotesValue) + '" />';
                        html += '</div>';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="discPages_' + i + '">' + self.escHtml(self.t('admin.discover.pages')) + '</label>';
                        html += '<input id="discPages_' + i + '" type="number" min="1" max="5" class="emby-input jux-discover-pages" value="' + self.escHtml(discoverPagesValue) + '" />';
                        html += '</div>';
                        html += '</div>';
                    }

                    if (self.isRewards(w.WidgetType)) {
                        var ceremonyValue = self.getExtraParam(w, 'ceremonyQid');
                        var categoryValue = self.getExtraParam(w, 'categoryQid');
                        var rewardsYearValue = self.getExtraParam(w, 'year');
                        var rewardsDatalistId = 'rewardsVals_' + i;

                        [
                            ['ceremony', self.t('admin.rewards.ceremony'), ceremonyValue],
                            ['category', self.t('admin.rewards.category'), categoryValue]
                        ].forEach(function (field) {
                            var fieldName = field[0], fieldLabel = field[1], fieldValue = field[2];
                            var currentLabel = fieldValue ? self.getRewardsLabelForValue(fieldName, fieldValue) : '';
                            html += '<div class="inputContainer">';
                            html += '<label class="inputLabel inputLabelUnfocused" for="rewards' + fieldName + '_' + i + '">' + self.escHtml(fieldLabel) + '</label>';
                            html += '<input id="rewards' + fieldName + '_' + i + '" type="text" list="' + rewardsDatalistId + '_' + fieldName + '"';
                            html += ' class="emby-input jux-rewards-' + fieldName + '" data-field="' + fieldName + '"';
                            html += ' placeholder="' + self.escHtml(self.t('admin.discover.search.placeholder')) + '" value="' + self.escHtml(currentLabel || fieldValue) + '" />';
                            html += '<datalist id="' + rewardsDatalistId + '_' + fieldName + '"></datalist>';
                            html += '</div>';
                        });

                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="rewardsYear_' + i + '">' + self.escHtml(self.t('admin.rewards.year')) + '</label>';
                        html += '<input id="rewardsYear_' + i + '" type="number" class="emby-input jux-rewards-year" value="' + self.escHtml(rewardsYearValue) + '" />';
                        html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.rewards.help')) + '</div>';
                        html += '</div>';
                    }

                    if (self.isSeasonal(w.WidgetType)) {
                        var seasonalThemeValue = self.getExtraParam(w, 'theme');
                        var seasonalStartValue = self.getExtraParam(w, 'startDate');
                        var seasonalEndValue = self.getExtraParam(w, 'endDate');
                        var seasonalGenreValue = self.getExtraParam(w, 'genre');
                        var seasonalTagValue = self.getExtraParam(w, 'tag');

                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="seasonalTheme_' + i + '">' + self.escHtml(self.t('admin.seasonal.theme.label')) + '</label>';
                        html += '<select id="seasonalTheme_' + i + '" class="emby-select-withcolor emby-select jux-seasonal-theme">';
                        [
                            ['', self.t('admin.seasonal.theme.none')],
                            ['halloween', self.t('admin.seasonal.theme.halloween')],
                            ['christmas', self.t('admin.seasonal.theme.christmas')],
                            ['valentines', self.t('admin.seasonal.theme.valentines')],
                            ['newyear', self.t('admin.seasonal.theme.newyear')]
                        ].forEach(function (opt) {
                            html += '<option value="' + opt[0] + '"' + (seasonalThemeValue === opt[0] ? ' selected' : '') + '>' + self.escHtml(opt[1]) + '</option>';
                        });
                        html += '</select>';
                        html += '</div>';

                        html += '<div class="jux-admin-row">';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="seasonalStart_' + i + '">' + self.escHtml(self.t('admin.seasonal.startdate.label')) + '</label>';
                        html += '<input id="seasonalStart_' + i + '" type="text" class="emby-input jux-seasonal-startdate" placeholder="MM-JJ" pattern="\\d{2}-\\d{2}" value="' + self.escHtml(seasonalStartValue) + '" />';
                        html += '</div>';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="seasonalEnd_' + i + '">' + self.escHtml(self.t('admin.seasonal.enddate.label')) + '</label>';
                        html += '<input id="seasonalEnd_' + i + '" type="text" class="emby-input jux-seasonal-enddate" placeholder="MM-JJ" pattern="\\d{2}-\\d{2}" value="' + self.escHtml(seasonalEndValue) + '" />';
                        html += '</div>';
                        html += '</div>';
                        html += '<div class="fieldDescription jux-seasonal-date-error" style="display:none;" id="seasonalDateError_' + i + '">' + self.escHtml(self.t('admin.seasonal.date.error')) + '</div>';
                        html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.seasonal.date.help')) + '</div>';

                        html += '<div class="jux-admin-row">';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="seasonalGenre_' + i + '">' + self.escHtml(self.t('admin.seasonal.genre.label')) + '</label>';
                        html += '<input id="seasonalGenre_' + i + '" type="text" class="emby-input jux-seasonal-genre" value="' + self.escHtml(seasonalGenreValue) + '" />';
                        html += '</div>';
                        html += '<div class="inputContainer jux-admin-half">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="seasonalTag_' + i + '">' + self.escHtml(self.t('admin.seasonal.tag.label')) + '</label>';
                        html += '<input id="seasonalTag_' + i + '" type="text" class="emby-input jux-seasonal-tag" value="' + self.escHtml(seasonalTagValue) + '" />';
                        html += '</div>';
                        html += '</div>';
                    }

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

                    if (isPers) {
                        var excludeWatched = self.getExtraParam(w, 'excludeWatched') !== 'false';

                        html += '<div class="checkboxContainer checkboxContainer-withDescription">';
                        html += '<label class="emby-checkbox-label">';
                        html += '<input type="checkbox" id="widgetExcludeWatched_' + i + '" class="emby-checkbox jux-admin-exclude-watched"';
                        html += (excludeWatched ? ' checked' : '') + ' />';
                        html += '<span class="checkboxLabel">' + self.escHtml(self.t('admin.excludewatched.label')) + '</span>';
                        html += '<span class="checkboxOutline">';
                        html += '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>';
                        html += '<span class="material-icons checkboxIcon checkboxIcon-unchecked " aria-hidden="true"></span>';
                        html += '</span>';
                        html += '</label>';
                        html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.excludewatched.help')) + '</div>';
                        html += '</div>';
                    }

                    if (self.isBecauseYouWatched(w.WidgetType)) {
                        var scopeValue = self.getExtraParam(w, 'scope') || 'both';

                        html += '<div class="inputContainer">';
                        html += '<label class="inputLabel inputLabelUnfocused" for="widgetScope_' + i + '">' + self.escHtml(self.t('admin.byw.scope.label')) + '</label>';
                        html += '<select id="widgetScope_' + i + '" class="emby-select-withcolor emby-select jux-admin-byw-scope">';
                        [
                            ['movies', self.t('admin.byw.scope.movies')],
                            ['series', self.t('admin.byw.scope.series')],
                            ['both', self.t('admin.byw.scope.both')]
                        ].forEach(function (opt) {
                            html += '<option value="' + opt[0] + '"' + (scopeValue === opt[0] ? ' selected' : '') + '>' + self.escHtml(opt[1]) + '</option>';
                        });
                        html += '</select>';
                        html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.byw.scope.help')) + '</div>';
                        html += '</div>';
                    }

                    html += '</div>';
                    html += '</div>';
                });

                list.innerHTML = html;

                // Bind up/down/remove buttons and pre-fetch values for admin cards.
                widgets.forEach(function (w, i) {
                    if (self.isAdmin(w.WidgetType)) {
                        adminTypes.push(w.WidgetType);
                        var datalistId = 'juxVals_' + i;
                        self.fetchValues(w.WidgetType, userId).then(function () {
                            self.populateDatalist(w.WidgetType, datalistId);
                            // Update the input when the stored Value differs from its Label
                            // (e.g. a collection GUID should display as the collection name).
                            var inputEl = document.getElementById('widgetValue_' + i);
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
                input.addEventListener('change', function () {
                    var idx = parseInt(input.getAttribute('data-index'), 10);
                    var target = parseInt(input.value, 10);
                    if (isNaN(target) || target < 1 || target > self.state.sortedWidgets.length || target === idx + 1) {
                        input.value = idx + 1;
                        return;
                    }
                    self.readDomIntoState();
                    self.state.sortedWidgets = moveRow(self.state.sortedWidgets, idx, target - 1);
                    self.renderWidgets();
                    var moved = list.querySelector('.jux-admin-position[data-index="' + (target - 1) + '"]');
                    if (moved) { moved.focus(); }
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

            // Search-as-you-type: each keystroke fetches matching values from the server.
            list.querySelectorAll('[data-is-admin="1"]').forEach(function (card) {
                var idx = parseInt(card.getAttribute('data-index'), 10);
                var w = self.state.sortedWidgets[idx];
                if (!w) { return; }
                var widgetType = w.WidgetType;
                var datalistId = 'juxVals_' + idx;
                var inputEl = card.querySelector('.jux-admin-widget-value');
                if (!inputEl) { return; }
                var timer = null;
                inputEl.addEventListener('input', function () {
                    clearTimeout(timer);
                    var search = inputEl.value.trim();
                    timer = setTimeout(function () {
                        var url = ApiClient.getUrl(
                            'JuxHomepage/Widget/' + encodeURIComponent(widgetType) + '/values',
                            { userId: userId, search: search }
                        );
                        ApiClient.getJSON(url).then(function (vals) {
                            var dl = document.getElementById(datalistId);
                            if (!dl) { return; }
                            var dlHtml = '';
                            (vals || []).forEach(function (v) {
                                dlHtml += '<option value="' + self.escHtml(v.Label) + '"></option>';
                            });
                            dl.innerHTML = dlHtml;
                            if (!self._valuesCache[widgetType]) { self._valuesCache[widgetType] = []; }
                            (vals || []).forEach(function (v) {
                                var cache = self._valuesCache[widgetType];
                                for (var k = 0; k < cache.length; k++) {
                                    if (cache[k].Value === v.Value) { return; }
                                }
                                cache.push(v);
                            });
                        }).catch(function () {});
                    }, 300);
                });
            });

            // Discover widget: search-as-you-type for person/keyword/company autocomplete.
            var searchEndpoints = { person: 'TMDb/Search/Person', keyword: 'TMDb/Search/Keyword', company: 'TMDb/Search/Company' };
            list.querySelectorAll('.jux-discover-person, .jux-discover-keyword, .jux-discover-company').forEach(function (inputEl) {
                var fieldName = inputEl.getAttribute('data-field');
                var datalistId = inputEl.getAttribute('list');
                var timer = null;
                inputEl.addEventListener('input', function () {
                    clearTimeout(timer);
                    var query = inputEl.value.trim();
                    if (!query) { return; }
                    timer = setTimeout(function () {
                        var url = ApiClient.getUrl('JuxHomepage/' + searchEndpoints[fieldName], { query: query });
                        ApiClient.getJSON(url).then(function (vals) {
                            var dl = document.getElementById(datalistId);
                            if (!dl) { return; }
                            var dlHtml = '';
                            (vals || []).forEach(function (v) {
                                dlHtml += '<option value="' + self.escHtml(v.Label) + '"></option>';
                            });
                            dl.innerHTML = dlHtml;
                            var cache = self._discoverSearchCache[fieldName];
                            (vals || []).forEach(function (v) {
                                for (var k = 0; k < cache.length; k++) {
                                    if (cache[k].Value === v.Value) { return; }
                                }
                                cache.push(v);
                            });
                        }).catch(function () {});
                    }, 300);
                });
            });

            // Rewards widget: search-as-you-type for Ceremony/Category autocomplete, proxied
            // server-side (config.js/browsers cannot set the User-Agent header Wikidata's 2026
            // access policy requires -- see JuxHomepageController.SearchRewardsCeremony/Category).
            var rewardsSearchEndpoints = { ceremony: 'Rewards/Search/Ceremony', category: 'Rewards/Search/Category' };
            list.querySelectorAll('.jux-rewards-ceremony, .jux-rewards-category').forEach(function (inputEl) {
                var fieldName = inputEl.getAttribute('data-field');
                var datalistId = inputEl.getAttribute('list');
                var timer = null;
                inputEl.addEventListener('input', function () {
                    clearTimeout(timer);
                    var query = inputEl.value.trim();
                    if (!query) { return; }
                    timer = setTimeout(function () {
                        var url = ApiClient.getUrl('JuxHomepage/' + rewardsSearchEndpoints[fieldName], { query: query });
                        ApiClient.getJSON(url).then(function (vals) {
                            var dl = document.getElementById(datalistId);
                            if (!dl) { return; }
                            var dlHtml = '';
                            (vals || []).forEach(function (v) {
                                dlHtml += '<option value="' + self.escHtml(v.Label) + '"></option>';
                            });
                            dl.innerHTML = dlHtml;
                            var cache = self._rewardsSearchCache[fieldName];
                            (vals || []).forEach(function (v) {
                                for (var k = 0; k < cache.length; k++) {
                                    if (cache[k].Value === v.Value) { return; }
                                }
                                cache.push(v);
                            });
                        }).catch(function () {});
                    }, 300);
                });
            });
        },

        readDomIntoState: function () {
            var list = document.getElementById('juxWidgetList');
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
                var valueEl = card.querySelector('.jux-admin-widget-value');
                var excludeWatchedEl = card.querySelector('.jux-admin-exclude-watched');
                if (enabledEl) { w.Enabled = enabledEl.checked; }
                if (nameEl) { w.CustomDisplayName = nameEl.value.trim() || null; }
                if (minEl) { w.MinItems = Math.max(0, parseInt(minEl.value, 10) || 0); }
                if (maxEl) { w.MaxItems = Math.max(1, parseInt(maxEl.value, 10) || 1); }
                if (viewEl) { w.ViewMode = viewEl.value; }
                if (valueEl) {
                    // Resolve the label back to a Value (handles collection-type widgets where Value != Label).
                    var label = valueEl.value.trim();
                    var resolvedValue = label ? self.getValueForLabel(w.WidgetType, label) : '';
                    // If the value changed, auto-prefill the display name with the label when empty.
                    if (resolvedValue && !w.CustomDisplayName) {
                        w.CustomDisplayName = label;
                        if (nameEl) { nameEl.value = label; }
                    }
                    w.ExtraParams = resolvedValue
                        ? [{ Key: 'value', Value: resolvedValue }]
                        : [];
                }
                if (excludeWatchedEl) {
                    w.ExtraParams = [{ Key: 'excludeWatched', Value: excludeWatchedEl.checked ? 'true' : 'false' }];
                }
                if (self.isBecauseYouWatched(w.WidgetType)) {
                    var scopeEl = card.querySelector('.jux-admin-byw-scope');
                    if (scopeEl) {
                        w.ExtraParams = (w.ExtraParams || []).concat([{ Key: 'scope', Value: scopeEl.value }]);
                    }
                }
                if (self.isDiscover(w.WidgetType)) {
                    var genreEl = card.querySelector('.jux-discover-genre');
                    var personEl = card.querySelector('.jux-discover-person');
                    var keywordEl = card.querySelector('.jux-discover-keyword');
                    var companyEl = card.querySelector('.jux-discover-company');
                    var sortByEl = card.querySelector('.jux-discover-sortby');
                    var yearEl = card.querySelector('.jux-discover-year');
                    var minRatingEl = card.querySelector('.jux-discover-minrating');
                    var minVotesEl = card.querySelector('.jux-discover-minvotes');
                    var pagesEl = card.querySelector('.jux-discover-pages');

                    var instanceId = self.getExtraParam(w, 'value');
                    var params = [{ Key: 'value', Value: instanceId }];

                    if (genreEl && genreEl.value) { params.push({ Key: 'genreIds', Value: genreEl.value }); }
                    [
                        ['person', personEl],
                        ['keyword', keywordEl],
                        ['company', companyEl]
                    ].forEach(function (entry) {
                        var fieldName = entry[0], el = entry[1];
                        if (!el) { return; }
                        var label = el.value.trim();
                        var resolved = label ? self.getDiscoverValueForLabel(fieldName, label) : '';
                        if (resolved) { params.push({ Key: fieldName + 'Ids', Value: resolved }); }
                    });
                    if (sortByEl && sortByEl.value) { params.push({ Key: 'sortBy', Value: sortByEl.value }); }
                    if (yearEl && yearEl.value) { params.push({ Key: 'year', Value: yearEl.value }); }
                    if (minRatingEl && minRatingEl.value) { params.push({ Key: 'minRating', Value: minRatingEl.value }); }
                    if (minVotesEl && minVotesEl.value) { params.push({ Key: 'minVotes', Value: minVotesEl.value }); }
                    if (pagesEl && pagesEl.value) { params.push({ Key: 'pages', Value: pagesEl.value }); }

                    w.ExtraParams = params;
                }

                if (self.isRewards(w.WidgetType)) {
                    var ceremonyEl = card.querySelector('.jux-rewards-ceremony');
                    var categoryEl = card.querySelector('.jux-rewards-category');
                    var rewardsYearEl = card.querySelector('.jux-rewards-year');

                    var rewardsInstanceId = self.getExtraParam(w, 'value');
                    var rewardsParams = [{ Key: 'value', Value: rewardsInstanceId }];

                    [
                        ['ceremony', ceremonyEl, 'ceremonyQid'],
                        ['category', categoryEl, 'categoryQid']
                    ].forEach(function (entry) {
                        var fieldName = entry[0], el = entry[1], paramKey = entry[2];
                        if (!el) { return; }
                        var label = el.value.trim();
                        var resolved = label ? self.getRewardsValueForLabel(fieldName, label) : '';
                        if (resolved) { rewardsParams.push({ Key: paramKey, Value: resolved }); }
                    });
                    if (rewardsYearEl && rewardsYearEl.value) { rewardsParams.push({ Key: 'year', Value: rewardsYearEl.value }); }

                    w.ExtraParams = rewardsParams;
                }

                if (self.isSeasonal(w.WidgetType)) {
                    var seasonalThemeEl = card.querySelector('.jux-seasonal-theme');
                    var seasonalStartEl = card.querySelector('.jux-seasonal-startdate');
                    var seasonalEndEl = card.querySelector('.jux-seasonal-enddate');
                    var seasonalGenreEl = card.querySelector('.jux-seasonal-genre');
                    var seasonalTagEl = card.querySelector('.jux-seasonal-tag');

                    var seasonalInstanceId = self.getExtraParam(w, 'value');
                    var seasonalParams = [{ Key: 'value', Value: seasonalInstanceId }];

                    if (seasonalStartEl) { seasonalParams.push({ Key: 'startDate', Value: seasonalStartEl.value.trim() }); }
                    if (seasonalEndEl) { seasonalParams.push({ Key: 'endDate', Value: seasonalEndEl.value.trim() }); }
                    if (seasonalThemeEl && seasonalThemeEl.value) { seasonalParams.push({ Key: 'theme', Value: seasonalThemeEl.value }); }
                    if (seasonalGenreEl && seasonalGenreEl.value.trim()) { seasonalParams.push({ Key: 'genre', Value: seasonalGenreEl.value.trim() }); }
                    if (seasonalTagEl && seasonalTagEl.value.trim()) { seasonalParams.push({ Key: 'tag', Value: seasonalTagEl.value.trim() }); }

                    w.ExtraParams = seasonalParams;
                }
            });
        },

        populateAddSelect: function () {
            var self = this;
            var sel = document.getElementById('juxAddWidgetType');
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
        },

        // Maps a TMDbCacheType status entry's "Type" to its TMDbLists page-count property.
        _tmdbPagesConfigKey: {
            TrendingMovies: 'TrendingMoviesPages',
            TrendingShows: 'TrendingShowsPages',
            AiringToday: 'AiringTodayPages',
            TopRatedMovies: 'TopRatedMoviesPages',
            TopRatedShows: 'TopRatedShowsPages',
            NowPlayingMovies: 'NowPlayingMoviesPages'
        },

        // Populated once via GET TMDb/Countries; used to render the Now Playing region select.
        _tmdbCountries: [],

        // Populated once via GET TMDb/Genres; used to render the Discover widget's genre select.
        _tmdbGenres: [],

        // Maps a TMDbCacheType status entry's "Type" to the translation key of its widget's
        // name (these types correspond 1:1 with a Connected widget, so the same WidgetType key
        // already in fr.json/en.json is reused rather than duplicating the label).
        _tmdbStatusLabels: {
            TrendingMovies: 'jux.connected.trending-movies',
            TrendingShows: 'jux.connected.trending-shows',
            AiringToday: 'jux.connected.airing-today',
            TopRatedMovies: 'jux.connected.top-rated-movies',
            TopRatedShows: 'jux.connected.top-rated-shows',
            NowPlayingMovies: 'jux.connected.now-playing-movies'
        },

        // Renders the per-type TMDb cache status list (last refresh timestamp + item count),
        // plus a "Pages" input controlling how many TMDb result pages (20 items each) are
        // fetched and cached for that list.
        //
        // IMPORTANT: this (re)creates the Pages/Region <input>/<select> elements, discarding
        // any value the admin has typed but not yet saved. Only call this when self.state.config
        // is known to reflect the truth the admin should see (initial load, or right after a
        // successful save) -- NEVER from a background poll like the post-"Refresh now" status
        // check, which must use updateTmdbStatusText instead so it cannot silently wipe an
        // in-progress, unsaved edit. (This is exactly the bug that let a stale default Pages
        // value get silently re-displayed and then genuinely persisted over a real save.)
        renderTmdbStatus: function (statuses) {
            var self = this;
            var list = document.getElementById('juxTmdbStatusList');
            if (!list) { return; }
            var labels = self._tmdbStatusLabels;
            var tmdbLists = (self.state.config && self.state.config.TMDbLists) || {};
            var currentRegion = tmdbLists.NowPlayingRegion || '';
            var currentVoteCountMin = tmdbLists.TopRatedVoteCountMin || 200;
            var html = '';
            (statuses || []).forEach(function (s) {
                var labelKey = labels[s.Type];
                var label = labelKey ? self.t(labelKey) : s.Type;
                var lastRefreshed = s.LastRefreshedUtc ? new Date(s.LastRefreshedUtc).toLocaleString() : self.t('admin.tmdbstatus.never');
                var pagesKey = self._tmdbPagesConfigKey[s.Type];
                var pages = pagesKey ? (tmdbLists[pagesKey] || 1) : 1;
                html += '<div class="jux-admin-tmdb-status-row">';
                html += '<span id="tmdbLabel_' + s.Type + '">' + self.escHtml(self.t('admin.tmdbstatus.cached', { label: label, count: s.ItemCount })) + '</span>';
                html += '<span id="tmdbRefreshed_' + s.Type + '">' + self.escHtml(lastRefreshed) + '</span>';
                if (pagesKey) {
                    html += '<span class="jux-admin-tmdb-pages">';
                    html += '<label for="numTmdbPages_' + s.Type + '">' + self.escHtml(self.t('admin.tmdbstatus.pages')) + '</label>';
                    html += '<input type="number" min="1" max="5" id="numTmdbPages_' + s.Type + '"';
                    html += ' class="emby-input jux-admin-tmdb-pages-input" value="' + pages + '" />';
                    html += '</span>';
                }
                if (s.Type === 'NowPlayingMovies') {
                    html += '<span class="jux-admin-tmdb-pages">';
                    html += '<label for="selNowPlayingRegion">' + self.escHtml(self.t('admin.tmdbregion.label')) + '</label>';
                    html += '<select id="selNowPlayingRegion" class="emby-select jux-admin-tmdb-pages-input">';
                    html += '<option value="">' + self.escHtml(self.t('admin.tmdbregion.default')) + '</option>';
                    self._tmdbCountries.forEach(function (c) {
                        var selected = c.Value === currentRegion ? ' selected' : '';
                        html += '<option value="' + self.escHtml(c.Value) + '"' + selected + '>' + self.escHtml(c.Label) + '</option>';
                    });
                    html += '</select>';
                    html += '</span>';
                }
                if (s.Type === 'TopRatedMovies') {
                    // Shared by Top Rated Movies and Top Rated Shows -- one field, attached to
                    // this row only, not duplicated on the Top Rated Shows row.
                    html += '<span class="jux-admin-tmdb-pages">';
                    html += '<label for="numTopRatedVoteCountMin">' + self.escHtml(self.t('admin.tmdbstatus.minvotes')) + '</label>';
                    html += '<input type="number" min="0" id="numTopRatedVoteCountMin"';
                    html += ' class="emby-input jux-admin-tmdb-pages-input" value="' + currentVoteCountMin + '" />';
                    html += '</span>';
                }
                html += '</div>';
            });
            list.innerHTML = html || '<p class="fieldDescription">' + self.escHtml(self.t('admin.tmdbstatus.empty')) + '</p>';
        },

        // Updates ONLY the read-only "last refreshed" timestamp and item count text for each
        // row, leaving the Pages/Region inputs completely untouched. Safe to call from a
        // background status poll (e.g. after "Refresh now") without discarding an admin's
        // in-progress, unsaved edit to those inputs. Falls back to a full renderTmdbStatus if
        // the rows don't exist yet (e.g. this is somehow called before the first full render).
        updateTmdbStatusText: function (statuses) {
            var self = this;
            var labels = self._tmdbStatusLabels;
            var anyMissing = false;
            (statuses || []).forEach(function (s) {
                var labelEl = document.getElementById('tmdbLabel_' + s.Type);
                var refreshedEl = document.getElementById('tmdbRefreshed_' + s.Type);
                if (!labelEl || !refreshedEl) {
                    anyMissing = true;
                    return;
                }
                var labelKey = labels[s.Type];
                var label = labelKey ? self.t(labelKey) : s.Type;
                var lastRefreshed = s.LastRefreshedUtc ? new Date(s.LastRefreshedUtc).toLocaleString() : self.t('admin.tmdbstatus.never');
                labelEl.textContent = self.t('admin.tmdbstatus.cached', { label: label, count: s.ItemCount });
                refreshedEl.textContent = lastRefreshed;
            });
            if (anyMissing) {
                self.renderTmdbStatus(statuses);
            }
        },

        // Fetches the ISO country list once for the Now Playing region select.
        loadTmdbCountries: function () {
            var self = this;
            if (self._tmdbCountries.length) { return Promise.resolve(); }
            return ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Countries')).then(function (countries) {
                self._tmdbCountries = countries || [];
            }).catch(function (err) {
                console.error('[JellyUX Config] TMDb countries load failed:', err);
            });
        },

        // Fetches the movie genre list once for the Discover widget's genre select.
        loadTmdbGenres: function () {
            var self = this;
            if (self._tmdbGenres.length) { return Promise.resolve(); }
            return ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Genres')).then(function (genres) {
                self._tmdbGenres = genres || [];
            }).catch(function (err) {
                console.error('[JellyUX Config] TMDb genres load failed:', err);
            });
        },

        // Fetches the TMDb cache status after a manual "Refresh now" and updates only the
        // read-only timestamp/count text (see updateTmdbStatusText) -- it must NOT rebuild the
        // Pages/Region inputs, since the admin may be mid-edit on them and hasn't saved yet.
        loadTmdbStatus: function () {
            var self = this;
            return ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Status')).then(function (statuses) {
                self.updateTmdbStatusText(statuses);
            }).catch(function (err) {
                console.error('[JellyUX Config] TMDb status load failed:', err);
            });
        },

        loadConfig: function () {
            var self = this;
            Dashboard.showLoadingMsg();
            Promise.all([
                ApiClient.getPluginConfiguration(self.PLUGIN_ID),
                ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/Admin/Catalog')),
                ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Status')).catch(function () { return []; }),
                self.loadTmdbCountries(),
                self.loadTmdbGenres(),
                self.loadLocalizations(),
                ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/Admin/Warnings')).catch(function () { return []; })
            ]).then(function (results) {
                self.state.config = results[0];
                var catalog = results[1] || {};
                self.state.descriptors = catalog.Widgets || [];

                // Populate _categoryIndex before anything below reads it (isAdmin/isPersonalized/
                // isConnected, populateAddSelect, renderWidgets).
                (catalog.WidgetCategories || []).forEach(function (name, i) {
                    self._categoryIndex[name] = i;
                });

                self.translateStaticText();
                if (self._syncShowTmdb) { self._syncShowTmdb(); }
                self.renderTmdbStatus(results[2] || []);

                var warnings = results[6] || [];
                var banner = document.getElementById('juxWarningBanner');
                banner.innerHTML = '';
                warnings.forEach(function (code) {
                    var p = document.createElement('p');
                    p.className = 'fieldDescription jux-admin-warning';
                    p.textContent = self.t('admin.warning.' + code);
                    banner.appendChild(p);
                });
                banner.style.display = warnings.length ? '' : 'none';

                document.getElementById('chkEnabled').checked = !!self.state.config.Enabled;
                var modules = self.state.config.Modules || {};
                document.querySelectorAll('.jux-admin-module').forEach(function (el) {
                    el.checked = modules[el.getAttribute('data-module')] !== false;
                });
                document.getElementById('txtTMDb').value = (self.state.config.ApiKeys && self.state.config.ApiKeys.TMDb) || '';
                document.getElementById('numSessionTtl').value = (self.state.config.Cache && self.state.config.Cache.SessionTtlMinutes) || 15;
                document.getElementById('numTMDbRefresh').value = (self.state.config.Cache && self.state.config.Cache.TMDbRefreshIntervalHours) || 24;

                self.state.sortedWidgets = (self.state.config.Widgets || []).slice().sort(function (a, b) {
                    return a.Order - b.Order;
                });
                self.populateAddSelect();
                self.renderWidgets();

                Dashboard.hideLoadingMsg();
            }).catch(function (err) {
                console.error('[JellyUX Config] Load failed:', err);
                Dashboard.hideLoadingMsg();
            });
        },

        saveConfig: function (e) {
            e.preventDefault();
            var self = this;

            var tmdbVal = document.getElementById('txtTMDb').value.trim();
            var tmdbError = document.getElementById('juxTmdbError');
            if (!self.validateTmdb(tmdbVal)) {
                tmdbError.style.display = '';
                document.getElementById('txtTMDb').focus();
                return;
            }
            tmdbError.style.display = 'none';

            self.readDomIntoState();

            // Validate: admin sections must have a value.
            var hasValueError = false;
            var firstErrorIndex = -1;
            var list = document.getElementById('juxWidgetList');
            self.state.sortedWidgets.forEach(function (w, i) {
                if (!self.isAdmin(w.WidgetType)) { return; }
                var extraValue = self.getExtraValue(w);
                var errEl = list ? list.querySelector('#juxValError_' + i) : null;
                if (!extraValue) {
                    if (!hasValueError) { firstErrorIndex = i; }
                    hasValueError = true;
                    self._expandedRows.add(w);
                    if (errEl) { errEl.style.display = ''; }
                } else {
                    if (errEl) { errEl.style.display = 'none'; }
                }
            });

            // Seasonal sections show an inline hint (not a save-blocking error) when their
            // start/end date isn't a valid MM-JJ pair: unlike Admin's "Value" field, an invalid
            // or empty date is not a broken configuration -- SeasonalWidget itself already
            // treats it as "never active" and the section is generically excluded from the home
            // screen (same MinItems=0-items mechanism as any other widget with no results), so
            // saving is still allowed -- the admin can safely add a section and fill in its dates
            // afterward, as described in the Seasonal manual test.
            var seasonalDatePattern = /^\d{2}-\d{2}$/;
            self.state.sortedWidgets.forEach(function (w, i) {
                if (!self.isSeasonal(w.WidgetType)) { return; }
                var startVal = self.getExtraParam(w, 'startDate');
                var endVal = self.getExtraParam(w, 'endDate');
                var errEl = list ? list.querySelector('#seasonalDateError_' + i) : null;
                var valid = seasonalDatePattern.test(startVal) && seasonalDatePattern.test(endVal);
                if (errEl) { errEl.style.display = valid ? 'none' : ''; }
            });

            if (hasValueError) {
                // A folded row hides its error: unfold the rows in error, then bring the first one
                // into view with its field focused.
                self.renderWidgets();
                self.state.sortedWidgets.forEach(function (w, i) {
                    var err = document.getElementById('juxValError_' + i);
                    if (err) { err.style.display = self.isAdmin(w.WidgetType) && !self.getExtraValue(w) ? '' : 'none'; }
                });
                var firstField = document.getElementById('widgetValue_' + firstErrorIndex);
                if (firstField) { firstField.focus(); }
                return;
            }

            Dashboard.showLoadingMsg();

            ApiClient.getPluginConfiguration(self.PLUGIN_ID).then(function (config) {
                config.Enabled = document.getElementById('chkEnabled').checked;
                if (!config.Modules) { config.Modules = {}; }
                document.querySelectorAll('.jux-admin-module').forEach(function (el) {
                    config.Modules[el.getAttribute('data-module')] = el.checked;
                });
                if (!config.ApiKeys) { config.ApiKeys = {}; }
                config.ApiKeys.TMDb = tmdbVal || null;
                if (!config.Cache) { config.Cache = {}; }
                config.Cache.SessionTtlMinutes = Math.max(1, parseInt(document.getElementById('numSessionTtl').value, 10) || 15);
                config.Cache.TMDbRefreshIntervalHours = Math.max(1, parseInt(document.getElementById('numTMDbRefresh').value, 10) || 24);

                if (!config.TMDbLists) { config.TMDbLists = {}; }
                Object.keys(self._tmdbPagesConfigKey).forEach(function (type) {
                    var el = document.getElementById('numTmdbPages_' + type);
                    var configKey = self._tmdbPagesConfigKey[type];
                    if (el) {
                        config.TMDbLists[configKey] = Math.min(5, Math.max(1, parseInt(el.value, 10) || 1));
                    }
                });
                var regionEl = document.getElementById('selNowPlayingRegion');
                if (regionEl) {
                    config.TMDbLists.NowPlayingRegion = regionEl.value || null;
                }
                var voteCountMinEl = document.getElementById('numTopRatedVoteCountMin');
                if (voteCountMinEl) {
                    config.TMDbLists.TopRatedVoteCountMin = Math.max(0, parseInt(voteCountMinEl.value, 10) || 200);
                }

                config.Widgets = self.state.sortedWidgets.map(function (w, i) {
                    var isAdm = self.isAdmin(w.WidgetType);
                    return {
                        WidgetType: w.WidgetType,
                        Enabled: w.Enabled,
                        CustomDisplayName: w.CustomDisplayName || null,
                        Order: i * 10,
                        MinItems: Math.max(0, w.MinItems),
                        MaxItems: Math.max(1, w.MaxItems),
                        ViewMode: w.ViewMode,
                        AllowUserOverride: isAdm ? false : (typeof w.AllowUserOverride === 'boolean' ? w.AllowUserOverride : true),
                        MinInstances: w.MinInstances || 1,
                        MaxInstances: w.MaxInstances || 1,
                        ExtraParams: w.ExtraParams || []
                    };
                });

                return ApiClient.updatePluginConfiguration(self.PLUGIN_ID, config).then(function (result) {
                    // Keep in-memory state honest with what was just actually persisted, so
                    // that any later render (e.g. a background status poll falling back to a
                    // full re-render because a row is missing) reflects real saved values
                    // instead of what was loaded at the start of this page view.
                    self.state.config = config;
                    return result;
                });
            }).then(function (result) {
                Dashboard.hideLoadingMsg();
                Dashboard.processPluginConfigurationUpdateResult(result);
            }).catch(function (err) {
                console.error('[JellyUX Config] Save failed:', err);
                Dashboard.hideLoadingMsg();
            });
        },

        init: function () {
            var self = this;
            if (!this._submitBound) {
                document.querySelector('.juxConfigForm').addEventListener('submit', function (e) {
                    self.saveConfig(e);
                });

                var addBtn = document.getElementById('juxBtnAddWidget');
                if (addBtn) {
                    addBtn.addEventListener('click', function () {
                        var sel = document.getElementById('juxAddWidgetType');
                        var errEl = document.getElementById('juxAddWidgetError');
                        var widgetType = sel ? sel.value : '';
                        if (!widgetType) {
                            if (errEl) { errEl.style.display = ''; }
                            return;
                        }
                        if (errEl) { errEl.style.display = 'none'; }

                        var desc = self.getDescriptor(widgetType);
                        var isPers = self.isPersonalized(widgetType);
                        var isConn = self.isConnected(widgetType);
                        var isNat = self.isNative(widgetType);
                        var isDisc = self.isDiscover(widgetType);
                        var isRew = self.isRewards(widgetType);
                        var isSeas = self.isSeasonal(widgetType);
                        self.readDomIntoState();
                        var extraParams = [];
                        if (isPers) {
                            extraParams = [{ Key: 'excludeWatched', Value: 'true' }];
                        } else if (isDisc) {
                            var newInstanceId = (window.crypto && window.crypto.randomUUID)
                                ? window.crypto.randomUUID()
                                : 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
                                    var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
                                    return v.toString(16);
                                });
                            extraParams = [
                                { Key: 'value', Value: newInstanceId },
                                { Key: 'sortBy', Value: 'popularity.desc' },
                                { Key: 'minVotes', Value: '50' },
                                { Key: 'pages', Value: '1' }
                            ];
                        } else if (isRew) {
                            var newRewardsInstanceId = (window.crypto && window.crypto.randomUUID)
                                ? window.crypto.randomUUID()
                                : 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
                                    var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
                                    return v.toString(16);
                                });
                            extraParams = [{ Key: 'value', Value: newRewardsInstanceId }];
                        } else if (isSeas) {
                            var newSeasonalInstanceId = (window.crypto && window.crypto.randomUUID)
                                ? window.crypto.randomUUID()
                                : 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
                                    var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
                                    return v.toString(16);
                                });
                            // Dates start empty on purpose: the section simply stays hidden
                            // (no items ever match an unparseable date) until the admin fills
                            // them in and saves, rather than blocking the "Add" action itself.
                            extraParams = [{ Key: 'value', Value: newSeasonalInstanceId }];
                        }
                        self.state.sortedWidgets.push({
                            WidgetType: widgetType,
                            Enabled: true,
                            CustomDisplayName: null,
                            MinItems: desc ? desc.MinItems : 4,
                            MaxItems: 20,
                            ViewMode: desc ? (desc.ViewMode || 'Portrait') : 'Portrait',
                            AllowUserOverride: isNat,
                            MinInstances: 1,
                            MaxInstances: (isPers || isConn || isNat) ? 1 : 5,
                            ExtraParams: extraParams
                        });
                        self._expandedRows.add(self.state.sortedWidgets[self.state.sortedWidgets.length - 1]);
                        self.populateAddSelect();
                        self.renderWidgets();
                    });
                }

                // The TMDb key is masked like a password; the eye button shows it for checking.
                var showTmdbBtn = document.getElementById('juxBtnShowTmdb');
                if (showTmdbBtn) {
                    var syncShowTmdb = function () {
                        var shown = document.getElementById('txtTMDb').type === 'text';
                        var label = self.t(shown ? 'admin.apikeys.tmdb.hide' : 'admin.apikeys.tmdb.show');
                        showTmdbBtn.setAttribute('aria-pressed', shown ? 'true' : 'false');
                        showTmdbBtn.setAttribute('aria-label', label);
                        showTmdbBtn.title = label;
                        showTmdbBtn.querySelector('.material-icons').textContent = shown ? 'visibility_off' : 'visibility';
                    };
                    showTmdbBtn.addEventListener('click', function () {
                        var input = document.getElementById('txtTMDb');
                        input.type = input.type === 'password' ? 'text' : 'password';
                        syncShowTmdb();
                    });
                    self._syncShowTmdb = syncShowTmdb;
                }

                var refreshBtn = document.getElementById('juxBtnRefreshTmdb');
                var refreshMsg = document.getElementById('juxTmdbRefreshMsg');
                if (refreshBtn) {
                    refreshBtn.addEventListener('click', function () {
                        refreshBtn.disabled = true;
                        if (refreshMsg) {
                            refreshMsg.textContent = self.t('admin.tmdbrefresh.inprogress');
                            refreshMsg.style.display = '';
                        }
                        ApiClient.ajax({
                            type: 'POST',
                            url: ApiClient.getUrl('JuxHomepage/TMDb/Refresh')
                        }).then(function () {
                            setTimeout(function () {
                                self.loadTmdbStatus().then(function () {
                                    refreshBtn.disabled = false;
                                    if (refreshMsg) { refreshMsg.style.display = 'none'; }
                                });
                            }, 5000);
                        }).catch(function (err) {
                            console.error('[JellyUX Config] TMDb refresh trigger failed:', err);
                            refreshBtn.disabled = false;
                            if (refreshMsg) {
                                refreshMsg.textContent = self.t('admin.tmdbrefresh.failed');
                            }
                        });
                    });
                }

                this._submitBound = true;
            }
            this.loadConfig();
        }
    };
}

export default function (view) {
    var page = createConfigPage();
    view.addEventListener('viewshow', function () {
        page.init();
    });
}
