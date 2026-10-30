import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest';
import juxUi from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js';

const { showEmpty, buildProgressBar, buildStatCard, createListTab, labelFor, openSortDialog, openOptionsDialog, _buildRadio, _buildRadioGroup } = juxUi;

// Registers the labels of a test tab as client.<name>.* English keys, as Localization/en.json holds
// those of the real tabs, and returns the scope createListTab reads them by.
function scope(name, text) {
    Object.entries(text).forEach(([key, value]) => {
        window.JuxI18n.en['client.' + name + '.' + key] = value;
    });
    return name;
}

describe('showEmpty', () => {
    it('renders an icon, title, and subtitle into the container', () => {
        document.body.innerHTML = '<div id="pane"></div>';
        const container = document.getElementById('pane');

        showEmpty(container, { icon: 'bookmark_border', title: 'Empty', subtitle: 'Nothing here yet.' });

        expect(container.querySelector('.jux-empty-icon .material-icons').className).toContain('bookmark_border');
        expect(container.querySelector('.jux-empty-title').textContent).toBe('Empty');
        expect(container.querySelector('.jux-empty-subtitle').textContent).toBe('Nothing here yet.');
    });

    it('omits the subtitle element when none is given', () => {
        document.body.innerHTML = '<div id="pane"></div>';
        const container = document.getElementById('pane');

        showEmpty(container, { icon: 'info', title: 'Empty' });

        expect(container.querySelector('.jux-empty-subtitle')).toBeNull();
    });

    it('escapes title and subtitle text', () => {
        document.body.innerHTML = '<div id="pane"></div>';
        const container = document.getElementById('pane');

        showEmpty(container, { icon: 'info', title: '<script>', subtitle: '<img>' });

        expect(container.innerHTML).toContain('&lt;script&gt;');
        expect(container.innerHTML).toContain('&lt;img&gt;');
    });
});

describe('buildProgressBar', () => {
    it('renders one chunk per episode, marking the watched ones', () => {
        const html = buildProgressBar(3, 5);
        const div = document.createElement('div');
        div.innerHTML = html;

        const chunks = div.querySelectorAll('.jux-progress-chunk');
        expect(chunks.length).toBe(5);
        expect(div.querySelectorAll('.jux-progress-chunk.watched').length).toBe(3);
        expect(div.querySelectorAll('.jux-progress-chunk.unwatched').length).toBe(2);
    });

    it('adds a completed badge and class when watched equals total', () => {
        const html = buildProgressBar(5, 5);
        expect(html).toContain('jux-progress-bar-completed');
        expect(html).toContain('jux-progress-complete-badge');
    });

    it('does not add a completed badge when partially watched', () => {
        const html = buildProgressBar(2, 5);
        expect(html).not.toContain('jux-progress-complete-badge');
    });

    it.each([
        ['en', '21/30 episodes'],
        ['fr-FR', '21/30 épisodes']
    ])('names the bar with the episode count for lang "%s" (F-49)', (lang, label) => {
        document.documentElement.lang = lang;
        const div = document.createElement('div');
        div.innerHTML = buildProgressBar(21, 30);

        expect(div.querySelector('[role="progressbar"]').getAttribute('aria-label')).toBe(label);
        document.documentElement.lang = 'en';
    });

    it('handles a zero-total series without throwing', () => {
        const html = buildProgressBar(0, 0);
        expect(html).toContain('jux-progress-bar');
        expect(html).not.toContain('jux-progress-chunk');
    });
});

describe('buildStatCard', () => {
    it('renders the icon, value, and label', () => {
        const html = buildStatCard('movie', 12, 'Movies watched');
        const div = document.createElement('div');
        div.innerHTML = html;

        expect(div.querySelector('.jux-stat-icon').className).toContain('movie');
        expect(div.querySelector('.jux-stat-value').textContent).toBe('12');
        expect(div.querySelector('.jux-stat-label').textContent).toBe('Movies watched');
    });

    it('escapes an unsafe label', () => {
        expect(buildStatCard('movie', 1, '<script>')).toContain('&lt;script&gt;');
    });
});

describe('_buildRadio', () => {
    it('marks the checked option and escapes the label', () => {
        const html = _buildRadio('sortBy', 'Name', '<b>Name</b>', true);
        expect(html).toContain('value="Name"');
        expect(html).toContain('checked');
        expect(html).toContain('&lt;b&gt;Name&lt;/b&gt;');
    });

    it('does not mark an unchecked option', () => {
        const html = _buildRadio('sortBy', 'Name', 'Name', false);
        expect(html).not.toContain('checked');
    });

    it('uses a unique clip-path id per call to avoid SVG id collisions', () => {
        const first = _buildRadio('sortBy', 'A', 'A', false);
        const second = _buildRadio('sortBy', 'B', 'B', false);
        const firstId = first.match(/clipPath id="([^"]+)"/)[1];
        const secondId = second.match(/clipPath id="([^"]+)"/)[1];
        expect(firstId).not.toBe(secondId);
    });
});

describe('_buildRadioGroup', () => {
    it('renders one radio per option with the current value checked', () => {
        const html = _buildRadioGroup([{ value: 'A', label: 'A' }, { value: 'B', label: 'B' }], 'B', 'group');
        const div = document.createElement('div');
        div.innerHTML = html;

        const inputs = div.querySelectorAll('input[type="radio"]');
        expect(inputs.length).toBe(2);
        expect(inputs[0].checked).toBe(false);
        expect(inputs[1].checked).toBe(true);
    });
});

describe('buildProgressBar bound (F-10)', () => {
    const nodeCount = html => {
        const host = document.createElement('div');
        host.innerHTML = html;
        return host.querySelector('.jux-progress-bar').getElementsByTagName('*').length;
    };

    it('keeps one chunk per episode up to 50 episodes', () => {
        expect(nodeCount(buildProgressBar(10, 50))).toBe(50);
    });

    it('renders a single fill at the watched percentage above 50 episodes', () => {
        const html = buildProgressBar(250, 1000);

        expect(nodeCount(html)).toBe(1);
        expect(html).toContain('jux-progress-bar-continuous');
        expect(html).toContain('width:25%');
        expect(html).toContain('aria-valuemax="1000"');
    });
});

describe('labelFor', () => {
    const options = [{ value: 'A', label: 'Alpha' }, { value: 'B', label: 'Beta' }];

    it('returns the label of the current value, or of the first option', () => {
        expect(labelFor(options, 'B')).toBe('Beta');
        expect(labelFor(options, 'Z')).toBe('Alpha');
    });
});

describe('createListTab (F-35)', () => {
    const labels = scope('things', { title: 'Things', empty: 'No things', emptySubtitle: 'Add some.', sortName: 'Name', all: 'All', kindDialog: 'Kind' });

    function makeTab(extra) {
        return createListTab(Object.assign({
            id: 'things',
            endpoint: 'JuxHomepage/Things/Items',
            logName: 'Things',
            labels,
            state: { sortBy: 'Name', sortOrder: 'Ascending', kind: 'All' },
            sortOptions: (t) => [{ value: 'Name', label: t.sortName }],
            filters: [{ name: 'kind', key: 'kind', icon: 'filter_list', titleKey: 'kindDialog', options: (t) => [{ value: 'All', label: t.all }] }],
            emptyIcon: 'inbox'
        }, extra));
    }

    function mountPane() {
        document.documentElement.lang = 'en';
        document.body.innerHTML = '<div id="jux-tab-things"><div class="sections"></div></div>';
    }

    function apiClient(result) {
        return {
            getCurrentUserId: () => 'u1',
            getUrl: (route, query) => route + '?' + new URLSearchParams(query).toString(),
            getJSON: vi.fn(() => result)
        };
    }

    afterEach(() => {
        delete window.ApiClient;
        delete window.JellyfinAPI;
    });

    it('requests the endpoint with the sort and filter state', () => {
        mountPane();
        window.ApiClient = apiClient(new Promise(() => {}));

        makeTab().render();

        expect(window.ApiClient.getJSON).toHaveBeenCalledWith('JuxHomepage/Things/Items?userId=u1&sortBy=Name&sortOrder=Ascending&kind=All&startIndex=0&limit=50');
        expect(document.querySelector('#jux-things-sort-btn').textContent).toContain('Name');
        expect(document.querySelector('#jux-things-kind-btn .filter_list')).not.toBeNull();
        expect(document.querySelector('.jux-things-items [data-jux-loading]')).not.toBeNull();
    });

    it('lets the tab fill a header between the title and the controls (F-58)', () => {
        mountPane();
        window.ApiClient = apiClient(new Promise(() => {}));
        const header = vi.fn((el) => { el.textContent = 'tiles'; });

        makeTab({ header }).render();

        const block = document.querySelector('.jux-list-header');
        expect(header).toHaveBeenCalledWith(block, 'en');
        expect(block.previousElementSibling.tagName).toBe('H2');
        expect(block.nextElementSibling.classList.contains('jux-view-controls')).toBe(true);
    });

    it('places the title, the controls and the items on the same native gutters (F-46)', () => {
        mountPane();
        window.ApiClient = apiClient(new Promise(() => {}));

        makeTab().render();

        for (const selector of ['h2.sectionTitle', '.jux-view-controls', '.jux-things-items']) {
            const el = document.querySelector(selector);
            expect(el.classList.contains('padded-left') && el.classList.contains('padded-right'), selector).toBe(true);
        }
    });

    it('shows the empty state when the endpoint returns no row', async () => {
        mountPane();
        window.ApiClient = apiClient(Promise.resolve({ Items: [] }));

        makeTab().render();
        await Promise.resolve(); await Promise.resolve();

        expect(document.querySelector('.jux-empty-title').textContent).toBe('No things');
        expect(document.querySelector('.jux-empty-icon .inbox')).not.toBeNull();
    });

    it('renders the items of each row with cardBuilder, then decorates them', async () => {
        mountPane();
        window.ApiClient = apiClient(Promise.resolve({ Items: [{ Item: { Id: 'a' } }] }));
        const getCardsHtml = vi.fn(() => '<div class="card" data-id="a"></div>');
        window.JellyfinAPI = { cardBuilder: { getCardsHtml }, getPortraitShape: () => 'portrait' };
        const decorate = vi.fn();

        makeTab({ itemOf: (row) => row.Item, cardOptions: { showParentTitle: true }, decorate }).render();
        await Promise.resolve(); await Promise.resolve();

        expect(getCardsHtml.mock.calls[0][0]).toMatchObject({ items: [{ Id: 'a' }], shape: 'portrait', showParentTitle: true });
        expect(decorate).toHaveBeenCalledWith(document.querySelector('.jux-things-items'), [{ Item: { Id: 'a' } }], window.JuxCore.labels('things'));
    });
});

describe('createListTab paging (F-54)', () => {
    const labels = scope('things', { title: 'Things', empty: 'No things', emptySubtitle: 'Add some.', sortName: 'Name' });

    function rows(from, count) {
        return Array.from({ length: count }, (_, i) => ({ Id: 'i' + (from + i) }));
    }

    function settle() {
        return new Promise((resolve) => setTimeout(resolve, 0));
    }

    beforeEach(() => {
        document.documentElement.lang = 'en';
        document.body.innerHTML = '<div id="jux-tab-things"><div class="sections"></div></div>';
        window.JellyfinAPI = {
            cardBuilder: { getCardsHtml: (o) => o.items.map((it) => '<div class="card" data-id="' + it.Id + '"><a href="#">x</a></div>').join('') },
            getPortraitShape: () => 'portrait'
        };
    });

    afterEach(() => {
        delete window.ApiClient;
        delete window.JellyfinAPI;
    });

    it('reads 50 rows, counts them against the total and loads the next 50 on demand', async () => {
        const pages = { 0: { Items: rows(0, 50), TotalRecordCount: 120 }, 50: { Items: rows(50, 50), TotalRecordCount: 120 } };
        window.ApiClient = {
            getCurrentUserId: () => 'u1',
            getUrl: (route, query) => route + '?' + new URLSearchParams(query).toString(),
            getJSON: vi.fn((url) => Promise.resolve(pages[new URL(url, 'http://x').searchParams.get('startIndex')]))
        };
        const tab = createListTab({ id: 'things', endpoint: 'JuxHomepage/Things/Items', logName: 'Things', labels, state: { sortBy: 'Name', sortOrder: 'Ascending' }, sortOptions: (t) => [{ value: 'Name', label: t.sortName }], emptyIcon: 'inbox' });

        tab.render();
        await settle();
        expect(document.querySelector('.jux-list-count').textContent).toBe('50 of 120');
        expect(document.querySelectorAll('.jux-things-items .card')).toHaveLength(50);

        document.querySelector('.jux-list-more').click();
        await settle();

        expect(window.ApiClient.getJSON).toHaveBeenLastCalledWith(expect.stringContaining('startIndex=50&limit=50'));
        expect(document.querySelectorAll('.jux-things-items .card')).toHaveLength(100);
        expect(document.querySelector('.jux-list-count').textContent).toBe('100 of 120');
        expect(document.activeElement).toBe(document.querySelectorAll('.jux-things-items .card')[50].querySelector('a'));
    });

    it('shows no Load more once every row is listed', async () => {
        window.ApiClient = {
            getCurrentUserId: () => 'u1',
            getUrl: (route) => route,
            getJSON: vi.fn(() => Promise.resolve({ Items: rows(0, 3), TotalRecordCount: 3 }))
        };
        createListTab({ id: 'things', endpoint: 'E', logName: 'Things', labels, state: { sortBy: 'Name', sortOrder: 'Ascending' }, sortOptions: (t) => [{ value: 'Name', label: t.sortName }], emptyIcon: 'inbox' }).render();
        await settle();

        expect(document.querySelector('.jux-list-count').textContent).toBe('3 of 3');
        expect(document.querySelector('.jux-list-more')).toBeNull();
    });
});

describe('createListTab search (F-55)', () => {
    const labels = scope('things', { title: 'Things', empty: 'No things', emptySubtitle: 'Add some.', sortName: 'Name' });

    beforeEach(() => {
        vi.useFakeTimers();
        document.documentElement.lang = 'en';
        document.body.innerHTML = '<div id="jux-tab-things"><div class="sections"></div></div>';
        window.JellyfinAPI = {
            cardBuilder: { getCardsHtml: (o) => o.items.map((it) => '<div class="card" data-id="' + it.Id + '"><a href="#">x</a></div>').join('') },
            getPortraitShape: () => 'portrait'
        };
        window.ApiClient = {
            getCurrentUserId: () => 'u1',
            getUrl: (route, query) => route + '?' + new URLSearchParams(query).toString(),
            getJSON: vi.fn(() => Promise.resolve({ Items: [], TotalRecordCount: 0 }))
        };
    });

    afterEach(() => {
        vi.useRealTimers();
        delete window.ApiClient;
        delete window.JellyfinAPI;
    });

    function makeTab() {
        return createListTab({ id: 'things', endpoint: 'E', logName: 'Things', labels, state: { sortBy: 'Name', sortOrder: 'Ascending' }, sortOptions: (t) => [{ value: 'Name', label: t.sortName }], emptyIcon: 'inbox' });
    }

    it('asks the server 300 ms after the last keystroke, keeping the field and its focus', async () => {
        makeTab().render();
        const input = document.querySelector('#jux-things-search');
        input.focus();

        input.value = 'du';
        input.dispatchEvent(new Event('input'));
        vi.advanceTimersByTime(200);
        input.value = ' dune ';
        input.dispatchEvent(new Event('input'));
        vi.advanceTimersByTime(299);
        expect(window.ApiClient.getJSON).toHaveBeenCalledTimes(1);

        vi.advanceTimersByTime(1);
        expect(window.ApiClient.getJSON).toHaveBeenCalledTimes(2);
        expect(window.ApiClient.getJSON).toHaveBeenLastCalledWith('E?userId=u1&sortBy=Name&sortOrder=Ascending&searchTerm=dune&startIndex=0&limit=50');
        expect(document.querySelector('#jux-things-search')).toBe(input);
        expect(document.activeElement).toBe(input);

        await vi.runAllTimersAsync();
        expect(document.querySelector('.jux-empty-title').textContent).toBe('No results for "dune"');
    });

    it('keeps the search across a new render of the tab', () => {
        const tab = makeTab();
        tab.state.searchTerm = 'a"b';
        tab.render();

        expect(document.querySelector('#jux-things-search').value).toBe('a"b');
        expect(window.ApiClient.getJSON).toHaveBeenLastCalledWith(expect.stringContaining('searchTerm=a%22b'));
    });
});

describe('createListTab failure (F-40)', () => {
    afterEach(() => {
        delete window.ApiClient;
    });

    it('shows the error state with a retry that renders again, never the empty state', async () => {
        document.documentElement.lang = 'en';
        document.body.innerHTML = '<div id="jux-tab-things"><div class="sections"></div></div>';
        const getJSON = vi.fn()
            .mockReturnValueOnce(Promise.reject(new Error('offline')))
            .mockReturnValueOnce(new Promise(() => {}));
        window.ApiClient = { getCurrentUserId: () => 'u1', getUrl: (route) => route, getJSON };
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const tab = createListTab({
            id: 'things', endpoint: 'JuxHomepage/Things/Items', logName: 'Things',
            labels: scope('things', { title: 'Things', empty: 'No things', sortName: 'Name' }),
            state: { sortBy: 'Name', sortOrder: 'Ascending' },
            sortOptions: (t) => [{ value: 'Name', label: t.sortName }],
            emptyIcon: 'inbox'
        });

        tab.render();
        await new Promise((resolve) => setTimeout(resolve, 0));

        expect(document.querySelector('.jux-empty-state')).toBeNull();
        expect(document.querySelector('.jux-error-title').textContent).toBe('Couldn’t load Things');
        document.querySelector('.jux-error-retry').click();
        expect(getJSON).toHaveBeenCalledTimes(2);
        errorSpy.mockRestore();
    });

    // BKL-39: a server that never answers shows the same error state once the request delay is over.
    it('shows the error state when the server stays silent', async () => {
        vi.useFakeTimers();
        document.documentElement.lang = 'en';
        document.body.innerHTML = '<div id="jux-tab-things"><div class="sections"></div></div>';
        window.ApiClient = { getCurrentUserId: () => 'u1', getUrl: (route) => route, getJSON: vi.fn(() => new Promise(() => {})) };
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const tab = createListTab({
            id: 'things', endpoint: 'JuxHomepage/Things/Items', logName: 'Things',
            labels: scope('things', { title: 'Things', empty: 'No things', sortName: 'Name' }),
            state: { sortBy: 'Name', sortOrder: 'Ascending' },
            sortOptions: (t) => [{ value: 'Name', label: t.sortName }],
            emptyIcon: 'inbox'
        });

        tab.render();
        await vi.advanceTimersByTimeAsync(15000);

        expect(document.querySelector('.jux-error-title').textContent).toBe('Couldn’t load Things');
        errorSpy.mockRestore();
        vi.useRealTimers();
    });
});

describe('options dialog (F-44)', () => {
    const fields = [{ value: 'Name', label: 'Name' }, { value: 'Date', label: 'Date' }];

    afterEach(() => {
        document.body.innerHTML = '';
    });

    function radios(name) {
        return [...document.querySelectorAll('.jux-sort-dialog input[type="radio"]')].filter((r) => r.name.endsWith('-' + name));
    }

    it('is a labelled modal dialog that takes the focus on the checked option and gives it back', () => {
        document.body.innerHTML = '<button id="opener">Sort</button>';
        const opener = document.getElementById('opener');
        opener.focus();

        const handle = openSortDialog({ title: 'Sort by', sortOptions: fields, currentSortBy: 'Date', currentSortOrder: 'Ascending', onChange: () => {} });
        const dialog = document.querySelector('.jux-sort-dialog');

        expect(dialog.getAttribute('role')).toBe('dialog');
        expect(dialog.getAttribute('aria-modal')).toBe('true');
        expect(document.getElementById(dialog.getAttribute('aria-labelledby')).textContent).toBe('Sort by');
        expect(dialog.contains(document.activeElement)).toBe(true);
        expect(document.activeElement.value).toBe('Date');

        handle.close();
        expect(document.querySelector('.jux-sort-dialog')).toBeNull();
        expect(document.activeElement).toBe(opener);
    });

    it('sorts with a field group and an order group, reporting both values on each change', () => {
        const onChange = vi.fn();
        openSortDialog({ title: 'Sort by', sortOptions: fields, currentSortBy: 'Name', currentSortOrder: 'Ascending', onChange });

        expect(radios('sortBy')).toHaveLength(2);
        expect(radios('sortOrder')).toHaveLength(2);
        const date = radios('sortBy').find((r) => r.value === 'Date');
        date.checked = true;
        date.dispatchEvent(new Event('change'));

        expect(onChange).toHaveBeenCalledWith('Date', 'Ascending');
    });

    it('filters with a single group and no empty order group', () => {
        const onChange = vi.fn();
        openOptionsDialog({ title: 'Filter by Type', groups: [{ name: 'type', options: fields, current: 'Name' }], onChange });

        expect(document.querySelectorAll('.jux-sort-dialog .jux-sort-group')).toHaveLength(1);
        const date = radios('type').find((r) => r.value === 'Date');
        date.checked = true;
        date.dispatchEvent(new Event('change'));

        expect(onChange).toHaveBeenCalledWith({ type: 'Date' });
    });

    it('gives each dialog its own radio names', () => {
        openOptionsDialog({ title: 'A', groups: [{ name: 'type', options: fields, current: 'Name' }] });
        openOptionsDialog({ title: 'B', groups: [{ name: 'type', options: fields, current: 'Date' }] });

        const names = new Set([...document.querySelectorAll('.jux-sort-dialog input')].map((r) => r.name));
        expect(names.size).toBe(2);
        expect(document.querySelectorAll('.jux-sort-dialog input:checked')).toHaveLength(2);
    });
});
