import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import createController, { dropIndex, editorKind, escHtml, getExtraParam, moveRow, previewHtml, sortOption, toConfigRow, validateTmdb, widgetSummary } from '../src/Jellyfin.Plugin.JuxHomepage/Web/config.js';

const CONFIG_HTML = readFileSync(resolve(__dirname, '../src/Jellyfin.Plugin.JuxHomepage/Web/config.html'), 'utf8');

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
    it('stores an admin row without user override and clamps its item range', () => {
        const row = toConfigRow({ WidgetType: 'jux.admin.genre', Enabled: true, MinItems: -2, MaxItems: 0, AllowUserOverride: true }, 3, true);
        expect(row).toMatchObject({ Order: 30, MinItems: 0, MaxItems: 1, AllowUserOverride: false, MinInstances: 1, MaxInstances: 1, ExtraParams: [], CustomDisplayName: null });
    });
});

describe('sortOption (F-53)', () => {
    it('keeps a known order, ignores the direction of Random and falls back to the name', () => {
        expect(toConfigRow({ SortBy: 'DateCreated', SortOrder: 'Descending' }, 0, false)).toMatchObject({ SortBy: 'DateCreated', SortOrder: 'Descending' });
        expect(sortOption({ SortBy: 'Random', SortOrder: 'Descending' })[2]).toBe('admin.sort.random');
        expect(toConfigRow({ SortBy: 'Bogus' }, 0, false)).toMatchObject({ SortBy: 'SortName', SortOrder: 'Ascending' });
        expect(toConfigRow({}, 0, false)).toMatchObject({ SortBy: 'SortName', SortOrder: 'Ascending' });
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
    });

    afterEach(() => {
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
