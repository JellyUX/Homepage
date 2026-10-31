// JellyUX Homepage admin page: the External sources tab (TMDb key, refresh, list settings and status).
// Imported by config.js as ./configurationpage?name=juxhomepageconfigsourcesjs, a plugin page of its own
// (Plugin.GetPages), so it is served like config.js, without cache headers.

import { escHtml, listOptionsHtml } from './configurationpage?name=juxhomepageconfigeditorsjs';

// Maps a TMDbCacheType status entry's "Type" to its TMDbLists page-count property.
export var TMDB_PAGES_CONFIG_KEY = {
    TrendingMovies: 'TrendingMoviesPages',
    TrendingShows: 'TrendingShowsPages',
    AiringToday: 'AiringTodayPages',
    TopRatedMovies: 'TopRatedMoviesPages',
    TopRatedShows: 'TopRatedShowsPages',
    NowPlayingMovies: 'NowPlayingMoviesPages'
};

// TopRatedVoteCountMin as the page shows and saves it: 0 is a valid minimum, the server default (200)
// only stands in for an empty or unreadable field (A-14).
export function voteCountMin(value) {
    var count = parseInt(value, 10);
    return isNaN(count) ? 200 : Math.max(0, count);
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

// The External sources tab's members, merged into the page object: they read their elements in its view.
export function sourcesTab(view, byId) {
    return {
        // Shows the tab's fields of a configuration (populate).
        fillSources: function (config) {
            byId('txtTMDb').value = (config.ApiKeys && config.ApiKeys.TMDb) || '';
            byId('numTMDbRefresh').value = (config.Cache && config.Cache.TMDbRefreshIntervalHours) || 24;
            if (this._tmdbStatuses) { this.renderTmdbStatus(this._tmdbStatuses); }
        },

        // Reads the tab's fields into the configuration the page would save (formConfig).
        readSources: function (config) {
            var self = this;
            if (!config.ApiKeys) { config.ApiKeys = {}; }
            config.ApiKeys.TMDb = byId('txtTMDb').value.trim() || null;
            if (!config.Cache) { config.Cache = {}; }
            config.Cache.TMDbRefreshIntervalHours = Math.max(1, parseInt(byId('numTMDbRefresh').value, 10) || 24);

            if (!config.TMDbLists) { config.TMDbLists = {}; }
            // A TMDb list field not on the page yet (External sources unopened, Annexe.a) keeps the value the
            // form shows, a restored draft's included (R11), not the one of base.
            var shown = (self.state.config && self.state.config.TMDbLists) || {};
            var listField = function (id, key, read) {
                var el = byId(id);
                if (el) {
                    config.TMDbLists[key] = read(el.value);
                } else if (Object.prototype.hasOwnProperty.call(shown, key)) {
                    config.TMDbLists[key] = shown[key];
                }
            };
            Object.keys(self._tmdbPagesConfigKey).forEach(function (type) {
                listField('numTmdbPages_' + type, self._tmdbPagesConfigKey[type], function (value) {
                    return Math.min(5, Math.max(1, parseInt(value, 10) || 1));
                });
            });
            listField('selNowPlayingRegion', 'NowPlayingRegion', function (value) { return value || null; });
            listField('numTopRatedVoteCountMin', 'TopRatedVoteCountMin', voteCountMin);
        },

        // Binds the tab's buttons, once per view (init).
        bindSources: function () {
            var self = this;
            // The TMDb key is masked like a password; the eye button shows it for checking.
            var showTmdbBtn = byId('juxBtnShowTmdb');
            if (showTmdbBtn) {
                var syncShowTmdb = function () {
                    var shown = byId('txtTMDb').type === 'text';
                    var label = self.t(shown ? 'admin.apikeys.tmdb.hide' : 'admin.apikeys.tmdb.show');
                    showTmdbBtn.setAttribute('aria-pressed', shown ? 'true' : 'false');
                    showTmdbBtn.setAttribute('aria-label', label);
                    showTmdbBtn.title = label;
                    showTmdbBtn.querySelector('.material-icons').textContent = shown ? 'visibility_off' : 'visibility';
                };
                showTmdbBtn.addEventListener('click', function () {
                    var input = byId('txtTMDb');
                    input.type = input.type === 'password' ? 'text' : 'password';
                    syncShowTmdb();
                });
                self._syncShowTmdb = syncShowTmdb;
            }

            byId('juxBtnRefreshTmdb').addEventListener('click', function () {
                self.refreshTmdb();
            });
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
            var list = byId('juxTmdbStatusList');
            if (!list) { return; }
            var labels = self._tmdbStatusLabels;
            var tmdbLists = (self.state.config && self.state.config.TMDbLists) || {};
            var currentRegion = tmdbLists.NowPlayingRegion || '';
            var currentVoteCountMin = voteCountMin(tmdbLists.TopRatedVoteCountMin);
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
                var labelEl = byId('tmdbLabel_' + s.Type);
                var refreshedEl = byId('tmdbRefreshed_' + s.Type);
                var errorEl = byId('tmdbError_' + s.Type);
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
            view.querySelectorAll('.jux-discover-genre').forEach(function (sel) {
                sel.innerHTML = listOptionsHtml(self, self.t('admin.discover.genre.any'), self._tmdbGenres, sel.value);
            });
            var region = byId('selNowPlayingRegion');
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

        // Refresh now refreshes from the saved settings: with unsaved External sources changes it would not
        // use what the admin sees, so it waits for a Save (ADM-02.a).
        syncRefreshButton: function () {
            var button = byId('juxBtnRefreshTmdb');
            var message = byId('juxTmdbRefreshMsg');
            var unsaved = (this._changedTabs || []).indexOf('sources') !== -1;
            button.disabled = unsaved || !!this._refreshing;
            if (this._refreshing) { return; }
            // Only its own message is cleared: the outcome of the last refresh stays until the next one (R11).
            if (unsaved) {
                message.textContent = this.t('admin.tmdbrefresh.unsaved');
                message.hidden = false;
                this._unsavedShown = true;
            } else if (this._unsavedShown) {
                message.textContent = '';
                message.hidden = true;
                this._unsavedShown = false;
            }
        },

        // POST TMDb/Refresh, then TMDb/Status every refreshPollMs until refreshSettled or refreshPolls tries.
        refreshTmdb: function () {
            var self = this;
            var message = byId('juxTmdbRefreshMsg');
            // Without a saved TMDb key no TMDb list can change: the refresh only redoes Rewards, which has no
            // status to follow (bench 8098: 30 polls, then "taking longer").
            var tmdbKey = !!((self._loaded || {}).ApiKeys || {}).TMDb;
            var show = function (key) {
                message.textContent = self.t(key);
                message.hidden = false;
                self._unsavedShown = false;
            };
            var finish = function (key) {
                self._refreshing = false;
                self.syncRefreshButton();
                show(key);
            };
            self._refreshing = true;
            self.syncRefreshButton();
            show('admin.tmdbrefresh.inprogress');
            // The statuses from before the refresh are what each list must move past: read them first when
            // External sources has not loaded them yet, or the first poll would count every list as done (R11).
            var statusBefore = self._tmdbStatuses
                ? Promise.resolve(self._tmdbStatuses)
                : ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Status')).catch(function () { return []; });
            var before = [];
            statusBefore.then(function (statuses) {
                before = statuses || [];
                return ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('JuxHomepage/TMDb/Refresh') });
            }).then(function () {
                var tries = 0;
                var poll = function () {
                    tries++;
                    ApiClient.getJSON(ApiClient.getUrl('JuxHomepage/TMDb/Status')).then(function (statuses) {
                        self._tmdbStatuses = statuses || [];
                        self.updateTmdbStatusText(self._tmdbStatuses);
                        if (!tmdbKey || refreshSettled(before, self._tmdbStatuses)) {
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
        }
    };
}
