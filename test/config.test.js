import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import createController, { escHtml, getExtraParam, moveRow, validateTmdb } from '../src/Jellyfin.Plugin.JuxHomepage/Web/config.js';

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
        { WidgetType: 'jux.admin.collection', DisplayName: 'Collection', Category: 1, MinItems: 4, ViewMode: 'Portrait' }
    ];
    const values = {
        'jux.admin.genre': [{ Value: 'Drame', Label: 'Drame' }],
        'jux.admin.collection': [{ Value: 'c1', Label: 'Harry Potter - Saga' }]
    };
    const config = {
        Enabled: true,
        Widgets: [
            { WidgetType: 'jux.admin.genre', Enabled: true, Order: 0, MinItems: 4, MaxItems: 20, ViewMode: 'Portrait', CustomDisplayName: 'Drame', ExtraParams: [{ Key: 'value', Value: 'Drame' }] },
            { WidgetType: 'jux.admin.collection', Enabled: true, Order: 1, MinItems: 4, MaxItems: 20, ViewMode: 'Portrait', CustomDisplayName: 'Harry Potter - Saga', ExtraParams: [{ Key: 'value', Value: 'c1' }] }
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
            ['jux.admin.genre', 'Drame', 10]
        ]);
    });
});
