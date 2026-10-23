// JellyUX Homepage configuration page controller (TODO_V4.md 6.2, F-33).
// jellyfin-web loads this file as an ES module through the data-controller attribute of
// config.html ("__plugin/juxhomepageconfigjs", served by Plugin.GetPages) and creates one controller
// per view. The pure helpers are exported for the Vitest suite.

// A Wikidata Q-id, the only form RewardsFilter.FromExtraParams accepts (QidPattern).
var QID = /^Q[1-9]\d*$/;

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
    'jux.connected.seasonal': 'seasonal',
    'jux.local.recently-released': 'local',
    'jux.local.top-rated': 'local'
};

// The item types a local library section can show (TODO_V4.md 9.1, F-61), stored as
// ExtraParams["itemTypes"]; the first one is the default of a new row.
var LOCAL_ITEM_TYPES = [
    ['Movie,Series', 'admin.local.itemtypes.both'],
    ['Movie', 'admin.local.itemtypes.movies'],
    ['Series', 'admin.local.itemtypes.series']
];

// The check mark of a native checkbox (the markup Jellyfin's emby-checkbox element draws), shared by
// the static switches of config.html and the checkboxes of the widget cards.
var CHECKBOX_OUTLINE_HTML = '<span class="checkboxOutline">' +
    '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>' +
    '<span class="material-icons checkboxIcon checkboxIcon-unchecked " aria-hidden="true"></span>' +
    '</span>';

// Adds the check mark after the label text of each static checkbox of the page, once.
function addCheckboxOutlines(root) {
    root.querySelectorAll('.emby-checkbox-label').forEach(function (label) {
        if (!label.querySelector('.checkboxOutline')) {
            label.insertAdjacentHTML('beforeend', CHECKBOX_OUTLINE_HTML);
        }
    });
}

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
function newInstanceId() {
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
function bindSearch(inputEl, options) {
    var timer = null;
    var latest = 0;
    inputEl.addEventListener('input', function () {
        clearTimeout(timer);
        var query = inputEl.value.trim();
        if (!query && !options.allowEmpty) { return; }
        timer = setTimeout(function () {
            var request = ++latest;
            ApiClient.getJSON(options.url(query)).then(function (vals) {
                var dl = document.getElementById(inputEl.getAttribute('list'));
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
    parts.push(t('admin.viewmode.' + String(widget.ViewMode || 'Portrait').toLowerCase()));
    parts.push(t('admin.summary.items', { min: widget.MinItems, max: widget.MaxItems }));
    if (widget.Enabled === false) { parts.push(t('admin.summary.disabled')); }
    return parts.join(' \u00b7 ');
}

// The three panels of the page, in tab order (ADM-01.a, D-04).
export var ADMIN_TABS = ['settings', 'sections', 'sources'];

// The tab the page opens on (DEC-P10-2): the one last open in this browser session, otherwise External
// sources when a TMDb list failed (ADM-01.d), otherwise Settings.
export function initialTab(remembered, warnings) {
    if (ADMIN_TABS.indexOf(remembered) !== -1) { return remembered; }
    return (warnings || []).some(function (code) { return code.indexOf('TMDb') === 0; }) ? 'sources' : 'settings';
}

// Maps a TMDbCacheType status entry's "Type" to its TMDbLists page-count property.
var TMDB_PAGES_CONFIG_KEY = {
    TrendingMovies: 'TrendingMoviesPages',
    TrendingShows: 'TrendingShowsPages',
    AiringToday: 'AiringTodayPages',
    TopRatedMovies: 'TopRatedMoviesPages',
    TopRatedShows: 'TopRatedShowsPages',
    NowPlayingMovies: 'NowPlayingMoviesPages'
};

// What each tab saves in a configuration, as comparable strings (ADM-01.c): the configurations compared
// both come from formConfig, so a field the page normalizes on save (Order, a trimmed name) compares equal.
// The TMDb list fields are only on the page once External sources has opened (Annexe.a): they are
// normalized here as Save writes them, so their appearance is no change.
export function tabSnapshot(config) {
    var cache = config.Cache || {};
    var lists = config.TMDbLists || {};
    return {
        settings: JSON.stringify([config.Enabled, config.WatchlistAutoRemove, config.Modules || {}, cache.SessionTtlMinutes]),
        sections: JSON.stringify(config.Widgets || []),
        sources: JSON.stringify([
            (config.ApiKeys || {}).TMDb || null,
            cache.TMDbRefreshIntervalHours,
            Object.keys(TMDB_PAGES_CONFIG_KEY).map(function (type) {
                return Math.min(5, Math.max(1, parseInt(lists[TMDB_PAGES_CONFIG_KEY[type]], 10) || 1));
            }),
            lists.NowPlayingRegion || null,
            Math.max(0, parseInt(lists.TopRatedVoteCountMin, 10) || 200)
        ])
    };
}

// The tabs whose snapshot differs from the one taken when the page was loaded or saved.
export function changedTabs(baseline, current) {
    return ADMIN_TABS.filter(function (tab) { return baseline[tab] !== current[tab]; });
}

// A Refresh now is over once every TMDb list has a newer refresh time or a newer failure than before it
// (ADM-02.a): an old failure does not count. A list TMDb answered empty without an error keeps both
// times, so polling is bounded.
export function refreshSettled(before, now) {
    var previous = {};
    (before || []).forEach(function (status) { previous[status.Type] = status; });
    var changed = function (value, was) { return !!value && value !== was; };
    return (now || []).length > 0 && now.every(function (status) {
        var was = previous[status.Type] || {};
        return changed(status.LastRefreshedUtc, was.LastRefreshedUtc) || changed(status.LastErrorUtc, was.LastErrorUtc);
    });
}

// sessionStorage can be unavailable (private window, blocked site data): the page then works without it.
function readSession(key) {
    try {
        return window.sessionStorage.getItem(key);
    } catch (e) {
        return null;
    }
}

function writeSession(key, value) {
    try {
        if (value === null) {
            window.sessionStorage.removeItem(key);
        } else {
            window.sessionStorage.setItem(key, value);
        }
        return true;
    } catch (e) {
        return false;
    }
}

var TAB_KEY = 'jux-admin-tab';
var DRAFT_KEY = 'jux-admin-draft';

// The options of a select filled from a TMDb list (genres, countries), which may still be loading or have
// failed (FIAB-10.b): the current value stays selected under its raw id until the list names it (DEC-P05-1).
function listOptionsHtml(self, emptyLabel, list, current) {
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
function nameFieldHtml(self, w, i, defaultName) {
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
function selectHtml(self, id, cls, options, current) {
    var html = '<select id="' + id + '" class="emby-select-withcolor emby-select ' + cls + '">';
    options.forEach(function (opt) {
        html += '<option value="' + opt[0] + '"' + (current === opt[0] ? ' selected' : '') + '>' + self.escHtml(opt[1]) + '</option>';
    });
    return html + '</select>';
}

// The search fields of an editor ([name, label, stored id]), each with its datalist: the label of
// a known id is shown, the id itself kept in data-value (FIAB-02.b).
function searchFieldsHtml(self, w, i, prefix, fields, labelFor) {
    var html = '';
    fields.forEach(function (field) {
        var fieldName = field[0], fieldValue = field[2];
        var currentLabel = fieldValue ? labelFor(fieldName, fieldValue) : '';
        html += '<div class="inputContainer">';
        html += '<label class="inputLabel inputLabelUnfocused" for="' + prefix + fieldName + '_' + i + '">' + self.escHtml(field[1]) + '</label>';
        html += '<input id="' + prefix + fieldName + '_' + i + '" type="text" list="' + prefix + 'Vals_' + i + '_' + fieldName + '"';
        html += ' class="emby-input jux-' + (prefix === 'disc' ? 'discover' : prefix) + '-' + fieldName + '" data-field="' + fieldName + '"';
        html += ' data-value="' + self.escHtml(fieldValue) + '"';
        html += ' placeholder="' + self.escHtml(self.t('admin.discover.search.placeholder')) + '" value="' + self.escHtml(currentLabel || fieldValue) + '" />';
        html += '<datalist id="' + prefix + 'Vals_' + i + '_' + fieldName + '"></datalist>';
        html += '</div>';
    });
    return html;
}

// Two inputs side by side: [id, class, label, attributes, value] each.
function halfInputsHtml(self, i, inputs) {
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
var DEFAULT_EDITOR = {
    render: nameFieldHtml,
    read: function () {}
};

var EDITORS = {
    admin: {
        // Value picker with datalist autocomplete.
        render: function (self, w, i, defaultName) {
            var currentValue = self.getExtraValue(w);
            var currentLabel = currentValue ? self.getLabelForValue(w.WidgetType, currentValue) : '';
            var datalistId = 'juxVals_' + i;
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
        // DisplayName already returned by GET JuxHomepage/Admin/Catalog if the key is missing.
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

        // The EDITORS entry of a row, by widget type.
        editorOf: function (widgetType) {
            return EDITORS[DEDICATED_EDITORS[widgetType] || this.editorKindOf(widgetType)] || DEFAULT_EDITOR;
        },

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
            return ['connected', 'discover', 'rewards', 'seasonal', 'local'].indexOf(this.editorKindOf(widgetType)) !== -1;
        },

        isNative: function (widgetType) {
            return this.editorKindOf(widgetType) === 'native';
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

        // Library sections whose card order is configurable; Because You Watched keeps its random
        // recommendations, native and TMDb rows their own ranking.
        isSortable: function (widgetType) {
            return this.isAdmin(widgetType) || this.isSeasonal(widgetType)
                || (this.isPersonalized(widgetType) && !this.isBecauseYouWatched(widgetType));
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

        // The field a row cannot be saved without, when it is empty: an admin row's value, a Rewards
        // row's ceremony or category as a Wikidata Q-id, the form RewardsFilter accepts (the server refuses
        // the Save otherwise, FIAB-02). Null when the row can be saved.
        requiredFieldError: function (w, i) {
            if (this.isAdmin(w.WidgetType) && !this.getExtraValue(w)) {
                return { errorId: 'juxValError_' + i, fieldId: 'widgetValue_' + i };
            }
            if (this.isRewards(w.WidgetType) && !QID.test(this.getExtraParam(w, 'ceremonyQid')) && !QID.test(this.getExtraParam(w, 'categoryQid'))) {
                return { errorId: 'juxRewardsError_' + i, fieldId: 'rewardsceremony_' + i };
            }
            return null;
        },

        // Generic ExtraParams[key] lookup, used for admin "value" and personalized settings
        // such as "excludeWatched".
        getExtraParam: function (w, key) {
            return getExtraParam(w, key);
        },

        // A parameter of the row, or its default from Admin/Catalog when the row has none (QUAL-03.c).
        paramOrDefault: function (w, key) {
            var desc = this.getDescriptor(w.WidgetType);
            return getExtraParam(w, key) || (desc && desc.DefaultRow ? getExtraParam(desc.DefaultRow, key) : '');
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
            // Wikidata's 2026 access policy requires -- see JuxHomepageController.SearchRewardsCeremony).
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
            var target = document.getElementById('juxPreview_' + index);
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
                var sortEl = card.querySelector('.jux-admin-sort');
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

        _tmdbPagesConfigKey: TMDB_PAGES_CONFIG_KEY,

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
                html += '<span id="tmdbLabel_' + s.Type + '" class="jux-admin-tmdb-label">' + self.escHtml(self.t('admin.tmdbstatus.cached', { label: label, count: s.ItemCount, matched: s.MatchedCount || 0 })) + '</span>';
                html += '<span id="tmdbRefreshed_' + s.Type + '" class="jux-admin-tmdb-refreshed">' + self.escHtml(lastRefreshed) + '</span>';
                html += '<span class="jux-admin-tmdb-controls">';
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
                    html += '<select id="selNowPlayingRegion" class="emby-select-withcolor emby-select jux-admin-tmdb-region">';
                    html += listOptionsHtml(self, self.t('admin.tmdbregion.default'), self._tmdbCountries, currentRegion);
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
                html += '</span>';
                html += '<span id="tmdbError_' + s.Type + '" class="jux-admin-field-error jux-admin-tmdb-error"' + (s.LastError ? '' : ' hidden') + '>';
                html += self.escHtml(self.tmdbErrorText(s)) + '</span>';
                html += '</div>';
                if (s.Type === 'NowPlayingMovies') {
                    html += '<div class="fieldDescription">' + self.escHtml(self.t('admin.tmdbstatus.nowplaying.help')) + '</div>';
                }
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
                var errorEl = document.getElementById('tmdbError_' + s.Type);
                if (!labelEl || !refreshedEl || !errorEl) {
                    anyMissing = true;
                    return;
                }
                var labelKey = labels[s.Type];
                var label = labelKey ? self.t(labelKey) : s.Type;
                var lastRefreshed = s.LastRefreshedUtc ? new Date(s.LastRefreshedUtc).toLocaleString() : self.t('admin.tmdbstatus.never');
                labelEl.textContent = self.t('admin.tmdbstatus.cached', { label: label, count: s.ItemCount, matched: s.MatchedCount || 0 });
                refreshedEl.textContent = lastRefreshed;
                errorEl.textContent = self.tmdbErrorText(s);
                errorEl.hidden = !s.LastError;
            });
            if (anyMissing) {
                self.renderTmdbStatus(statuses);
            }
        },

        // Why a TMDb list came back empty at its last refresh (FIAB-10.c), or '' when it did not fail.
        tmdbErrorText: function (status) {
            return status.LastError ? this.t('admin.tmdbstatus.error.' + status.LastError) : '';
        },

        // Completes the genre and region selects once their TMDb list has arrived, keeping each one's value
        // (FIAB-10.b: the page no longer waits for these lists).
        refillListSelects: function () {
            var self = this;
            document.querySelectorAll('.jux-discover-genre').forEach(function (sel) {
                sel.innerHTML = listOptionsHtml(self, self.t('admin.discover.genre.any'), self._tmdbGenres, sel.value);
            });
            var region = document.getElementById('selNowPlayingRegion');
            if (region) {
                region.innerHTML = listOptionsHtml(self, self.t('admin.tmdbregion.default'), self._tmdbCountries, region.value);
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

        // The configuration came back blank while a saved copy exists (FIAB-07): restoring it is the
        // admin's choice, never automatic. Once restored, the page is loaded again from the server.
        appendRestoreAction: function (warning) {
            var self = this;
            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'raised button-submit emby-button jux-admin-warning-restore';
            button.textContent = self.t('admin.warning.restore');
            var error = document.createElement('span');
            error.className = 'jux-admin-warning-restore-error';
            error.hidden = true;
            error.textContent = self.t('admin.warning.restore.failed');
            button.addEventListener('click', function () {
                button.disabled = true;
                error.hidden = true;
                ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('JuxHomepage/Admin/RestoreConfiguration') }).then(function () {
                    self.loadConfig();
                }).catch(function (err) {
                    console.error('[JellyUX Config] Configuration restore failed:', err);
                    button.disabled = false;
                    error.hidden = false;
                });
            });
            warning.appendChild(document.createElement('br'));
            warning.appendChild(button);
            warning.appendChild(error);
        },

        // Shows a configuration in the form. The rows are copies, so reading the form never changes the
        // configuration given here.
        populate: function (config) {
            var self = this;
            self.state.config = config;
            document.getElementById('chkEnabled').checked = !!config.Enabled;
            document.getElementById('chkWatchlistAutoRemove').checked = config.WatchlistAutoRemove !== false;
            var modules = config.Modules || {};
            document.querySelectorAll('.jux-admin-module').forEach(function (el) {
                el.checked = modules[el.getAttribute('data-module')] !== false;
            });
            document.getElementById('txtTMDb').value = (config.ApiKeys && config.ApiKeys.TMDb) || '';
            document.getElementById('numSessionTtl').value = (config.Cache && config.Cache.SessionTtlMinutes) || 15;
            document.getElementById('numTMDbRefresh').value = (config.Cache && config.Cache.TMDbRefreshIntervalHours) || 24;
            if (self._tmdbStatuses) { self.renderTmdbStatus(self._tmdbStatuses); }

            self.state.sortedWidgets = JSON.parse(JSON.stringify(config.Widgets || [])).sort(function (a, b) {
                return a.Order - b.Order;
            });
            self._expandedRows = new Set();
            self.populateAddSelect();
            self.renderWidgets();
        },

        // Records what the page shows for a configuration it just loaded or saved: the reference the
        // unsaved-changes check compares the form with (ADM-01.c).
        takeBaseline: function (config) {
            this._loaded = JSON.parse(JSON.stringify(config));
            this._baseline = tabSnapshot(this.formConfig(this._loaded, true));
            this.updateDirty(false);
        },

        // The configuration the page would save: the form read over a copy of base. Save and the
        // unsaved-changes check both use it, so they cannot disagree (ADM-01.c).
        formConfig: function (base, readRows) {
            var self = this;
            var config = JSON.parse(JSON.stringify(base || {}));
            config.Enabled = document.getElementById('chkEnabled').checked;
            config.WatchlistAutoRemove = document.getElementById('chkWatchlistAutoRemove').checked;
            if (!config.Modules) { config.Modules = {}; }
            document.querySelectorAll('.jux-admin-module').forEach(function (el) {
                config.Modules[el.getAttribute('data-module')] = el.checked;
            });
            if (!config.ApiKeys) { config.ApiKeys = {}; }
            config.ApiKeys.TMDb = document.getElementById('txtTMDb').value.trim() || null;
            if (!config.Cache) { config.Cache = {}; }
            config.Cache.SessionTtlMinutes = Math.max(1, parseInt(document.getElementById('numSessionTtl').value, 10) || 15);
            config.Cache.TMDbRefreshIntervalHours = Math.max(1, parseInt(document.getElementById('numTMDbRefresh').value, 10) || 24);

            if (!config.TMDbLists) { config.TMDbLists = {}; }
            Object.keys(self._tmdbPagesConfigKey).forEach(function (type) {
                var el = document.getElementById('numTmdbPages_' + type);
                if (el) {
                    config.TMDbLists[self._tmdbPagesConfigKey[type]] = Math.min(5, Math.max(1, parseInt(el.value, 10) || 1));
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

            if (readRows) { self.readDomIntoState(); }
            config.Widgets = self.state.sortedWidgets.map(function (w, i) {
                return toConfigRow(w, i);
            });
            return config;
        },

        // Marks the tabs whose fields differ from the baseline and says so in the Save bar (ADM-01.c).
        // readRows reads the section cards first; their editors may fill a field (a name from a picked
        // value), so it is done on change, not on every keystroke.
        updateDirty: function (readRows) {
            var self = this;
            if (!self._baseline) { return []; }
            var changed = changedTabs(self._baseline, tabSnapshot(self.formConfig(self._loaded, readRows)));
            ADMIN_TABS.forEach(function (tab) {
                document.querySelector('#juxTab_' + tab + ' .jux-admin-tab-dirty').hidden = changed.indexOf(tab) === -1;
            });
            document.getElementById('juxUnsaved').hidden = !changed.length;
            self._changedTabs = changed;
            self.syncRefreshButton();
            return changed;
        },

        // Refresh now refreshes from the saved settings: with unsaved External sources changes it would not
        // use what the admin sees, so it waits for a Save (ADM-02.a).
        syncRefreshButton: function () {
            var button = document.getElementById('juxBtnRefreshTmdb');
            var message = document.getElementById('juxTmdbRefreshMsg');
            var unsaved = (this._changedTabs || []).indexOf('sources') !== -1;
            button.disabled = unsaved || !!this._refreshing;
            if (this._refreshing) { return; }
            message.textContent = unsaved ? this.t('admin.tmdbrefresh.unsaved') : '';
            message.hidden = !unsaved;
        },

        // POST TMDb/Refresh, then TMDb/Status every refreshPollMs until refreshSettled or refreshPolls tries.
        refreshTmdb: function () {
            var self = this;
            var message = document.getElementById('juxTmdbRefreshMsg');
            var before = self._tmdbStatuses || [];
            var show = function (key) {
                message.textContent = self.t(key);
                message.hidden = false;
            };
            var finish = function (key) {
                self._refreshing = false;
                self.syncRefreshButton();
                show(key);
            };
            self._refreshing = true;
            self.syncRefreshButton();
            show('admin.tmdbrefresh.inprogress');
            ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('JuxHomepage/TMDb/Refresh') }).then(function () {
                var tries = 0;
                var poll = function () {
                    tries++;
                    ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Status')).then(function (statuses) {
                        self._tmdbStatuses = statuses || [];
                        self.updateTmdbStatusText(self._tmdbStatuses);
                        if (refreshSettled(before, self._tmdbStatuses)) {
                            self.loadWarnings();
                            finish('admin.tmdbrefresh.done');
                        } else if (tries >= self.refreshPolls) {
                            finish('admin.tmdbrefresh.slow');
                        } else {
                            setTimeout(poll, self.refreshPollMs);
                        }
                    }).catch(function (err) {
                        console.error('[JellyUX Config] TMDb status load failed:', err);
                        finish('admin.tmdbrefresh.slow');
                    });
                };
                setTimeout(poll, self.refreshPollMs);
            }).catch(function (err) {
                if (err && err.status === 409) {
                    finish('admin.tmdbrefresh.busy');
                    return;
                }
                console.error('[JellyUX Config] TMDb refresh trigger failed:', err);
                finish('admin.tmdbrefresh.failed');
            });
        },

        refreshPollMs: 2000,
        refreshPolls: 30,

        renderWarnings: function (warnings) {
            var self = this;
            var banner = document.getElementById('juxWarningBanner');
            banner.innerHTML = '';
            warnings.forEach(function (code) {
                var p = document.createElement('p');
                p.className = 'fieldDescription jux-admin-warning';
                p.textContent = self.t('admin.warning.' + code);
                if (code === 'ConfigurationReset') { self.appendRestoreAction(p); }
                banner.appendChild(p);
            });
            banner.hidden = !warnings.length;
        },

        // After a refresh the TMDb warnings may be gone: the banner is read again.
        loadWarnings: function () {
            var self = this;
            return ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/Admin/Warnings')).then(function (warnings) {
                self.renderWarnings(warnings || []);
            }).catch(function (err) {
                console.error('[JellyUX Config] Warnings load failed:', err);
            });
        },

        selectTab: function (name, focus) {
            ADMIN_TABS.forEach(function (tab) {
                var button = document.getElementById('juxTab_' + tab);
                var selected = tab === name;
                button.setAttribute('aria-selected', selected ? 'true' : 'false');
                button.tabIndex = selected ? 0 : -1;
                document.getElementById('juxPanel_' + tab).hidden = !selected;
            });
            if (focus) { document.getElementById('juxTab_' + name).focus(); }
            writeSession(TAB_KEY, name);
            if (name === 'sources') { this.loadSources(); }
        },

        // External sources reads the TMDb status and the country list the first time it opens in a page view
        // (Annexe.a, DEC-P10-1); the TMDb alert comes from Admin/Warnings, read at load.
        loadSources: function () {
            var self = this;
            if (!self._sourcesLoad) {
                var status = ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Status')).catch(function () {
                    return [];
                }).then(function (statuses) {
                    self._tmdbStatuses = statuses || [];
                    self.renderTmdbStatus(self._tmdbStatuses);
                    self.updateDirty(false);
                });
                // The countries complete the region select when they come, without holding the status back.
                var countries = self.loadTmdbCountries();
                self._sourcesLoad = Promise.all([status, countries]).then(function () {
                    self.refillListSelects();
                });
            }
            return self._sourcesLoad;
        },

        // Opens the tab of a field before it is focused: focus() does nothing under a hidden panel (ADM-01.b).
        revealField: function (el) {
            var panel = el && el.closest('[role="tabpanel"]');
            if (panel && panel.hidden) { this.selectTab(panel.id.replace('juxPanel_', '')); }
        },

        // Leaving the page cannot be stopped from inside Jellyfin (DEC-P10-5): the unsaved changes are kept
        // for this browser session and shown again on return.
        leave: function () {
            this._shown = false;
            if (!this._baseline) { return; }
            var changed = this.updateDirty(true);
            writeSession(DRAFT_KEY, changed.length
                ? JSON.stringify({ base: JSON.stringify(this._baseline), config: this.formConfig(this._loaded, false) })
                : null);
        },

        // Shows again the changes kept by leave(), unless the saved configuration has changed since.
        restoreDraft: function () {
            var notice = document.getElementById('juxDraftNotice');
            var draft = null;
            try {
                draft = JSON.parse(readSession(DRAFT_KEY) || 'null');
            } catch (e) {
                draft = null;
            }
            writeSession(DRAFT_KEY, null);
            if (!draft || !draft.config || draft.base !== JSON.stringify(this._baseline)) {
                notice.hidden = true;
                return;
            }
            this.populate(draft.config);
            this.updateDirty(true);
            notice.hidden = false;
        },

        discardDraft: function () {
            document.getElementById('juxDraftNotice').hidden = true;
            this.populate(JSON.parse(JSON.stringify(this._loaded)));
            this.updateDirty(true);
        },

        loadConfig: function () {
            var self = this;
            Dashboard.showLoadingMsg();
            // Asked at once but not awaited: TMDb can take up to the lookup deadline to answer (FIAB-10.b). The
            // TMDb status and countries wait for External sources to open (loadSources).
            var genres = self.loadTmdbGenres();
            self._sourcesLoad = null;
            self._tmdbStatuses = null;
            document.getElementById('juxTmdbStatusList').innerHTML = '';
            Promise.all([
                ApiClient.getPluginConfiguration(self.PLUGIN_ID),
                ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/Admin/Catalog')),
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

                var warnings = results[3] || [];
                self.renderWarnings(warnings);

                self.populate(results[0]);
                self.takeBaseline(results[0]);
                self.restoreDraft();
                self.selectTab(initialTab(readSession(TAB_KEY), warnings));

                Dashboard.hideLoadingMsg();
                genres.then(function () { self.refillListSelects(); });
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
                tmdbError.hidden = false;
                self.revealField(document.getElementById('txtTMDb'));
                document.getElementById('txtTMDb').focus();
                return;
            }
            tmdbError.hidden = true;

            self.readDomIntoState();

            // Validate: admin sections must have a value, Rewards sections a ceremony or a category.
            var hasValueError = false;
            var firstErrorField = null;
            var list = document.getElementById('juxWidgetList');
            self.state.sortedWidgets.forEach(function (w, i) {
                var error = self.requiredFieldError(w, i);
                if (!error) {
                    ['#juxValError_' + i, '#juxRewardsError_' + i].forEach(function (id) {
                        var errEl = list ? list.querySelector(id) : null;
                        if (errEl) { errEl.style.display = 'none'; }
                    });
                    return;
                }
                if (!hasValueError) { firstErrorField = error.fieldId; }
                hasValueError = true;
                self._expandedRows.add(w);
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
                    var error = self.requiredFieldError(w, i);
                    var err = error ? document.getElementById(error.errorId) : null;
                    if (err) { err.style.display = ''; }
                });
                var firstField = document.getElementById(firstErrorField);
                if (firstField) {
                    self.revealField(firstField);
                    firstField.focus();
                }
                return;
            }

            // A Save the server refuses (400 from validation, 500) used to fail in the console only (R11).
            var saveError = document.getElementById('juxSaveError');
            if (saveError) { saveError.hidden = true; }
            Dashboard.showLoadingMsg();

            ApiClient.getPluginConfiguration(self.PLUGIN_ID).then(function (stored) {
                var config = self.formConfig(stored, false);
                return ApiClient.updatePluginConfiguration(self.PLUGIN_ID, config).then(function (result) {
                    // Keep in-memory state honest with what was just actually persisted, so
                    // that any later render (e.g. a background status poll falling back to a
                    // full re-render because a row is missing) reflects real saved values
                    // instead of what was loaded at the start of this page view.
                    self.state.config = config;
                    self.takeBaseline(config);
                    document.getElementById('juxDraftNotice').hidden = true;
                    return result;
                });
            }).then(function (result) {
                Dashboard.hideLoadingMsg();
                Dashboard.processPluginConfigurationUpdateResult(result);
            }).catch(function (err) {
                console.error('[JellyUX Config] Save failed:', err);
                Dashboard.hideLoadingMsg();
                if (saveError) { saveError.hidden = false; }
            });
        },

        init: function () {
            var self = this;
            if (!this._submitBound) {
                addCheckboxOutlines(document.querySelector('.juxConfigForm'));

                var form = document.querySelector('.juxConfigForm');
                form.addEventListener('submit', function (e) {
                    self.saveConfig(e);
                });
                form.addEventListener('input', function (e) {
                    if (!e.target.closest('#juxWidgetList')) { self.updateDirty(false); }
                });
                form.addEventListener('change', function () {
                    self.updateDirty(true);
                });
                // Closing or reloading the browser tab can still be stopped (DEC-P10-5).
                window.addEventListener('beforeunload', function (e) {
                    if (self._shown && self._changedTabs && self._changedTabs.length) {
                        e.preventDefault();
                        e.returnValue = '';
                    }
                });
                document.getElementById('juxBtnDiscardDraft').addEventListener('click', function () {
                    self.discardDraft();
                });

                // WAI-ARIA Tabs: the arrows, Home and End move between tabs and open them (ADM-01.a).
                var tablist = document.querySelector('.jux-admin-tabs');
                tablist.addEventListener('click', function (e) {
                    var tab = e.target.closest('[role="tab"]');
                    if (tab) { self.selectTab(tab.getAttribute('data-tab')); }
                });
                tablist.addEventListener('keydown', function (e) {
                    var current = ADMIN_TABS.indexOf(document.activeElement.getAttribute('data-tab'));
                    var next = { ArrowRight: current + 1, ArrowLeft: current - 1, Home: 0, End: ADMIN_TABS.length - 1 }[e.key];
                    if (current === -1 || next === undefined) { return; }
                    e.preventDefault();
                    self.selectTab(ADMIN_TABS[(next + ADMIN_TABS.length) % ADMIN_TABS.length], true);
                });

                var addBtn = document.getElementById('juxBtnAddWidget');
                if (addBtn) {
                    addBtn.addEventListener('click', function () {
                        var sel = document.getElementById('juxAddWidgetType');
                        var errEl = document.getElementById('juxAddWidgetError');
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
                        var added = document.querySelector('.jux-admin-widget-card[data-index="' + (self.state.sortedWidgets.length - 1) + '"]');
                        if (added && added.scrollIntoView) { added.scrollIntoView({ block: 'center' }); }
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

                document.getElementById('juxBtnRefreshTmdb').addEventListener('click', function () {
                    self.refreshTmdb();
                });

                this._submitBound = true;
            }
            this._shown = true;
            this.loadConfig();
        }
    };
}

export default function (view) {
    var page = createConfigPage();
    view.addEventListener('viewshow', function () {
        page.init();
    });
    view.addEventListener('viewbeforehide', function () {
        page.leave();
    });
}
