// JellyUX Homepage admin page: the editor of each kind of section row and the HTML helpers they share.
// Imported by config.js as ./configurationpage?name=juxhomepageconfigeditorsjs, a plugin page of its own
// (Plugin.GetPages), so it is served like config.js, without cache headers.

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

// Widget types whose row gets an editor of its own, whatever their category.
export var DEDICATED_EDITORS = {
    'jux.connected.discover-movies': 'discover',
    'jux.connected.rewards': 'rewards',
    'jux.connected.seasonal': 'seasonal',
    'jux.local.recently-released': 'local',
    'jux.local.top-rated': 'local'
};

// The item types a local library section can show (TODO_V4.md 9.1, F-61), stored as
// ExtraParams["itemTypes"]; the first one is the default of a new row.
export var LOCAL_ITEM_TYPES = [
    ['Movie,Series', 'admin.local.itemtypes.both'],
    ['Movie', 'admin.local.itemtypes.movies'],
    ['Series', 'admin.local.itemtypes.series']
];

// The check mark of a native checkbox (the markup Jellyfin's emby-checkbox element draws), shared by
// the static switches of config.html and the checkboxes of the widget cards.
export var CHECKBOX_OUTLINE_HTML = '<span class="checkboxOutline">' +
    '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>' +
    '<span class="material-icons checkboxIcon checkboxIcon-unchecked " aria-hidden="true"></span>' +
    '</span>';

// Adds the check mark after the label text of each static checkbox of the page, once.
export function addCheckboxOutlines(root) {
    root.querySelectorAll('.emby-checkbox-label').forEach(function (label) {
        if (!label.querySelector('.checkboxOutline')) {
            label.insertAdjacentHTML('beforeend', CHECKBOX_OUTLINE_HTML);
        }
    });
}

export var CATEGORY_EDITORS = ['Native', 'Admin', 'Personalized', 'Connected'];

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

// The options of a select filled from a TMDb list (genres, countries), which may still be loading or have
// failed (FIAB-10.b): the current value stays selected under its raw id until the list names it (DEC-P05-1).
export function listOptionsHtml(self, emptyLabel, list, current) {
    var html = '<option value="">' + self.escHtml(emptyLabel) + '</option>';
    list.forEach(function (item) {
        html += '<option value="' + self.escHtml(item.Value) + '"' + (item.Value === current ? ' selected' : '') + '>' + self.escHtml(item.Label) + '</option>';
    });
    if (current && !list.some(function (item) { return item.Value === current; })) {
        html += '<option value="' + self.escHtml(current) + '" selected>' + self.escHtml(current) + '</option>';
    }
    return html;
}

// The name field of a row, shown by every editor except the personalized one.
export function nameFieldHtml(self, w, i, defaultName) {
    var html = '<div class="inputContainer">';
    html += '<label class="inputLabel inputLabelUnfocused" for="widgetName_' + i + '">' + self.escHtml(self.t('admin.name.label')) + '</label>';
    html += '<input id="widgetName_' + i + '" type="text" class="emby-input jux-admin-widget-name"';
    html += ' placeholder="' + self.escHtml(defaultName) + '"';
    html += ' value="' + self.escHtml(w.CustomDisplayName || '') + '" />';
    html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.name.help')) + '</div>';
    html += '</div>';
    return html;
}

// A <select> of [value, label] options with the current one selected.
export function selectHtml(self, id, cls, options, current) {
    var html = '<select id="' + id + '" class="emby-select-withcolor emby-select ' + cls + '">';
    options.forEach(function (opt) {
        html += '<option value="' + opt[0] + '"' + (current === opt[0] ? ' selected' : '') + '>' + self.escHtml(opt[1]) + '</option>';
    });
    return html + '</select>';
}

// The search fields of an editor ([name, label, stored id]), each with its datalist: the label of
// a known id is shown, the id itself kept in data-value (FIAB-02.b).
export function searchFieldsHtml(self, w, i, prefix, fields, labelFor) {
    var html = '';
    fields.forEach(function (field) {
        var fieldName = field[0], fieldValue = field[2];
        var currentLabel = fieldValue ? labelFor(fieldName, fieldValue) : '';
        html += '<div class="inputContainer">';
        html += '<label class="inputLabel inputLabelUnfocused" for="' + prefix + fieldName + '_' + i + '">' + self.escHtml(field[1]) + '</label>';
        var listId = self.listId(prefix + 'Vals_' + i + '_' + fieldName);
        html += '<input id="' + prefix + fieldName + '_' + i + '" type="text" list="' + listId + '"';
        html += ' class="emby-input jux-' + (prefix === 'disc' ? 'discover' : prefix) + '-' + fieldName + '" data-field="' + fieldName + '"';
        html += ' data-value="' + self.escHtml(fieldValue) + '"';
        html += ' placeholder="' + self.escHtml(self.t('admin.discover.search.placeholder')) + '" value="' + self.escHtml(currentLabel || fieldValue) + '" />';
        html += '<datalist id="' + listId + '"></datalist>';
        html += '</div>';
    });
    return html;
}

// Two inputs side by side: [id, class, label, attributes, value] each.
export function halfInputsHtml(self, i, inputs) {
    var html = '<div class="jux-admin-row">';
    inputs.forEach(function (input) {
        html += '<div class="inputContainer jux-admin-half">';
        html += '<label class="inputLabel inputLabelUnfocused" for="' + input[0] + '_' + i + '">' + self.escHtml(self.t(input[2])) + '</label>';
        html += '<input id="' + input[0] + '_' + i + '" ' + input[3] + ' class="emby-input ' + input[1] + '"' + input[5] + ' value="' + self.escHtml(input[4]) + '" />';
        html += '</div>';
    });
    return html + '</div>';
}

// The editor of each kind of row (QUAL-03.a), chosen by editorKind: render(self, w, i, defaultName)
// returns the fields placed under the row's header, tail(self, w, i) those placed after the shared
// settings, read(self, card, w) writes them back to w.ExtraParams. The defaults of a new row come
// from Admin/Catalog (DefaultRow, QUAL-03.c). A kind without an entry shows the name field only.
export var DEFAULT_EDITOR = {
    render: nameFieldHtml,
    read: function () {}
};

export var EDITORS = {
    admin: {
        // Value picker with datalist autocomplete.
        render: function (self, w, i, defaultName) {
            var currentValue = self.getExtraValue(w);
            var currentLabel = currentValue ? self.getLabelForValue(w.WidgetType, currentValue) : '';
            var datalistId = self.listId('juxVals_' + i);
            var html = '<div class="inputContainer">';
            html += '<label class="inputLabel inputLabelUnfocused" for="widgetValue_' + i + '">' + self.escHtml(self.t('admin.value.label')) + '</label>';
            html += '<input id="widgetValue_' + i + '" type="text" list="' + datalistId + '"';
            html += ' class="emby-input jux-admin-widget-value"';
            html += ' placeholder="' + self.escHtml(self.t('admin.value.placeholder')) + '"';
            html += ' value="' + self.escHtml(currentLabel || currentValue) + '" />';
            html += '<datalist id="' + datalistId + '"></datalist>';
            html += '<div class="fieldDescription jux-admin-value-error" style="display:none;" id="juxValError_' + i + '">' + self.escHtml(self.t('admin.value.error')) + '</div>';
            html += '</div>';
            return html + nameFieldHtml(self, w, i, defaultName);
        },
        read: function (self, card, w) {
            // Resolve the label back to a Value (handles collection-type widgets where Value != Label).
            var nameEl = card.querySelector('.jux-admin-widget-name');
            var label = card.querySelector('.jux-admin-widget-value').value.trim();
            var resolvedValue = label ? self.getValueForLabel(w.WidgetType, label) : '';
            // If the value changed, auto-prefill the display name with the label when empty. An unchanged
            // row is left as loaded: the unsaved-changes check reads every row as soon as the page loads,
            // before the labels are known (test: config.test.js "loads with no unsaved change").
            if (resolvedValue && resolvedValue !== self.getExtraValue(w) && !w.CustomDisplayName) {
                w.CustomDisplayName = label;
                if (nameEl) { nameEl.value = label; }
            }
            w.ExtraParams = resolvedValue ? [{ Key: 'value', Value: resolvedValue }] : [];
        }
    },

    personalized: {
        render: function (self) {
            return '<p class="fieldDescription">' + self.escHtml(self.t('admin.personalized.note')) + '</p>';
        },
        tail: function (self, w, i) {
            var excludeWatched = self.getExtraParam(w, 'excludeWatched') !== 'false';
            var html = '<div class="checkboxContainer checkboxContainer-withDescription">';
            html += '<label class="emby-checkbox-label">';
            html += '<input type="checkbox" id="widgetExcludeWatched_' + i + '" class="emby-checkbox jux-admin-exclude-watched"';
            html += (excludeWatched ? ' checked' : '') + ' />';
            html += '<span class="checkboxLabel">' + self.escHtml(self.t('admin.excludewatched.label')) + '</span>';
            html += CHECKBOX_OUTLINE_HTML;
            html += '</label>';
            html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.excludewatched.help')) + '</div>';
            html += '</div>';
            if (self.isBecauseYouWatched(w.WidgetType)) {
                html += '<div class="inputContainer">';
                html += '<label class="inputLabel inputLabelUnfocused" for="widgetScope_' + i + '">' + self.escHtml(self.t('admin.byw.scope.label')) + '</label>';
                html += selectHtml(self, 'widgetScope_' + i, 'jux-admin-byw-scope', [
                    ['movies', self.t('admin.byw.scope.movies')],
                    ['series', self.t('admin.byw.scope.series')],
                    ['both', self.t('admin.byw.scope.both')]
                ], self.paramOrDefault(w, 'scope'));
                html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.byw.scope.help')) + '</div>';
                html += '</div>';
            }
            return html;
        },
        read: function (self, card, w) {
            w.ExtraParams = [{ Key: 'excludeWatched', Value: card.querySelector('.jux-admin-exclude-watched').checked ? 'true' : 'false' }];
            var scopeEl = card.querySelector('.jux-admin-byw-scope');
            if (scopeEl) { w.ExtraParams.push({ Key: 'scope', Value: scopeEl.value }); }
        }
    },

    discover: {
        render: function (self, w, i, defaultName) {
            var genreValue = self.getExtraParam(w, 'genreIds');
            var html = nameFieldHtml(self, w, i, defaultName);
            html += '<div class="inputContainer">';
            html += '<label class="inputLabel inputLabelUnfocused" for="discGenre_' + i + '">' + self.escHtml(self.t('admin.discover.genre.label')) + '</label>';
            html += '<select id="discGenre_' + i + '" class="emby-select-withcolor emby-select jux-discover-genre">';
            html += listOptionsHtml(self, self.t('admin.discover.genre.any'), self._tmdbGenres, genreValue);
            html += '</select>';
            html += '</div>';
            html += searchFieldsHtml(self, w, i, 'disc', [
                ['person', self.t('admin.discover.person'), self.getExtraParam(w, 'personIds')],
                ['keyword', self.t('admin.discover.keyword'), self.getExtraParam(w, 'keywordIds')],
                ['company', self.t('admin.discover.company'), self.getExtraParam(w, 'companyIds')]
            ], self.getDiscoverLabelForValue.bind(self));
            html += '<div class="inputContainer">';
            html += '<label class="inputLabel inputLabelUnfocused" for="discSortBy_' + i + '">' + self.escHtml(self.t('admin.discover.sortby.label')) + '</label>';
            html += selectHtml(self, 'discSortBy_' + i, 'jux-discover-sortby', [
                ['popularity.desc', self.t('admin.discover.sortby.popularity')],
                ['vote_average.desc', self.t('admin.discover.sortby.rating')],
                ['vote_count.desc', self.t('admin.discover.sortby.votecount')],
                ['primary_release_date.desc', self.t('admin.discover.sortby.releasedate')],
                ['revenue.desc', self.t('admin.discover.sortby.revenue')],
                ['title.asc', self.t('admin.discover.sortby.title')]
            ], self.paramOrDefault(w, 'sortBy'));
            html += '</div>';
            html += halfInputsHtml(self, i, [
                ['discYear', 'jux-discover-year', 'admin.discover.year', 'type="number"', self.getExtraParam(w, 'year'), ''],
                ['discMinRating', 'jux-discover-minrating', 'admin.discover.minrating', 'type="number" min="0" max="10" step="0.1"', self.getExtraParam(w, 'minRating'), '']
            ]);
            html += halfInputsHtml(self, i, [
                ['discMinVotes', 'jux-discover-minvotes', 'admin.discover.minvotes', 'type="number" min="0"', self.paramOrDefault(w, 'minVotes'), ''],
                ['discPages', 'jux-discover-pages', 'admin.discover.pages', 'type="number" min="1" max="5"', self.paramOrDefault(w, 'pages'), '']
            ]);
            return html;
        },
        read: function (self, card, w) {
            var params = [{ Key: 'value', Value: self.getExtraParam(w, 'value') }];
            var genreEl = card.querySelector('.jux-discover-genre');
            if (genreEl && genreEl.value) { params.push({ Key: 'genreIds', Value: genreEl.value }); }
            ['person', 'keyword', 'company'].forEach(function (fieldName) {
                var el = card.querySelector('.jux-discover-' + fieldName);
                if (!el) { return; }
                var resolved = self.readIdField(el, function (label) { return self.getDiscoverValueForLabel(fieldName, label); });
                if (resolved) { params.push({ Key: fieldName + 'Ids', Value: resolved }); }
            });
            [['sortBy', 'sortby'], ['year', 'year'], ['minRating', 'minrating'], ['minVotes', 'minvotes'], ['pages', 'pages']].forEach(function (field) {
                var el = card.querySelector('.jux-discover-' + field[1]);
                if (el && el.value) { params.push({ Key: field[0], Value: el.value }); }
            });
            w.ExtraParams = params;
        }
    },

    rewards: {
        render: function (self, w, i, defaultName) {
            var html = nameFieldHtml(self, w, i, defaultName);
            html += searchFieldsHtml(self, w, i, 'rewards', [
                ['ceremony', self.t('admin.rewards.ceremony'), self.getExtraParam(w, 'ceremonyQid')],
                ['category', self.t('admin.rewards.category'), self.getExtraParam(w, 'categoryQid')]
            ], self.getRewardsLabelForValue.bind(self));
            html += '<div class="fieldDescription jux-admin-value-error" style="display:none;" id="juxRewardsError_' + i + '">' + self.escHtml(self.t('admin.rewards.error')) + '</div>';
            html += '<div class="inputContainer">';
            html += '<label class="inputLabel inputLabelUnfocused" for="rewardsYear_' + i + '">' + self.escHtml(self.t('admin.rewards.year')) + '</label>';
            html += '<input id="rewardsYear_' + i + '" type="number" class="emby-input jux-rewards-year" value="' + self.escHtml(self.getExtraParam(w, 'year')) + '" />';
            html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.rewards.help')) + '</div>';
            html += '</div>';
            return html;
        },
        read: function (self, card, w) {
            var params = [{ Key: 'value', Value: self.getExtraParam(w, 'value') }];
            [['ceremony', 'ceremonyQid'], ['category', 'categoryQid']].forEach(function (field) {
                var el = card.querySelector('.jux-rewards-' + field[0]);
                if (!el) { return; }
                var resolved = self.readIdField(el, function (label) { return self.getRewardsValueForLabel(field[0], label); });
                if (resolved) { params.push({ Key: field[1], Value: resolved }); }
            });
            var yearEl = card.querySelector('.jux-rewards-year');
            if (yearEl && yearEl.value) { params.push({ Key: 'year', Value: yearEl.value }); }
            w.ExtraParams = params;
        }
    },

    seasonal: {
        render: function (self, w, i, defaultName) {
            var html = nameFieldHtml(self, w, i, defaultName);
            html += '<div class="inputContainer">';
            html += '<label class="inputLabel inputLabelUnfocused" for="seasonalTheme_' + i + '">' + self.escHtml(self.t('admin.seasonal.theme.label')) + '</label>';
            html += selectHtml(self, 'seasonalTheme_' + i, 'jux-seasonal-theme', [
                ['', self.t('admin.seasonal.theme.none')],
                ['halloween', self.t('admin.seasonal.theme.halloween')],
                ['christmas', self.t('admin.seasonal.theme.christmas')],
                ['valentines', self.t('admin.seasonal.theme.valentines')],
                ['newyear', self.t('admin.seasonal.theme.newyear')]
            ], self.getExtraParam(w, 'theme'));
            html += '</div>';
            var date = ' placeholder="MM-JJ" pattern="\\d{2}-\\d{2}"';
            html += halfInputsHtml(self, i, [
                ['seasonalStart', 'jux-seasonal-startdate', 'admin.seasonal.startdate.label', 'type="text"', self.getExtraParam(w, 'startDate'), date],
                ['seasonalEnd', 'jux-seasonal-enddate', 'admin.seasonal.enddate.label', 'type="text"', self.getExtraParam(w, 'endDate'), date]
            ]);
            html += '<div class="fieldDescription jux-seasonal-date-error" style="display:none;" id="seasonalDateError_' + i + '">' + self.escHtml(self.t('admin.seasonal.date.error')) + '</div>';
            html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.seasonal.date.help')) + '</div>';
            html += halfInputsHtml(self, i, [
                ['seasonalGenre', 'jux-seasonal-genre', 'admin.seasonal.genre.label', 'type="text"', self.getExtraParam(w, 'genre'), ''],
                ['seasonalTag', 'jux-seasonal-tag', 'admin.seasonal.tag.label', 'type="text"', self.getExtraParam(w, 'tag'), '']
            ]);
            html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.seasonal.filter.help')) + '</div>';
            return html;
        },
        read: function (self, card, w) {
            var params = [{ Key: 'value', Value: self.getExtraParam(w, 'value') }];
            var nameKey = self.getExtraParam(w, 'nameKey');
            if (nameKey) { params.push({ Key: 'nameKey', Value: nameKey }); }
            var startEl = card.querySelector('.jux-seasonal-startdate');
            var endEl = card.querySelector('.jux-seasonal-enddate');
            var themeEl = card.querySelector('.jux-seasonal-theme');
            if (startEl) { params.push({ Key: 'startDate', Value: startEl.value.trim() }); }
            if (endEl) { params.push({ Key: 'endDate', Value: endEl.value.trim() }); }
            if (themeEl && themeEl.value) { params.push({ Key: 'theme', Value: themeEl.value }); }
            ['genre', 'tag'].forEach(function (key) {
                var el = card.querySelector('.jux-seasonal-' + key);
                if (el && el.value.trim()) { params.push({ Key: key, Value: el.value.trim() }); }
            });
            w.ExtraParams = params;
        }
    },

    local: {
        render: function (self, w, i, defaultName) {
            var html = nameFieldHtml(self, w, i, defaultName);
            html += '<div class="inputContainer">';
            html += '<label class="inputLabel inputLabelUnfocused" for="localItemTypes_' + i + '">' + self.escHtml(self.t('admin.local.itemtypes.label')) + '</label>';
            html += selectHtml(self, 'localItemTypes_' + i, 'jux-local-itemtypes', LOCAL_ITEM_TYPES.map(function (opt) {
                return [opt[0], self.t(opt[1])];
            }), self.paramOrDefault(w, 'itemTypes'));
            html += '</div>';
            return html;
        },
        read: function (self, card, w) {
            var el = card.querySelector('.jux-local-itemtypes');
            if (el) { w.ExtraParams = [{ Key: 'itemTypes', Value: el.value }]; }
        }
    }
};
