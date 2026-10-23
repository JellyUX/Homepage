import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import createController from '../src/Jellyfin.Plugin.JuxHomepage/Web/config.js';

// The ExtraParams keys each widget type is saved with, shared with ExtraParamsContractTests on the C#
// side (QUAL-06.a): a key renamed on one side only turns one of the two tests red.
const CONTRACT = JSON.parse(readFileSync(resolve(__dirname, 'fixtures/extraparams-contract.json'), 'utf8')).widgets;
const CONFIG_HTML = readFileSync(resolve(__dirname, '../src/Jellyfin.Plugin.JuxHomepage/Web/config.html'), 'utf8');

// A value for every key, each one an option or a form the editor of its row accepts as loaded.
const SAMPLE = {
    value: 'v1',
    genreIds: '18',
    personIds: '525',
    keywordIds: '9715',
    companyIds: '420',
    sortBy: 'popularity.desc',
    year: '2020',
    minRating: '7',
    minVotes: '50',
    pages: '2',
    ceremonyQid: 'Q19020',
    categoryQid: 'Q102427',
    nameKey: 'jux.seasonal.halloween',
    startDate: '10-01',
    endDate: '10-31',
    theme: 'halloween',
    genre: 'Horror',
    tag: 'halloween',
    itemTypes: 'Movie',
    excludeWatched: 'false',
    scope: 'movies'
};

const CATEGORY = { native: 0, admin: 1, local: 1, personalized: 2, connected: 3 };

describe('ExtraParams contract (QUAL-06.a)', () => {
    const types = Object.keys(CONTRACT);
    let view;

    beforeEach(() => {
        const doc = new DOMParser().parseFromString(CONFIG_HTML, 'text/html');
        view = doc.getElementById('juxHomepageConfigPage');
        document.body.appendChild(document.adoptNode(view));
        const descriptors = types.map((type) => ({
            WidgetType: type,
            DisplayName: type,
            Category: type === 'jux.connected.seasonal' ? 1 : CATEGORY[type.split('.')[1]],
            MinItems: 1,
            ViewMode: 'Portrait'
        }));
        const widgets = types.map((type, i) => ({
            WidgetType: type,
            Enabled: true,
            Order: i,
            MinItems: 1,
            MaxItems: 20,
            ViewMode: 'Portrait',
            ExtraParams: CONTRACT[type].map((key) => ({ Key: key, Value: SAMPLE[key] }))
        }));
        window.ApiClient = {
            getPluginConfiguration: vi.fn(() => Promise.resolve({ Enabled: true, Widgets: widgets })),
            getJSON: vi.fn((url) => {
                if (url === 'JuxHomepage/Admin/Catalog') {
                    return Promise.resolve({ WidgetCategories: ['Native', 'Admin', 'Personalized', 'Connected'], Widgets: descriptors });
                }
                if (/^JuxHomepage\/Widget\/.+\/values$/.test(url)) {
                    return Promise.resolve([{ Value: SAMPLE.value, Label: SAMPLE.value }]);
                }
                return Promise.resolve(url === 'JuxHomepage/Localizations' ? {} : []);
            }),
            getUrl: (route) => route,
            getCurrentUserId: () => 'u1',
            ajax: vi.fn(() => Promise.resolve()),
            updatePluginConfiguration: vi.fn(() => Promise.resolve({}))
        };
        window.Dashboard = { showLoadingMsg: vi.fn(), hideLoadingMsg: vi.fn(), processPluginConfigurationUpdateResult: vi.fn() };
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

    it('saves every widget type with exactly the keys of the contract', async () => {
        createController(view);
        view.dispatchEvent(new Event('viewshow'));
        await settle();

        view.querySelector('.juxConfigForm').dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        const saved = window.ApiClient.updatePluginConfiguration.mock.calls[0][1].Widgets;
        const keys = Object.fromEntries(saved.map((w) => [w.WidgetType, w.ExtraParams.map((p) => p.Key).sort()]));
        const expected = Object.fromEntries(types.map((type) => [type, [...CONTRACT[type]].sort()]));
        expect(keys).toEqual(expected);
    });
});
