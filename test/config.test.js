import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import createController, { changedTabs, dropIndex, editorKind, escHtml, getExtraParam, initialTab, moveRow, previewHtml, sortOption, tabSnapshot, toConfigRow, validateTmdb, widgetSummary } from '../src/Jellyfin.Plugin.JuxHomepage/Web/config.js';

const CONFIG_HTML = readFileSync(resolve(__dirname, '../src/Jellyfin.Plugin.JuxHomepage/Web/config.html'), 'utf8');

describe('error colors', () => {
    // Jellyfin's theme sets .fieldDescription to a grey loaded after the plugin's styles: an error class
    // on a .fieldDescription element shows grey unless its rule also names .fieldDescription (FIAB-02, FIAB-03).
    const css = (file) => readFileSync(resolve(__dirname, '../src/Jellyfin.Plugin.JuxHomepage/Web', file), 'utf8');

    it.each([
        ['config.css', 'jux-admin-warning'],
        ['config.css', 'jux-admin-field-error'],
        ['config.css', 'jux-admin-value-error'],
        ['jux-homepage.css', 'jux-user-settings-error']
    ])('%s colors .fieldDescription.%s as an error', (file, cls) => {
        const rule = new RegExp(`\\.fieldDescription\\.${cls}\\b[^{]*\\{[^}]*color:\\s*var\\(--error-color`);
        expect(css(file)).toMatch(rule);
    });
});

describe('moveRow', () => {
    const rows = ['a', 'b', 'c', 'd'];

    it('swaps a row with its upper or lower neighbour', () => {
        expect(moveRow(rows, 2, 1)).toEqual(['a', 'c', 'b', 'd']);
        expect(moveRow(rows, 1, 2)).toEqual(['a', 'c', 'b', 'd']);
    });

    it('moves the last row to the first place in one step', () => {
        expect(moveRow(rows, 3, 0)).toEqual(['d', 'a', 'b', 'c']);
    });

    it('clamps the target to the list and ignores an index outside it', () => {
        expect(moveRow(rows, 0, -1)).toEqual(rows);
        expect(moveRow(rows, 3, 4)).toEqual(rows);
        expect(moveRow(rows, 7, 0)).toEqual(rows);
    });

    it('leaves the input list untouched', () => {
        moveRow(rows, 3, 0);
        expect(rows).toEqual(['a', 'b', 'c', 'd']);
    });
});

describe('editorKind (F-37)', () => {
    const index = { Native: 0, Admin: 1, Personalized: 2, Connected: 3 };

    it('gives the widget types with an editor of their own that editor, whatever their category', () => {
        expect(editorKind('jux.connected.seasonal', 1, index)).toBe('seasonal');
        expect(editorKind('jux.connected.discover-movies', 3, index)).toBe('discover');
        expect(editorKind('jux.connected.rewards', 3, index)).toBe('rewards');
    });

    it('falls back to the editor of the category, by index or by name', () => {
        expect(editorKind('jux.admin.genre', 1, index)).toBe('admin');
        expect(editorKind('jux.native.next-up', 0, index)).toBe('native');
        expect(editorKind('jux.personalized.favorite-genre', 'Personalized', index)).toBe('personalized');
        expect(editorKind('jux.connected.trending-movies', 3, index)).toBe('connected');
        expect(editorKind('jux.unknown', 9, index)).toBe('');
    });
});

describe('previewHtml (F-62)', () => {
    const t = (key, params) => key + (params ? JSON.stringify(params) : '');
    const poster = (item) => (item.Id === 'a' ? '/img/a' : '');

    it('lists each item with its poster, and a placeholder when it has none', () => {
        const html = previewHtml({ TotalRecordCount: 2, MinItems: 1, Items: [{ Id: 'a', Name: 'Alien' }, { Id: 'b', Name: '<b>B</b>' }] }, poster, t);
        const doc = new DOMParser().parseFromString(html, 'text/html');
        expect(doc.querySelector('.jux-admin-preview-count').textContent).toBe('admin.preview.count{"count":2}');
        expect([...doc.querySelectorAll('.jux-admin-preview-name')].map((n) => n.textContent)).toEqual(['Alien', '<b>B</b>']);
        expect(doc.querySelector('img.jux-admin-preview-poster').getAttribute('src')).toBe('/img/a');
        expect(doc.querySelectorAll('.jux-admin-preview-noposter')).toHaveLength(1);
        expect(doc.querySelector('.jux-admin-preview-hidden')).toBeNull();
    });

    it('says why the home screen would hide the section, and when nothing matches', () => {
        const text = (html) => new DOMParser().parseFromString(html, 'text/html').body.textContent;
        const below = previewHtml({ TotalRecordCount: 1, MinItems: 4, HiddenReason: 'MinItems', Items: [{ Id: 'a', Name: 'Alien' }] }, poster, t);
        expect(text(below)).toContain('admin.preview.hidden.minitems{"min":4}');
        const none = previewHtml({ TotalRecordCount: 0, MinItems: 1, HiddenReason: 'NoValue', Items: [] }, poster, t);
        expect(text(none)).toContain('admin.preview.hidden.novalue');
        expect(text(none)).toContain('admin.preview.empty');
    });
});

describe('toConfigRow', () => {
    it('clamps the item range of a row and orders it by its place', () => {
        const row = toConfigRow({ WidgetType: 'jux.admin.genre', Enabled: true, MinItems: -2, MaxItems: 0 }, 3);
        expect(row).toMatchObject({ Order: 30, MinItems: 0, MaxItems: 1, ExtraParams: [], CustomDisplayName: null });
    });

    it('saves the frozen per-user override settings as they were loaded, without writing them (QUAL-02)', () => {
        const loaded = { WidgetType: 'jux.admin.genre', AllowUserOverride: true, MinInstances: 1, MaxInstances: 5 };
        expect(toConfigRow(loaded, 0)).toMatchObject({ AllowUserOverride: true, MinInstances: 1, MaxInstances: 5 });
        expect(toConfigRow({ WidgetType: 'jux.admin.genre' }, 0)).not.toHaveProperty('AllowUserOverride');
    });
});

describe('sortOption (F-53)', () => {
    it('keeps a known order, ignores the direction of Random and falls back to the name', () => {
        expect(toConfigRow({ SortBy: 'DateCreated', SortOrder: 'Descending' }, 0)).toMatchObject({ SortBy: 'DateCreated', SortOrder: 'Descending' });
        expect(sortOption({ SortBy: 'Random', SortOrder: 'Descending' })[2]).toBe('admin.sort.random');
        expect(toConfigRow({ SortBy: 'Bogus' }, 0)).toMatchObject({ SortBy: 'SortName', SortOrder: 'Ascending' });
        expect(toConfigRow({}, 0)).toMatchObject({ SortBy: 'SortName', SortOrder: 'Ascending' });
    });
});

describe('dropIndex', () => {
    it('lands a dragged row above or below the row it is dropped on', () => {
        const rows = ['a', 'b', 'c', 'd'];
        expect(moveRow(rows, 3, dropIndex(3, 0, false))).toEqual(['d', 'a', 'b', 'c']);
        expect(moveRow(rows, 0, dropIndex(0, 3, true))).toEqual(['b', 'c', 'd', 'a']);
        expect(moveRow(rows, 0, dropIndex(0, 2, false))).toEqual(['b', 'a', 'c', 'd']);
        expect(moveRow(rows, 3, dropIndex(3, 1, true))).toEqual(['a', 'b', 'd', 'c']);
        expect(moveRow(rows, 1, dropIndex(1, 1, true))).toEqual(rows);
    });
});

describe('widgetSummary (F-50)', () => {
    const t = (key, params) => key + (params ? JSON.stringify(params) : '');

    it('lists the name, the value, the date window, the view mode and the item range', () => {
        const seasonal = { CustomDisplayName: 'Halloween', ViewMode: 'Landscape', MinItems: 1, MaxItems: 20, ExtraParams: [{ Key: 'startDate', Value: '10-01' }, { Key: 'endDate', Value: '10-31' }] };
        expect(widgetSummary(seasonal, '', t)).toBe(
            'Halloween · admin.summary.window{"start":"10-01","end":"10-31"} · admin.viewmode.landscape · admin.summary.items{"min":1,"max":20}');
    });

    it('shows a value equal to the name once, and flags a disabled row', () => {
        const genre = { CustomDisplayName: 'Drame', Enabled: false, ViewMode: 'Portrait', MinItems: 4, MaxItems: 20, ExtraParams: [] };
        expect(widgetSummary(genre, 'Drame', t)).toBe('Drame · admin.viewmode.portrait · admin.summary.items{"min":4,"max":20} · admin.summary.disabled');
    });
});

describe('getExtraParam', () => {
    it('reads one ExtraParams entry, empty when absent', () => {
        const widget = { ExtraParams: [{ Key: 'value', Value: 'Drame' }, { Key: 'scope', Value: null }] };
        expect(getExtraParam(widget, 'value')).toBe('Drame');
        expect(getExtraParam(widget, 'scope')).toBe('');
        expect(getExtraParam(widget, 'missing')).toBe('');
        expect(getExtraParam({}, 'value')).toBe('');
    });
});

describe('validateTmdb', () => {
    it('accepts an empty key, a v3 key and a v4 token, and nothing else', () => {
        expect(validateTmdb('')).toBe(true);
        expect(validateTmdb('  ')).toBe(true);
        expect(validateTmdb('0123456789abcdef0123456789ABCDEF')).toBe(true);
        expect(validateTmdb('eyJhbGciOi.eyJzdWIiOi.c2lnbmF0dXJl')).toBe(true);
        expect(validateTmdb('0123456789abcdef')).toBe(false);
        expect(validateTmdb('not a key')).toBe(false);
    });
});

describe('escHtml', () => {
    it('escapes the HTML metacharacters', () => {
        expect(escHtml('<a href="x">&</a>')).toBe('&lt;a href=&quot;x&quot;&gt;&amp;&lt;/a&gt;');
        expect(escHtml(null)).toBe('');
    });
});

describe('initialTab (DEC-P10-2)', () => {
    it('opens the tab of this session, otherwise External sources on a TMDb failure, otherwise Settings', () => {
        expect(initialTab('sections', ['TMDbInvalidKey'])).toBe('sections');
        expect(initialTab(null, ['CustomTabsInstalled', 'TMDbUnreachable'])).toBe('sources');
        expect(initialTab('unknown', ['CustomTabsInstalled'])).toBe('settings');
        expect(initialTab(null, [])).toBe('settings');
    });
});

describe('tabSnapshot and changedTabs (ADM-01.c)', () => {
    const base = { Enabled: true, WatchlistAutoRemove: true, Modules: { Tabs: true }, Cache: { SessionTtlMinutes: 15, TMDbRefreshIntervalHours: 24 }, ApiKeys: { TMDb: null }, TMDbLists: {}, Widgets: [{ WidgetType: 'a', Order: 0 }] };
    const edit = (change) => { const copy = JSON.parse(JSON.stringify(base)); change(copy); return tabSnapshot(copy); };

    it('names only the tab a field belongs to', () => {
        expect(changedTabs(tabSnapshot(base), tabSnapshot(base))).toEqual([]);
        expect(changedTabs(tabSnapshot(base), edit((c) => { c.Cache.SessionTtlMinutes = 5; }))).toEqual(['settings']);
        expect(changedTabs(tabSnapshot(base), edit((c) => { c.Cache.TMDbRefreshIntervalHours = 6; }))).toEqual(['sources']);
        expect(changedTabs(tabSnapshot(base), edit((c) => { c.Widgets[0].Order = 10; }))).toEqual(['sections']);
        expect(changedTabs(tabSnapshot(base), edit((c) => { c.ApiKeys.TMDb = 'k'; c.Modules.Tabs = false; }))).toEqual(['settings', 'sources']);
    });
});

describe('configuration page controller (F-33)', () => {
    const descriptors = [
        { WidgetType: 'jux.admin.genre', DisplayName: 'Genre', Category: 1, MinItems: 4, ViewMode: 'Portrait' },
        { WidgetType: 'jux.admin.collection', DisplayName: 'Collection', Category: 1, MinItems: 4, ViewMode: 'Portrait' },
        { WidgetType: 'jux.connected.seasonal', DisplayName: 'Seasonal', Category: 1, MinItems: 1, ViewMode: 'Portrait' },
        { WidgetType: 'jux.local.top-rated', DisplayName: 'Top Rated in Library', Category: 1, MinItems: 4, ViewMode: 'Portrait' }
    ];
    const values = {
        'jux.admin.genre': [{ Value: 'Drame', Label: 'Drame' }],
        'jux.admin.collection': [{ Value: 'c1', Label: 'Harry Potter - Saga' }]
    };
    const config = {
        Enabled: true,
        Widgets: [
            { WidgetType: 'jux.admin.genre', Enabled: true, Order: 0, MinItems: 4, MaxItems: 20, ViewMode: 'Portrait', CustomDisplayName: 'Drame', ExtraParams: [{ Key: 'value', Value: 'Drame' }] },
            { WidgetType: 'jux.admin.collection', Enabled: true, Order: 1, MinItems: 4, MaxItems: 20, ViewMode: 'Portrait', CustomDisplayName: 'Harry Potter - Saga', ExtraParams: [{ Key: 'value', Value: 'c1' }] },
            { WidgetType: 'jux.connected.seasonal', Enabled: true, Order: 2, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 's1' }, { Key: 'startDate', Value: '10-01' }, { Key: 'endDate', Value: '10-31' }, { Key: 'theme', Value: 'halloween' }] }
        ]
    };

    function getJSON(url) {
        const valuesRoute = /^JuxHomepage\/Widget\/(.+)\/values$/.exec(url);
        if (valuesRoute) { return Promise.resolve(values[decodeURIComponent(valuesRoute[1])] || []); }
        if (url === 'JuxHomepage/Admin/Catalog') {
            return Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: descriptors });
        }
        if (url === 'JuxHomepage/Localizations') { return Promise.resolve({}); }
        return Promise.resolve([]);
    }

    let view;

    beforeEach(() => {
        const doc = new DOMParser().parseFromString(CONFIG_HTML, 'text/html');
        view = doc.getElementById('juxHomepageConfigPage');
        document.body.appendChild(document.adoptNode(view));
        window.ApiClient = {
            getPluginConfiguration: vi.fn(() => Promise.resolve(JSON.parse(JSON.stringify(config)))),
            getJSON: vi.fn(getJSON),
            getUrl: (route) => route,
            getCurrentUserId: () => 'u1',
            ajax: vi.fn(() => Promise.resolve())
        };
        window.Dashboard = { showLoadingMsg: vi.fn(), hideLoadingMsg: vi.fn() };
        window.sessionStorage.clear();
    });

    afterEach(() => {
        view.dispatchEvent(new Event('viewbeforehide'));
        view.remove();
        delete window.ApiClient;
        delete window.Dashboard;
    });

    async function settle() {
        for (let i = 0; i < 10; i++) {
            await new Promise((r) => setTimeout(r, 0));
        }
    }

    function card(index) {
        return view.querySelector('.jux-admin-widget-card[data-index="' + index + '"]');
    }

    it('keeps each row with its own value when two admin rows of different types are swapped', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        expect(card(1).querySelector('.jux-admin-widget-value').value).toBe('Harry Potter - Saga');
        card(1).querySelector('.jux-admin-btn-up').click();
        await settle();

        expect(card(0).querySelector('.jux-admin-widget-value').value).toBe('Harry Potter - Saga');
        expect(card(1).querySelector('.jux-admin-widget-value').value).toBe('Drame');

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const saved = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets;
        expect(saved.map((w) => [w.WidgetType, getExtraParam(w, 'value'), w.Order])).toEqual([
            ['jux.admin.collection', 'c1', 0],
            ['jux.admin.genre', 'Drame', 10],
            ['jux.connected.seasonal', 's1', 20]
        ]);
    });

    it('saves a loaded Discover and Rewards row unchanged, even after a fold, with no search done and no TMDb genres (FIAB-02)', async () => {
        const discover = { WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 3, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 'd1' }, { Key: 'genreIds', Value: '18' }, { Key: 'personIds', Value: '525' }, { Key: 'keywordIds', Value: '9715' }, { Key: 'companyIds', Value: '420' }, { Key: 'sortBy', Value: 'popularity.desc' }, { Key: 'minVotes', Value: '50' }, { Key: 'pages', Value: '1' }] };
        const rewards = { WidgetType: 'jux.connected.rewards', Enabled: true, Order: 4, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 'r1' }, { Key: 'ceremonyQid', Value: 'Q19020' }, { Key: 'categoryQid', Value: 'Q102427' }, { Key: 'year', Value: '2020' }] };
        const loaded = { ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), discover, rewards] };
        const catalog = [...descriptors,
            { WidgetType: 'jux.connected.discover-movies', DisplayName: 'Discover', Category: 3, MinItems: 1, ViewMode: 'Portrait' },
            { WidgetType: 'jux.connected.rewards', DisplayName: 'Rewards', Category: 3, MinItems: 1, ViewMode: 'Portrait' }];
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve(JSON.parse(JSON.stringify(loaded))));
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Catalog'
            ? Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog })
            : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        card(3).querySelector('.jux-admin-btn-toggle').click();
        await settle();
        card(3).querySelector('.jux-admin-btn-toggle').click();
        await settle();

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const saved = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets;
        const byKey = (w) => Object.fromEntries(w.ExtraParams.map((p) => [p.Key, p.Value]));
        expect(byKey(saved[3])).toEqual(byKey(discover));
        expect(byKey(saved[4])).toEqual(byKey(rewards));
    });

    it('adds a row as the catalog describes it, with a new instance id in place of the empty value (QUAL-03.c)', async () => {
        const defaultRow = { WidgetType: 'jux.connected.discover-movies', Enabled: true, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: '' }, { Key: 'sortBy', Value: 'popularity.desc' }, { Key: 'minVotes', Value: '30' }, { Key: 'pages', Value: '1' }] };
        const catalog = [...descriptors, { WidgetType: 'jux.connected.discover-movies', DisplayName: 'Discover', Category: 3, MinItems: 1, ViewMode: 'Portrait', DefaultRow: defaultRow }];
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Catalog'
            ? Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog })
            : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        view.querySelector('#juxAddWidgetType').value = 'jux.connected.discover-movies';
        view.querySelector('#juxBtnAddWidget').click();
        view.querySelector('#juxAddWidgetType').value = 'jux.connected.discover-movies';
        view.querySelector('#juxBtnAddWidget').click();
        await settle();

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const added = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets.slice(3);
        const byKey = (w) => Object.fromEntries(w.ExtraParams.map((p) => [p.Key, p.Value]));
        expect(added.map((w) => [w.WidgetType, w.MinItems, w.MaxItems, byKey(w).minVotes])).toEqual([
            ['jux.connected.discover-movies', 1, 20, '30'],
            ['jux.connected.discover-movies', 1, 20, '30']
        ]);
        expect(byKey(added[0]).value).toMatch(/^[0-9a-f-]{36}$/);
        expect(byKey(added[0]).value).not.toBe(byKey(added[1]).value);
        expect(defaultRow.ExtraParams[0].Value).toBe('');
    });

    it('shows the catalog default of a parameter a loaded row does not have (QUAL-03.c, R11)', async () => {
        const row = { WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 3, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 'd1' }] };
        const defaultRow = { WidgetType: 'jux.connected.discover-movies', ExtraParams: [{ Key: 'value', Value: '' }, { Key: 'sortBy', Value: 'vote_count.desc' }, { Key: 'minVotes', Value: '30' }, { Key: 'pages', Value: '2' }] };
        const catalog = [...descriptors, { WidgetType: 'jux.connected.discover-movies', DisplayName: 'Discover', Category: 3, MinItems: 1, ViewMode: 'Portrait', DefaultRow: defaultRow }];
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve({ ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), row] }));
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Catalog'
            ? Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog })
            : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        expect([card(3).querySelector('.jux-discover-sortby').value, card(3).querySelector('.jux-discover-minvotes').value, card(3).querySelector('.jux-discover-pages').value])
            .toEqual(['vote_count.desc', '30', '2']);
    });

    it('lists the answer of the latest search, even when an older one answers last (QUAL-03.b)', async () => {
        const row = { WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 3, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 'd1' }] };
        const catalog = [...descriptors, { WidgetType: 'jux.connected.discover-movies', DisplayName: 'Discover', Category: 3, MinItems: 1, ViewMode: 'Portrait' }];
        const pending = {};
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve({ ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), row] }));
        window.ApiClient.getUrl = (route, params) => (params && params.query ? route + '?query=' + params.query : route);
        window.ApiClient.getJSON = vi.fn((url) => {
            if (url === 'JuxHomepage/Admin/Catalog') { return Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog }); }
            if (url.startsWith('JuxHomepage/TMDb/Search/Person')) { return new Promise((r) => { pending[url] = r; }); }
            return getJSON(url);
        });
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        card(3).querySelector('.jux-admin-btn-toggle').click();
        await settle();

        const search = card(3).querySelector('.jux-discover-person');
        search.value = 'Jo';
        search.dispatchEvent(new Event('input'));
        await new Promise((r) => setTimeout(r, 350));
        search.value = 'John';
        search.dispatchEvent(new Event('input'));
        await new Promise((r) => setTimeout(r, 350));
        pending['JuxHomepage/TMDb/Search/Person?query=John']([{ Value: '2', Label: 'John Smith' }]);
        await settle();
        pending['JuxHomepage/TMDb/Search/Person?query=Jo']([{ Value: '1', Label: 'Jo Ann' }]);
        await settle();

        const listed = [...view.querySelector('#' + search.getAttribute('list')).options].map((o) => o.value);
        expect(listed).toEqual(['John Smith']);
    });

    it('keeps each row its own id when two people found by a search share a name (FIAB-02, R11)', async () => {
        const row = (id, person, order) => ({ WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: order, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: id }, { Key: 'personIds', Value: person }] });
        const catalog = [...descriptors, { WidgetType: 'jux.connected.discover-movies', DisplayName: 'Discover', Category: 3, MinItems: 1, ViewMode: 'Portrait' }];
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve({ ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), row('d1', '1', 3), row('d2', '2', 4)] }));
        window.ApiClient.getJSON = vi.fn((url) => {
            if (url === 'JuxHomepage/Admin/Catalog') { return Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog }); }
            if (url === 'JuxHomepage/TMDb/Search/Person') { return Promise.resolve([{ Value: '1', Label: 'John Smith' }, { Value: '2', Label: 'John Smith' }]); }
            return getJSON(url);
        });
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const search = card(3).querySelector('.jux-discover-person');
        search.value = 'John Smith';
        search.dispatchEvent(new Event('input'));
        await new Promise((r) => setTimeout(r, 350));
        await settle();
        search.value = '1';
        card(4).querySelector('.jux-admin-btn-toggle').click();
        await settle();
        expect(card(4).querySelector('.jux-discover-person').value).toBe('John Smith');

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const saved = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets;
        expect(saved.slice(3).map((w) => getExtraParam(w, 'personIds'))).toEqual(['1', '2']);
    });

    it('drops a Discover id once its field is cleared or edited to a name with no search match', async () => {
        const discover = { WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 3, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 'd1' }, { Key: 'personIds', Value: '525' }, { Key: 'keywordIds', Value: '9715' }] };
        const catalog = [...descriptors, { WidgetType: 'jux.connected.discover-movies', DisplayName: 'Discover', Category: 3, MinItems: 1, ViewMode: 'Portrait' }];
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve({ ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), discover] }));
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Catalog'
            ? Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog })
            : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        card(3).querySelector('.jux-discover-person').value = '';
        card(3).querySelector('.jux-discover-keyword').value = 'Christopher Nolan';
        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const keys = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets[3].ExtraParams.map((p) => p.Key);
        expect(keys).not.toContain('personIds');
        expect(keys).not.toContain('keywordIds');
    });

    it('unfolds a Rewards row with no ceremony or category and focuses its ceremony, without saving (FIAB-02)', async () => {
        const rewards = { WidgetType: 'jux.connected.rewards', Enabled: true, Order: 3, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 'r1' }, { Key: 'year', Value: '2024' }] };
        const catalog = [...descriptors, { WidgetType: 'jux.connected.rewards', DisplayName: 'Rewards', Category: 3, MinItems: 1, ViewMode: 'Portrait' }];
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve({ ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), rewards] }));
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Catalog'
            ? Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog })
            : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));

        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        expect(window.ApiClient.updatePluginConfiguration).not.toHaveBeenCalled();
        expect(card(3).classList.contains('jux-admin-widget-card-collapsed')).toBe(false);
        expect(view.querySelector('#juxRewardsError_3').style.display).toBe('');
        expect(document.activeElement).toBe(card(3).querySelector('.jux-rewards-ceremony'));
    });

    it('refuses to save a Rewards row whose ceremony is not a Wikidata Q-id, as the server would (FIAB-02, R11)', async () => {
        const rewards = { WidgetType: 'jux.connected.rewards', Enabled: true, Order: 3, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'value', Value: 'r1' }, { Key: 'ceremonyQid', Value: 'Academy' }] };
        const catalog = [...descriptors, { WidgetType: 'jux.connected.rewards', DisplayName: 'Rewards', Category: 3, MinItems: 1, ViewMode: 'Portrait' }];
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve({ ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), rewards] }));
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Catalog'
            ? Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: catalog })
            : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));

        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        expect(window.ApiClient.updatePluginConfiguration).not.toHaveBeenCalled();
        expect(view.querySelector('#juxRewardsError_3').style.display).toBe('');
    });

    it('shows that the configuration was not saved when the server refuses it, and hides it on the next save (R11)', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.reject(new Error('400')));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();

        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();
        expect(view.querySelector('#juxSaveError').hidden).toBe(false);

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();
        expect(view.querySelector('#juxSaveError').hidden).toBe(true);
        errorSpy.mockRestore();
    });

    it('offers to restore the saved configuration after a reset, and reloads the page once restored (FIAB-07)', async () => {
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Warnings' ? Promise.resolve(['ConfigurationReset']) : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const restore = view.querySelector('#juxWarningBanner .jux-admin-warning-restore');
        expect(restore).not.toBeNull();
        expect(window.ApiClient.ajax).not.toHaveBeenCalled();

        restore.click();
        await settle();

        expect(window.ApiClient.ajax).toHaveBeenCalledWith(expect.objectContaining({ type: 'POST', url: 'JuxHomepage/Admin/RestoreConfiguration' }));
        expect(window.ApiClient.getPluginConfiguration).toHaveBeenCalledTimes(2);
    });

    it('says so when the saved configuration cannot be restored (FIAB-07)', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Warnings' ? Promise.resolve(['ConfigurationReset']) : getJSON(url)));
        window.ApiClient.ajax = vi.fn(() => Promise.reject(new Error('404')));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        view.querySelector('#juxWarningBanner .jux-admin-warning-restore').click();
        await settle();

        expect(view.querySelector('#juxWarningBanner .jux-admin-warning-restore-error').hidden).toBe(false);
        expect(window.ApiClient.getPluginConfiguration).toHaveBeenCalledTimes(1);
        errorSpy.mockRestore();
    });

    it('offers the sort order on library rows and saves the chosen one (F-53)', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const sort = card(0).querySelector('.jux-admin-sort');
        expect(sort.value).toBe('SortName:Ascending');
        expect(card(2).querySelector('.jux-admin-sort')).not.toBeNull();
        sort.value = 'Random:Ascending';

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const saved = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets;
        expect(saved.map((w) => w.SortBy)).toEqual(['Random', 'SortName', 'SortName']);
    });

    it('gives a local library row its item types instead of a value or a sort, and saves them (F-61)', async () => {
        const local = { WidgetType: 'jux.local.top-rated', Enabled: false, Order: 3, MinItems: 4, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [{ Key: 'itemTypes', Value: 'Series' }] };
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve({ ...JSON.parse(JSON.stringify(config)), Widgets: [...JSON.parse(JSON.stringify(config.Widgets)), local] }));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const types = card(3).querySelector('.jux-local-itemtypes');
        expect(types.value).toBe('Series');
        expect(card(3).querySelector('.jux-admin-sort')).toBeNull();
        expect(card(3).querySelector('.jux-admin-widget-value')).toBeNull();
        expect(card(0).querySelector('.jux-admin-widget-value')).not.toBeNull();
        types.value = 'Movie';

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const saved = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets[3];
        expect(saved.ExtraParams).toEqual([{ Key: 'itemTypes', Value: 'Movie' }]);
    });

    it('folds every row into a summary line, and unfolds one with its toggle', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const cards = [...view.querySelectorAll('.jux-admin-widget-card')];
        expect(cards.every((c) => c.classList.contains('jux-admin-widget-card-collapsed'))).toBe(true);
        expect(card(0).querySelector('.jux-admin-widget-summary').textContent).toContain('Drame');

        card(0).querySelector('.jux-admin-btn-toggle').click();
        await settle();
        expect(card(0).classList.contains('jux-admin-widget-card-collapsed')).toBe(false);
        expect(card(0).querySelector('.jux-admin-btn-toggle').getAttribute('aria-expanded')).toBe('true');
        expect(card(1).classList.contains('jux-admin-widget-card-collapsed')).toBe(true);
        expect(document.activeElement).toBe(card(0).querySelector('.jux-admin-btn-toggle'));
    });

    it('unfolds an admin row saved without a value and focuses its field, without saving', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        const input = card(1).querySelector('.jux-admin-widget-value');
        input.value = '';
        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));

        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        expect(window.ApiClient.updatePluginConfiguration).not.toHaveBeenCalled();
        expect(card(1).classList.contains('jux-admin-widget-card-collapsed')).toBe(false);
        expect(view.querySelector('#juxValError_1').style.display).toBe('');
        expect(document.activeElement).toBe(card(1).querySelector('.jux-admin-widget-value'));
    });

    function types() {
        return [...view.querySelectorAll('.jux-admin-widget-card')].map((c) => c.querySelector('.checkboxLabel').textContent);
    }

    it('moves the last section to the first place by typing its position (F-50)', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        expect(types()).toEqual(['Genre', 'Collection', 'Seasonal']);

        const position = card(2).querySelector('.jux-admin-position');
        position.value = '1';
        position.dispatchEvent(new Event('change'));
        await settle();

        expect(types()).toEqual(['Seasonal', 'Genre', 'Collection']);
        expect(document.activeElement).toBe(card(0).querySelector('.jux-admin-position'));
    });

    it('applies a typed position on Enter without saving the page', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        const position = card(2).querySelector('.jux-admin-position');
        position.focus();
        position.value = '1';

        const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
        position.dispatchEvent(enter);
        await settle();

        expect(enter.defaultPrevented).toBe(true);
        expect(types()).toEqual(['Seasonal', 'Genre', 'Collection']);
        expect(window.ApiClient.updatePluginConfiguration).not.toHaveBeenCalled();

        // The replaced field then fires a change, as Chromium does when the re-render removes it while
        // focused: its stale index (2) now names Collection, which must stay where it is.
        position.dispatchEvent(new Event('change'));
        await settle();
        expect(types()).toEqual(['Seasonal', 'Genre', 'Collection']);
    });

    it('puts an out-of-range position back without moving anything', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const position = card(0).querySelector('.jux-admin-position');
        position.value = '9';
        position.dispatchEvent(new Event('change'));
        await settle();

        expect(types()).toEqual(['Genre', 'Collection', 'Seasonal']);
        expect(position.value).toBe('1');
    });

    it('moves a section dragged by its handle and dropped above another one (F-50)', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        const dataTransfer = { setData: vi.fn(), setDragImage: vi.fn() };
        function dragEvent(type, clientY) {
            const e = new Event(type, { bubbles: true, cancelable: true });
            e.dataTransfer = dataTransfer;
            e.clientY = clientY;
            return e;
        }

        card(2).querySelector('.jux-admin-drag-handle').dispatchEvent(dragEvent('dragstart', 0));
        const over = dragEvent('dragover', -1);
        card(0).dispatchEvent(over);
        expect(over.defaultPrevented).toBe(true);
        expect(card(0).classList.contains('jux-admin-drop-before')).toBe(true);
        card(0).dispatchEvent(dragEvent('drop', -1));
        await settle();

        expect(types()).toEqual(['Seasonal', 'Genre', 'Collection']);
    });

    it('previews the edited, unsaved value of a row (F-62)', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        window.ApiClient.ajax = vi.fn(() => Promise.resolve({ TotalRecordCount: 1, MinItems: 4, HiddenReason: 'MinItems', Items: [{ Id: 'x', Name: 'Mad Max', ImageTags: { Primary: 't1' } }] }));
        card(0).querySelector('.jux-admin-btn-toggle').click();
        await settle();
        values['jux.admin.genre'].push({ Value: 'Action', Label: 'Action' });
        card(0).querySelector('.jux-admin-widget-value').value = 'Action';

        card(0).querySelector('.jux-admin-btn-preview').click();
        await settle();

        const request = window.ApiClient.ajax.mock.calls[0][0];
        expect(request.type).toBe('POST');
        expect(request.url).toBe('JuxHomepage/Preview');
        expect(getExtraParam(JSON.parse(request.data), 'value')).toBe('Action');
        const preview = view.querySelector('#juxPreview_0');
        expect(preview.querySelector('.jux-admin-preview-name').textContent).toBe('Mad Max');
        expect(preview.querySelector('img').getAttribute('src')).toBe('Items/x/Images/Primary');
        expect(preview.querySelector('.jux-admin-preview-hidden')).not.toBeNull();
        values['jux.admin.genre'].pop();
    });

    it('offers a retry when the preview fails', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        window.ApiClient.ajax = vi.fn()
            .mockImplementationOnce(() => Promise.reject(new Error('down')))
            .mockImplementationOnce(() => Promise.resolve({ TotalRecordCount: 0, MinItems: 1, Items: [] }));
        vi.spyOn(console, 'error').mockImplementation(() => {});

        card(0).querySelector('.jux-admin-btn-preview').click();
        await settle();
        const preview = view.querySelector('#juxPreview_0');
        expect(preview.querySelector('.jux-admin-field-error')).not.toBeNull();

        preview.querySelector('.jux-admin-btn-preview-retry').click();
        await settle();
        expect(window.ApiClient.ajax).toHaveBeenCalledTimes(2);
        expect(preview.textContent).toContain('admin.preview.empty');
    });

    it('masks the TMDb key and shows it on demand (F-50)', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        const key = view.querySelector('#txtTMDb');
        const toggle = view.querySelector('#juxBtnShowTmdb');

        expect(key.type).toBe('password');
        expect(toggle.getAttribute('aria-pressed')).toBe('false');
        expect(toggle.getAttribute('aria-label')).toBe('admin.apikeys.tmdb.show');

        toggle.click();
        expect(key.type).toBe('text');
        expect(toggle.getAttribute('aria-pressed')).toBe('true');
        expect(toggle.getAttribute('aria-label')).toBe('admin.apikeys.tmdb.hide');

        toggle.click();
        expect(key.type).toBe('password');
    });

    it('shows how many cached TMDb items were found in the library, and what Now Playing needs (F-61)', async () => {
        window.ApiClient.getJSON = vi.fn((url) => {
            if (url === 'JuxHomepage/TMDb/Status') {
                return Promise.resolve([
                    { Type: 'TrendingMovies', LastRefreshedUtc: null, ItemCount: 80, MatchedCount: 2 },
                    { Type: 'NowPlayingMovies', LastRefreshedUtc: null, ItemCount: 79, MatchedCount: 2 }
                ]);
            }
            if (url === 'JuxHomepage/Localizations') {
                return Promise.resolve({ 'admin.tmdbstatus.cached': '{label} ({count} cached, {matched} in your library)', 'jux.connected.trending-movies': 'Trending Movies' });
            }
            return getJSON(url);
        });
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        expect(view.querySelector('#tmdbLabel_TrendingMovies').textContent).toBe('Trending Movies (80 cached, 2 in your library)');
        const help = [...view.querySelectorAll('#juxTmdbStatusList .fieldDescription')].map((d) => d.textContent);
        expect(help).toEqual(['admin.tmdbstatus.nowplaying.help']);
    });

    const tab = (name) => view.querySelector('#juxTab_' + name);
    const panel = (name) => view.querySelector('#juxPanel_' + name);
    const dirtyTabs = () => ['settings', 'sections', 'sources'].filter((name) => !tab(name).querySelector('.jux-admin-tab-dirty').hidden);
    async function open() {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();
    }
    function type(el, value) {
        el.value = value;
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
    }

    it('shows one panel at a time, as WAI-ARIA tabs that the arrows move between (ADM-01.a)', async () => {
        await open();

        expect(view.querySelector('.jux-admin-tabs').getAttribute('role')).toBe('tablist');
        expect(tab('settings').getAttribute('aria-selected')).toBe('true');
        expect([panel('settings').hidden, panel('sections').hidden, panel('sources').hidden]).toEqual([false, true, true]);

        tab('sections').click();
        expect(tab('sections').getAttribute('aria-selected')).toBe('true');
        expect(tab('settings').getAttribute('aria-selected')).toBe('false');
        expect([panel('settings').hidden, panel('sections').hidden]).toEqual([true, false]);
        expect([tab('settings').tabIndex, tab('sections').tabIndex]).toEqual([-1, 0]);

        tab('sections').focus();
        tab('sections').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        expect(panel('sources').hidden).toBe(false);
        expect(document.activeElement).toBe(tab('sources'));
        tab('sources').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        expect(panel('settings').hidden).toBe(false);
        tab('settings').dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
        expect(panel('sources').hidden).toBe(false);
    });

    it('opens the tab of the session again, and External sources on a TMDb failure (ADM-01.d, DEC-P10-2)', async () => {
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Warnings' ? Promise.resolve(['TMDbInvalidKey']) : getJSON(url)));
        await open();

        expect(panel('sources').hidden).toBe(false);
        expect(view.querySelector('#juxWarningBanner').textContent).toContain('admin.warning.TMDbInvalidKey');

        tab('sections').click();
        view.dispatchEvent(new Event('viewshow'));
        await settle();
        expect(panel('sections').hidden).toBe(false);
    });

    it('opens the tab of the field a Save refuses before focusing it (ADM-01.b)', async () => {
        await open();
        view.querySelector('#txtTMDb').value = 'not a key';
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));

        expect(panel('sources').hidden).toBe(false);
        expect(tab('sources').getAttribute('aria-selected')).toBe('true');

        view.querySelector('#txtTMDb').value = '';
        tab('settings').click();
        card(0).querySelector('.jux-admin-widget-value').value = '';
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));

        expect(panel('sections').hidden).toBe(false);
        expect(tab('sections').getAttribute('aria-selected')).toBe('true');
    });

    it('brings a section it adds into view at the top of the Sections tab (ADM-01.a)', async () => {
        const scrolled = vi.fn();
        Element.prototype.scrollIntoView = scrolled;
        const topRated = { ...descriptors[3], DefaultRow: { WidgetType: 'jux.local.top-rated', Enabled: true, MinItems: 4, MaxItems: 20, ViewMode: 'Portrait', ExtraParams: [] } };
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/Admin/Catalog'
            ? Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: [...descriptors.slice(0, 3), topRated] })
            : getJSON(url)));
        await open();
        const addBar = panel('sections').querySelector('.jux-admin-add-section');
        expect(addBar.compareDocumentPosition(view.querySelector('#juxWidgetList')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

        view.querySelector('#juxAddWidgetType').value = 'jux.local.top-rated';
        view.querySelector('#juxBtnAddWidget').click();

        expect(scrolled).toHaveBeenCalledTimes(1);
        expect(scrolled.mock.contexts[0]).toBe(card(3));
        delete Element.prototype.scrollIntoView;
    });

    it('loads with no unsaved change, even for an admin row with no name and no label yet (ADM-01.c)', async () => {
        const unnamed = JSON.parse(JSON.stringify(config));
        unnamed.Widgets[1].CustomDisplayName = null;
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve(JSON.parse(JSON.stringify(unnamed))));
        await open();

        expect(dirtyTabs()).toEqual([]);
        expect(view.querySelector('#juxUnsaved').hidden).toBe(true);
        expect(card(1).querySelector('.jux-admin-widget-name').value).toBe('');
    });

    it('marks the tab a change belongs to and clears it when the change is undone or saved (ADM-01.c)', async () => {
        await open();
        const ttl = view.querySelector('#numSessionTtl');

        type(ttl, '5');
        expect(dirtyTabs()).toEqual(['settings']);
        expect(view.querySelector('#juxUnsaved').hidden).toBe(false);

        type(ttl, '15');
        expect(dirtyTabs()).toEqual([]);

        card(1).querySelector('.jux-admin-btn-up').click();
        expect(dirtyTabs()).toEqual(['sections']);

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        expect(dirtyTabs()).toEqual([]);
        expect(view.querySelector('#juxUnsaved').hidden).toBe(true);
    });

    it('asks before the browser tab closes with unsaved changes, and not once they are gone (DEC-P10-5)', async () => {
        await open();
        const unload = () => {
            const event = new Event('beforeunload', { cancelable: true });
            window.dispatchEvent(event);
            return event.defaultPrevented;
        };
        expect(unload()).toBe(false);

        type(view.querySelector('#numSessionTtl'), '5');
        expect(unload()).toBe(true);

        view.dispatchEvent(new Event('viewbeforehide'));
        expect(unload()).toBe(false);
    });

    it('shows the changes left unsaved again on return, until they are discarded (DEC-P10-5)', async () => {
        await open();
        type(view.querySelector('#numSessionTtl'), '5');
        card(1).querySelector('.jux-admin-btn-up').click();
        view.dispatchEvent(new Event('viewbeforehide'));

        view.dispatchEvent(new Event('viewshow'));
        await settle();

        expect(view.querySelector('#numSessionTtl').value).toBe('5');
        expect(card(0).querySelector('.jux-admin-widget-value').value).toBe('Harry Potter - Saga');
        expect(view.querySelector('#juxDraftNotice').hidden).toBe(false);
        expect(dirtyTabs()).toEqual(['settings', 'sections']);

        view.querySelector('#juxBtnDiscardDraft').click();

        expect(view.querySelector('#numSessionTtl').value).toBe('15');
        expect(card(0).querySelector('.jux-admin-widget-value').value).toBe('Drame');
        expect(view.querySelector('#juxDraftNotice').hidden).toBe(true);
        expect(dirtyTabs()).toEqual([]);
    });

    it('drops the kept changes when the saved configuration changed meanwhile (DEC-P10-5)', async () => {
        await open();
        type(view.querySelector('#numSessionTtl'), '5');
        view.dispatchEvent(new Event('viewbeforehide'));

        const changed = JSON.parse(JSON.stringify(config));
        changed.Cache = { SessionTtlMinutes: 30 };
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve(JSON.parse(JSON.stringify(changed))));
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        expect(view.querySelector('#numSessionTtl').value).toBe('30');
        expect(view.querySelector('#juxDraftNotice').hidden).toBe(true);
        expect(dirtyTabs()).toEqual([]);
    });

    it('shows the page without waiting for the TMDb countries and genres (FIAB-10.b)', async () => {
        window.ApiClient.getJSON = vi.fn((url) => (url === 'JuxHomepage/TMDb/Countries' || url === 'JuxHomepage/TMDb/Genres'
            ? new Promise(() => {})
            : getJSON(url)));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        expect(card(0)).not.toBeNull();
        expect(window.Dashboard.hideLoadingMsg).toHaveBeenCalled();
    });

    it('keeps the saved region while the countries load, then names it (FIAB-10.b)', async () => {
        let answerCountries;
        const countries = new Promise((resolve) => { answerCountries = resolve; });
        const withRegion = { ...JSON.parse(JSON.stringify(config)), TMDbLists: { NowPlayingRegion: 'FR' } };
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve(JSON.parse(JSON.stringify(withRegion))));
        window.ApiClient.getJSON = vi.fn((url) => {
            if (url === 'JuxHomepage/TMDb/Countries') { return countries; }
            if (url === 'JuxHomepage/TMDb/Status') {
                return Promise.resolve([{ Type: 'NowPlayingMovies', LastRefreshedUtc: null, ItemCount: 0, MatchedCount: 0, LastError: null }]);
            }
            return getJSON(url);
        });
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const region = () => view.querySelector('#selNowPlayingRegion');
        expect(region().value).toBe('FR');

        answerCountries([{ Value: 'DE', Label: 'Germany' }, { Value: 'FR', Label: 'France' }]);
        await settle();

        expect(region().value).toBe('FR');
        expect(region().selectedOptions[0].textContent).toBe('France');
        expect(region().options).toHaveLength(3);
    });

    it('says why a TMDb list came back empty, and nothing for a list that did not fail (FIAB-10.c)', async () => {
        window.ApiClient.getJSON = vi.fn((url) => {
            if (url === 'JuxHomepage/TMDb/Status') {
                return Promise.resolve([
                    { Type: 'TrendingMovies', LastRefreshedUtc: null, ItemCount: 0, MatchedCount: 0, LastError: 'InvalidKey' },
                    { Type: 'TrendingShows', LastRefreshedUtc: null, ItemCount: 20, MatchedCount: 1, LastError: null }
                ]);
            }
            return getJSON(url);
        });
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const failed = view.querySelector('#tmdbError_TrendingMovies');
        expect(failed.hidden).toBe(false);
        expect(failed.textContent).toBe('admin.tmdbstatus.error.InvalidKey');
        expect(view.querySelector('#tmdbError_TrendingShows').hidden).toBe(true);
    });

    it('names a preset row by its translated key and keeps the key on save (F-64)', async () => {
        const named = JSON.parse(JSON.stringify(config));
        named.Widgets[2].ExtraParams.push({ Key: 'nameKey', Value: 'jux.seasonal.halloween-horror' });
        window.ApiClient.getPluginConfiguration = vi.fn(() => Promise.resolve(named));
        window.ApiClient.getJSON = vi.fn((url) => url === 'JuxHomepage/Localizations'
            ? Promise.resolve({ 'jux.seasonal.halloween-horror': "Horreur d'Halloween" })
            : getJSON(url));
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        expect(card(2).querySelector('.checkboxLabel').textContent).toBe("Horreur d'Halloween");

        window.ApiClient.updatePluginConfiguration = vi.fn(() => Promise.resolve({}));
        window.Dashboard.processPluginConfigurationUpdateResult = vi.fn();
        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const saved = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets[2];
        expect(saved.CustomDisplayName).toBeNull();
        expect(saved.ExtraParams).toContainEqual({ Key: 'nameKey', Value: 'jux.seasonal.halloween-horror' });
    });

    it('gives the Admin-category Seasonal row its own editor, not the admin value picker', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        const seasonal = card(2);
        expect(seasonal.getAttribute('data-is-admin')).toBe('0');
        expect(seasonal.querySelector('.jux-admin-widget-value')).toBeNull();
        expect(seasonal.querySelector('.jux-seasonal-startdate').value).toBe('10-01');
        expect(seasonal.querySelector('.jux-admin-category-badge').textContent).toBe('admin.category.admin');
    });
});
