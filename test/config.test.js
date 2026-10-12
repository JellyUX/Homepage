import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import createController, { editorKind, escHtml, getExtraParam, moveRow, validateTmdb, widgetSummary } from '../src/Jellyfin.Plugin.JuxHomepage/Web/config.js';

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
        { WidgetType: 'jux.connected.seasonal', DisplayName: 'Seasonal', Category: 1, MinItems: 1, ViewMode: 'Portrait' }
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
