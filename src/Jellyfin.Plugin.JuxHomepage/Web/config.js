// JellyUX Homepage configuration page controller (TODO_V4.md 6.2, F-33).
// jellyfin-web loads this file as an ES module through the data-controller attribute of
// config.html ("__plugin/juxhomepageconfigjs", served by Plugin.GetPages) and creates one controller
// per view. The pure helpers are exported for the Vitest suite.

import { escHtml, getExtraParam, DEDICATED_EDITORS, addCheckboxOutlines, editorKind, DEFAULT_EDITOR, EDITORS } from './configurationpage?name=juxhomepageconfigeditorsjs';
import { TMDB_PAGES_CONFIG_KEY, voteCountMin, refreshSettled, sourcesTab } from './configurationpage?name=juxhomepageconfigsourcesjs';
import { moveRow, dropIndex, SORT_OPTIONS, sortOption, FEATURED_MIN_RATINGS, toConfigRow, previewHtml, widgetSummary, sectionsTab } from './configurationpage?name=juxhomepageconfigsectionsjs';

export { escHtml, getExtraParam, editorKind, refreshSettled, moveRow, dropIndex, SORT_OPTIONS, sortOption, FEATURED_MIN_RATINGS, toConfigRow, previewHtml, widgetSummary };

// A Wikidata Q-id, the only form RewardsFilter.FromExtraParams accepts (QidPattern).
var QID = /^Q[1-9]\d*$/;

// A TMDb key is optional; when set, it is a 32-character hexadecimal v3 key or a v4 bearer token.
export function validateTmdb(value) {
    if (!value || value.trim() === '') { return true; }
    var v = value.trim();
    if (/^[a-f0-9]{32}$/i.test(v)) { return true; }
    if (/^ey[\w-]+\.[\w-]+\.[\w-]+$/.test(v)) { return true; }
    return false;
}

// The three panels of the page, in tab order (ADM-01.a, D-04).
export var ADMIN_TABS = ['settings', 'sections', 'sources'];

// The tab the page opens on (DEC-P10-2): the one last open in this browser session, otherwise External
// sources when a TMDb list failed (ADM-01.d), otherwise Settings.
export function initialTab(remembered, warnings) {
    if (ADMIN_TABS.indexOf(remembered) !== -1) { return remembered; }
    return (warnings || []).some(function (code) { return code.indexOf('TMDb') === 0; }) ? 'sources' : 'settings';
}





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
            voteCountMin(lists.TopRatedVoteCountMin)
        ])
    };
}

// The tabs whose snapshot differs from the one taken when the page was loaded or saved.
export function changedTabs(baseline, current) {
    return ADMIN_TABS.filter(function (tab) { return baseline[tab] !== current[tab]; });
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

// How many admin page views this module built, for ids that must stay unique in the document.
var configPageCount = 0;

function createConfigPage(view) {
    // The element of this page's own view: jellyfin-web keeps an earlier view of the same page hidden in the
    // document when the admin page opens under a second URL (A-13), with the same element ids.
    function byId(id) {
        return view.querySelector('[id="' + id + '"]');
    }

    return Object.assign({
        PLUGIN_ID: '3adf1f1f-1541-4e47-b9e3-34d2d2968af6',
        _submitBound: false,
        // Tells this page's datalists apart from those of a hidden earlier view of the page (A-13).
        _listKey: String(++configPageCount),
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
            view.querySelectorAll('[data-i18n]').forEach(function (el) {
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
        // A row of this type may show as a banner (KTW-01): Admin/Catalog says false for the native-route
        // sections and My Media (DEC-P11-4), nothing for the others.
        canFeature: function (widgetType) {
            var desc = this.getDescriptor(widgetType);
            return !!desc && desc.CanFeature !== false;
        },

        isSortable: function (widgetType) {
            return this.isAdmin(widgetType) || this.isSeasonal(widgetType)
                || (this.isPersonalized(widgetType) && !this.isBecauseYouWatched(widgetType));
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
            byId('chkEnabled').checked = !!config.Enabled;
            byId('chkWatchlistAutoRemove').checked = config.WatchlistAutoRemove !== false;
            var modules = config.Modules || {};
            view.querySelectorAll('.jux-admin-module').forEach(function (el) {
                el.checked = modules[el.getAttribute('data-module')] !== false;
            });
            byId('numSessionTtl').value = (config.Cache && config.Cache.SessionTtlMinutes) || 15;
            self.fillSources(config);

            self.state.sortedWidgets = JSON.parse(JSON.stringify(config.Widgets || [])).sort(function (a, b) {
                return a.Order - b.Order;
            });
            self._expandedRows = new Set();
            self.populateAddSelect();
            self.renderWidgets();
        },

        // Records what the page shows for a configuration it just loaded, or the one it just saved: the
        // reference the unsaved-changes check compares the form with (ADM-01.c). A saved one is the posted
        // configuration itself, not the form, which the admin may have changed during the Save (R11).
        takeBaseline: function (config, saved) {
            this._loaded = JSON.parse(JSON.stringify(config));
            this._baseline = tabSnapshot(saved ? this._loaded : this.formConfig(this._loaded, true));
            this.updateDirty(false);
        },

        // The configuration the page would save: the form read over a copy of base. Save and the
        // unsaved-changes check both use it, so they cannot disagree (ADM-01.c).
        formConfig: function (base, readRows) {
            var self = this;
            var config = JSON.parse(JSON.stringify(base || {}));
            config.Enabled = byId('chkEnabled').checked;
            config.WatchlistAutoRemove = byId('chkWatchlistAutoRemove').checked;
            if (!config.Modules) { config.Modules = {}; }
            view.querySelectorAll('.jux-admin-module').forEach(function (el) {
                config.Modules[el.getAttribute('data-module')] = el.checked;
            });
            if (!config.Cache) { config.Cache = {}; }
            config.Cache.SessionTtlMinutes = Math.max(1, parseInt(byId('numSessionTtl').value, 10) || 15);
            self.readSources(config);

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
                view.querySelector('#juxTab_' + tab + ' .jux-admin-tab-dirty').hidden = changed.indexOf(tab) === -1;
            });
            byId('juxUnsaved').hidden = !changed.length;
            self._changedTabs = changed;
            self.syncRefreshButton();
            return changed;
        },








        renderWarnings: function (warnings) {
            var self = this;
            var banner = byId('juxWarningBanner');
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
                var button = byId('juxTab_' + tab);
                var selected = tab === name;
                button.setAttribute('aria-selected', selected ? 'true' : 'false');
                button.tabIndex = selected ? 0 : -1;
                byId('juxPanel_' + tab).hidden = !selected;
            });
            if (focus) { byId('juxTab_' + name).focus(); }
            writeSession(TAB_KEY, name);
            if (name === 'sources') { this.loadSources(); }
        },



        // Opens the tab of a field before it is focused: focus() does nothing under a hidden panel (ADM-01.b).
        revealField: function (el) {
            var panel = el && el.closest('[role="tabpanel"]');
            if (panel && panel.hidden) { this.selectTab(panel.id.replace('juxPanel_', '')); }
        },

        // The first field the browser would refuse (min, max, pattern), brought into view: its tab open and
        // its section card unfolded, the state read from the page first so the card renders what was typed.
        revealInvalidField: function (form) {
            var field = Array.prototype.find.call(form.elements, function (el) {
                return el.willValidate && !el.checkValidity();
            });
            if (!field) { return null; }
            var card = field.closest('.jux-admin-widget-card-collapsed');
            if (card) {
                var index = card.getAttribute('data-index');
                var position = Array.prototype.indexOf.call(card.querySelectorAll('input, select, textarea'), field);
                this._expandedRows.add(this.state.sortedWidgets[index]);
                this.renderWidgets();
                card = view.querySelector('#juxWidgetList .jux-admin-widget-card[data-index="' + index + '"]');
                field = (field.id && byId(field.id)) ||
                    card.querySelectorAll('input, select, textarea')[position];
            }
            this.revealField(field);
            return field;
        },

        // Leaving the page cannot be stopped from inside Jellyfin (DEC-P10-5): the unsaved changes are kept
        // for this browser session and shown again on return.
        leave: function () {
            this._shown = false;
            if (!this._baseline) { return; }
            this.updateDirty(true);
            // The TMDb key, masked on the page, is not written to sessionStorage: a changed key is not kept (R11).
            var draft = this.formConfig(this._loaded, false);
            draft.ApiKeys = JSON.parse(JSON.stringify(this._loaded.ApiKeys || {}));
            writeSession(DRAFT_KEY, changedTabs(this._baseline, tabSnapshot(draft)).length
                ? JSON.stringify({ base: JSON.stringify(this._baseline), config: draft })
                : null);
        },

        // Shows again the changes kept by leave(), unless the saved configuration has changed since.
        restoreDraft: function () {
            var notice = byId('juxDraftNotice');
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
            byId('juxDraftNotice').hidden = true;
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
            byId('juxTmdbStatusList').innerHTML = '';
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

            var tmdbVal = byId('txtTMDb').value.trim();
            var tmdbError = byId('juxTmdbError');
            if (!self.validateTmdb(tmdbVal)) {
                tmdbError.hidden = false;
                self.revealField(byId('txtTMDb'));
                byId('txtTMDb').focus();
                return;
            }
            tmdbError.hidden = true;

            self.readDomIntoState();

            // The form is novalidate (DR-018): the browser's own check stops a Save without a word when the
            // field it refuses sits in a hidden tab or a folded card, so the page brings it into view first.
            var invalidField = self.revealInvalidField(view.querySelector('.juxConfigForm'));
            if (invalidField) {
                invalidField.reportValidity();
                return;
            }

            // Validate: admin sections must have a value, Rewards sections a ceremony or a category.
            var hasValueError = false;
            var firstErrorField = null;
            var list = byId('juxWidgetList');
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
            // start/end date is empty or not a real day: unlike Admin's "Value" field, such a date
            // is not a broken configuration -- SeasonalWidget itself already treats it as "never
            // active" and the section is generically excluded from the home screen (same
            // MinItems=0-items mechanism as any other widget with no results), so saving is still
            // allowed and the admin can fill in the dates afterward. A date typed outside the MM-JJ
            // form is a typing slip the field's pattern refuses: revealInvalidField above brings it
            // into view before this point (DR-018).
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
                    var err = error ? byId(error.errorId) : null;
                    if (err) { err.style.display = ''; }
                });
                var firstField = byId(firstErrorField);
                if (firstField) {
                    self.revealField(firstField);
                    firstField.focus();
                }
                return;
            }

            // A Save the server refuses (400 from validation, 500) used to fail in the console only (R11).
            var saveError = byId('juxSaveError');
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
                    self.takeBaseline(config, true);
                    byId('juxDraftNotice').hidden = true;
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
                addCheckboxOutlines(view.querySelector('.juxConfigForm'));

                var form = view.querySelector('.juxConfigForm');
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
                    // A section field typed into has no change event until it loses focus: read the rows now (R11).
                    if (self._shown && self.updateDirty(true).length) {
                        e.preventDefault();
                        e.returnValue = '';
                    }
                });
                byId('juxBtnDiscardDraft').addEventListener('click', function () {
                    self.discardDraft();
                });

                // WAI-ARIA Tabs: the arrows, Home and End move between tabs and open them (ADM-01.a).
                var tablist = view.querySelector('.jux-admin-tabs');
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

                self.bindSections();
                self.bindSources();

                this._submitBound = true;
            }
            this._shown = true;
            this.loadConfig();
        }
    }, sourcesTab(view, byId), sectionsTab(view, byId));
}

export default function (view) {
    var page = createConfigPage(view);
    view.addEventListener('viewshow', function () {
        page.init();
    });
    view.addEventListener('viewbeforehide', function () {
        page.leave();
    });
}
