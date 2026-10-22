import { describe, it, expect, afterEach, vi } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import juxCore from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-core.js';

const { escHtml, cssUrl, iconName, cached, getItem, getIncludedIn, observeCardImages, errorHtml, showError } = juxCore;

describe('escHtml (F-35)', () => {
    it('escapes ampersands, angle brackets, double quotes and apostrophes', () => {
        expect(escHtml('<b>Tom & "Jerry"</b>')).toBe('&lt;b&gt;Tom &amp; &quot;Jerry&quot;&lt;/b&gt;');
        expect(escHtml("L'Odyssée")).toBe('L&#39;Odyssée');
    });

    it('keeps a single-quoted attribute closed on an apostrophe', () => {
        const div = document.createElement('div');
        div.innerHTML = "<span title='" + escHtml("x' onmouseover='alert(1)") + "'></span>";

        expect(div.firstChild.getAttribute('onmouseover')).toBeNull();
        expect(div.firstChild.getAttribute('title')).toBe("x' onmouseover='alert(1)");
    });

    it('returns an empty string for nullish input and keeps zero', () => {
        expect(escHtml('')).toBe('');
        expect(escHtml(null)).toBe('');
        expect(escHtml(undefined)).toBe('');
        expect(escHtml(0)).toBe('0');
    });
});

describe('cssUrl (F-35)', () => {
    it('encodes parentheses, quotes and spaces so the url() token stays closed', () => {
        expect(cssUrl("/Items/1/Images/Primary?tag=a(b)'c\" d")).toBe('url("/Items/1/Images/Primary?tag=a%28b%29%27c%22%20d")');
    });

    it('keeps existing percent escapes', () => {
        expect(cssUrl('/img/a%20b.jpg')).toBe('url("/img/a%20b.jpg")');
    });

    it('applies as a single background image in the DOM', () => {
        const el = document.createElement('div');
        el.style.backgroundImage = cssUrl('/x/a)b.jpg');

        expect(el.style.backgroundImage).toContain('a%29b.jpg');
    });
});

describe('iconName (F-35)', () => {
    it('accepts Material ligature names', () => {
        expect(iconName('bookmark_border')).toBe('bookmark_border');
    });

    it('falls back on anything that could add classes or attributes', () => {
        expect(iconName('info" onclick="x')).toBe('info');
        expect(iconName('a b')).toBe('info');
        expect(iconName(undefined, 'movie')).toBe('movie');
    });
});

describe('cached', () => {
    it('sends a request once per item and key, and again for another item', () => {
        let calls = 0;
        const request = () => { calls += 1; return Promise.resolve(calls); };

        cached('a', 'item', request);
        cached('a', 'item', request);
        cached('b', 'item', request);

        expect(calls).toBe(2);
    });

    it('lets collections and the breadcrumb share one getItem and one Included In per item', () => {
        window.ApiClient = {
            getUrl: path => path,
            getItem: vi.fn().mockResolvedValue({ Type: 'Movie' }),
            getJSON: vi.fn().mockResolvedValue([])
        };

        getItem('user1', 'shared1');
        getIncludedIn('shared1');
        getItem('user1', 'shared1');
        getIncludedIn('shared1');

        expect(window.ApiClient.getItem).toHaveBeenCalledTimes(1);
        expect(window.ApiClient.getJSON).toHaveBeenCalledTimes(1);
        expect(window.ApiClient.getJSON).toHaveBeenCalledWith('JuxHomepage/Collections/IncludedIn/shared1');
        delete window.ApiClient;
    });
});

describe('getStatistics (Annexe.a)', () => {
    afterEach(() => {
        vi.useRealTimers();
        delete window.ApiClient;
    });

    function client(getJSON) {
        window.ApiClient = { getUrl: (route, query) => route + '?userId=' + query.userId, getJSON };
        return getJSON;
    }

    it('serves the History tiles and the Statistics tab with one request for a minute', async () => {
        vi.useFakeTimers();
        const getJSON = client(vi.fn().mockResolvedValue({ MoviesWatched: 3 }));

        const history = await juxCore.getStatistics('stats1');
        const statistics = await juxCore.getStatistics('stats1');
        expect(getJSON).toHaveBeenCalledTimes(1);
        expect(getJSON).toHaveBeenCalledWith('JuxHomepage/Statistics?userId=stats1');
        expect(statistics).toBe(history);

        vi.advanceTimersByTime(60000);
        await juxCore.getStatistics('stats1');
        expect(getJSON).toHaveBeenCalledTimes(2);
    });

    it('asks again after a failure, and for another user', async () => {
        const getJSON = client(vi.fn().mockRejectedValueOnce(new Error('down')).mockResolvedValue({}));

        await expect(juxCore.getStatistics('stats2')).rejects.toThrow('down');
        await juxCore.getStatistics('stats2');
        await juxCore.getStatistics('stats3');

        expect(getJSON.mock.calls.map((c) => c[0])).toEqual([
            'JuxHomepage/Statistics?userId=stats2', 'JuxHomepage/Statistics?userId=stats2', 'JuxHomepage/Statistics?userId=stats3']);
    });
});

describe('observeCardImages (F-10, F-35)', () => {
    const originalObserver = globalThis.IntersectionObserver;
    let observers = [];

    afterEach(() => {
        globalThis.IntersectionObserver = originalObserver;
        observers = [];
    });

    function installObserver() {
        globalThis.IntersectionObserver = class {
            constructor(callback) { this.callback = callback; this.targets = []; this.disconnected = false; observers.push(this); }
            observe(el) { this.targets.push(el); }
            unobserve(el) { this.targets = this.targets.filter(t => t !== el); }
            disconnect() { this.disconnected = true; this.targets = []; }
        };
    }

    function cards(count) {
        const container = document.createElement('div');
        container.innerHTML = Array.from({ length: count }, (_, i) =>
            '<div class="cardImageContainer lazy" data-src="/Items/' + i + '/Images/Primary"></div>').join('');
        return container;
    }

    it('applies only the images of cards that come into view', () => {
        installObserver();
        const container = cards(100);

        observeCardImages(container);
        const visible = observers[0].targets.slice(0, 10);
        observers[0].callback(visible.map(target => ({ target, isIntersecting: true })));

        expect(container.querySelectorAll('.cardImageContainer:not(.lazy)').length).toBe(10);
        expect(visible[0].style.backgroundImage).toContain('/Items/0/Images/Primary');
        expect(observers[0].targets.length).toBe(90);
    });

    it('drops the observer of the previous render of the same container', () => {
        installObserver();
        const container = cards(3);

        observeCardImages(container);
        container.innerHTML = cards(2).innerHTML;
        observeCardImages(container);

        expect(observers[0].disconnected).toBe(true);
        expect(observers[1].targets.length).toBe(2);
    });

    it('applies every image at once without IntersectionObserver', () => {
        globalThis.IntersectionObserver = undefined;
        const container = cards(3);

        observeCardImages(container);

        expect(container.querySelectorAll('.cardImageContainer.lazy').length).toBe(0);
    });
});

describe('error state (F-40)', () => {
    it('names what failed, says what to do and offers a native raised retry button', () => {
        document.documentElement.lang = 'en';
        const div = document.createElement('div');
        div.innerHTML = errorHtml({ name: '<Watchlist>' });

        expect(div.querySelector('[role="alert"]')).not.toBeNull();
        expect(div.querySelector('.jux-error-title').textContent).toBe('Couldn’t load <Watchlist>');
        expect(div.querySelector('.jux-error-subtitle').textContent).toContain('try again');
        expect(div.querySelector('button.raised.emby-button.jux-error-retry').textContent).toBe('Retry');
    });

    it('renders one line without subtitle inside a home section, in French', () => {
        document.documentElement.lang = 'fr-FR';
        const div = document.createElement('div');
        div.innerHTML = errorHtml({ compact: true });

        expect(div.querySelector('.jux-error-state-compact .jux-error-title').textContent).toBe('Impossible de charger cette section.');
        expect(div.querySelector('.jux-error-subtitle')).toBeNull();
        expect(div.querySelector('.jux-error-retry').textContent).toBe('Réessayer');
        document.documentElement.lang = 'en';
    });

    it('calls onRetry once and disables the button', () => {
        const container = document.createElement('div');
        const onRetry = vi.fn();
        showError(container, { name: 'History', onRetry });

        const button = container.querySelector('.jux-error-retry');
        button.click();
        button.click();

        expect(onRetry).toHaveBeenCalledTimes(1);
        expect(button.disabled).toBe(true);
    });
});

describe('turned off for this user (F-36)', () => {
    afterEach(() => {
        delete window.ApiClient;
        window.sessionStorage.clear();
    });

    it('remembers the answer per user for the session, and unknown means on', () => {
        window.ApiClient = { getCurrentUserId: () => 'alice' };
        expect(juxCore.isOffForUser()).toBe(false);

        juxCore.setUserEnabled('alice', false);
        expect(juxCore.isOffForUser()).toBe(true);
        window.ApiClient = { getCurrentUserId: () => 'bob' };
        expect(juxCore.isOffForUser()).toBe(false);

        window.ApiClient = { getCurrentUserId: () => 'alice' };
        juxCore.setUserEnabled('alice', true);
        expect(juxCore.isOffForUser()).toBe(false);
    });
});

describe('labels (F-64)', () => {
    afterEach(() => {
        delete window.JuxI18n.de;
        document.documentElement.lang = '';
    });

    it('falls back to English for a language without a dictionary, then reads it once one is served', () => {
        document.documentElement.lang = 'de-DE';
        expect(juxCore.resolveLang()).toBe('en');
        expect(juxCore.t('client.list.more')).toBe('Load more');

        window.JuxI18n.de = { 'client.list.more': 'Mehr laden' };

        expect(juxCore.resolveLang()).toBe('de');
        expect(juxCore.t('client.list.more')).toBe('Mehr laden');
        // A key the German file lacks still reads in English.
        expect(juxCore.labels('list').search).toBe('Search');
    });

    it('fills placeholders and reads the language of another document', () => {
        expect(juxCore.t('client.list.count', { shown: 50, total: 120 }, 'fr')).toBe('50 sur 120');
        expect(juxCore.t('client.unknown.key')).toBe('client.unknown.key');
    });

    it('builds the labels of a scope once, and again when another dictionary is served (Annexe.a)', () => {
        const first = juxCore.labels('list', 'de');
        expect(juxCore.labels('list', 'de')).toBe(first);

        window.JuxI18n.de = { 'client.list.more': 'Mehr laden' };

        const served = juxCore.labels('list', 'de');
        expect(served).not.toBe(first);
        expect(served.more).toBe('Mehr laden');
        expect(juxCore.labels('list', 'de')).toBe(served);

        window.JuxI18n.de = { 'client.list.more': 'Weitere laden' };
        expect(juxCore.labels('list', 'de').more).toBe('Weitere laden');
    });

    it('leaves no label dictionary in the client scripts', () => {
        const web = resolve(__dirname, '../src/Jellyfin.Plugin.JuxHomepage/Web');
        const offenders = readdirSync(web)
            .filter((file) => /^jux-.*\.js$/.test(file))
            .filter((file) => /\b(en|fr): \{|=== 'fr'|labelFr/.test(readFileSync(resolve(web, file), 'utf8')));
        expect(offenders).toEqual([]);
    });
});

describe('onDomMutation (QUAL-04)', () => {
    const flush = () => new Promise(resolve => setTimeout(resolve, 0));
    let created = 0;
    const Native = window.MutationObserver;

    afterEach(() => {
        window.MutationObserver = Native;
        created = 0;
        document.body.innerHTML = '';
        location.hash = '';
        delete window.ApiClient;
        window.sessionStorage.clear();
    });

    function countObservers() {
        window.MutationObserver = class extends Native {
            constructor(callback) {
                super(callback);
                created++;
            }
        };
    }

    it('serves every subscriber from one observer, a throwing one included, and disconnects when none is left', async () => {
        countObservers();
        const errors = vi.spyOn(console, 'error').mockImplementation(() => {});
        const first = vi.fn(() => { throw new Error('boom'); });
        const second = vi.fn();
        const offFirst = juxCore.onDomMutation(first);
        const offSecond = juxCore.onDomMutation(second);

        document.body.appendChild(document.createElement('div'));
        await flush();
        expect(created).toBe(1);
        expect(first).toHaveBeenCalledTimes(1);
        expect(second).toHaveBeenCalledTimes(1);
        expect(second.mock.calls[0][0][0].addedNodes).toHaveLength(1);

        offFirst();
        offSecond();
        document.body.appendChild(document.createElement('div'));
        await flush();
        expect(second).toHaveBeenCalledTimes(1);
        errors.mockRestore();
    });

    it('lets several detail-page modules share that observer', async () => {
        countObservers();
        window.ApiClient = { getCurrentUserId: () => 's1' };
        window.sessionStorage.setItem('jux-user-off:s1', '0');
        location.hash = '#/details?id=ab12';
        const modules = [vi.fn(), vi.fn(), vi.fn()];
        const syncs = modules.map((onChange) => juxCore.watchDetailPages(onChange));
        await flush();

        document.body.appendChild(document.createElement('div'));
        await flush();
        expect(created).toBe(1);
        modules.forEach((onChange) => expect(onChange).toHaveBeenCalledTimes(2));

        location.hash = '#/home';
        syncs.forEach((sync) => sync());
        document.body.appendChild(document.createElement('div'));
        await flush();
        modules.forEach((onChange) => expect(onChange).toHaveBeenCalledTimes(2));
    });
});

describe('runInFlight (QUAL-04)', () => {
    it('starts once per id while in flight, again for a newer id, and clears only its own mark', async () => {
        const el = document.createElement('div');
        let resolveFirst;
        const start = vi.fn(() => new Promise((resolve) => { resolveFirst = resolve; }));

        const first = juxCore.runInFlight(el, 'juxPending', 'a', start);
        expect(juxCore.runInFlight(el, 'juxPending', 'a', start)).toBeNull();
        expect(start).toHaveBeenCalledTimes(1);

        let resolveNewer;
        const newer = juxCore.runInFlight(el, 'juxPending', 'b', () => new Promise((resolve) => { resolveNewer = resolve; }));
        resolveFirst();
        await first;
        expect(el.dataset.juxPending).toBe('b');
        resolveNewer();
        await newer;
        expect(el.dataset.juxPending).toBeUndefined();
    });

    it('clears the mark when start throws', () => {
        const el = document.createElement('div');
        expect(() => juxCore.runInFlight(el, 'juxPending', 'a', () => { throw new Error('boom'); })).toThrow('boom');
        expect(el.dataset.juxPending).toBeUndefined();
    });
});

describe('fetchWithTimeout (FIAB-08)', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('answers with the server, and rejects once the limit passes without an answer', async () => {
        vi.useFakeTimers();
        const fast = { getJSON: vi.fn().mockResolvedValue({ ok: 1 }) };
        await expect(juxCore.fetchWithTimeout(fast, 'a')).resolves.toEqual({ ok: 1 });
        expect(vi.getTimerCount()).toBe(0);

        let answer;
        const silent = { getJSON: vi.fn(() => new Promise((resolve) => { answer = resolve; })) };
        const pending = juxCore.fetchWithTimeout(silent, 'b');
        const outcome = pending.then(() => 'answered', (err) => err.message);
        await vi.advanceTimersByTimeAsync(15000);
        answer({ late: true });

        expect(await outcome).toBe('No answer within 15000 ms: b');
    });

    it('passes a failed request through, a synchronous throw included', async () => {
        const failing = { getJSON: vi.fn().mockRejectedValue(new Error('500')) };
        const throwing = { getJSON: () => { throw new Error('offline'); } };
        await expect(juxCore.fetchWithTimeout(failing, 'c')).rejects.toThrow('500');
        await expect(juxCore.fetchWithTimeout(throwing, 'd')).rejects.toThrow('offline');
    });
});

describe('turned off for this user, on a detail page opened directly (H-09)', () => {
    const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
    const mutate = async () => { document.body.appendChild(document.createElement('div')); await flush(); };

    // JuxCore remembers per user, for the page's life, a state it could not read: a test that needs
    // a fresh user passes its own id.
    function signIn(getJSON, userId = 'h1') {
        window.sessionStorage.clear();
        window.ApiClient = { getCurrentUserId: () => userId, getUrl: (path) => path, getJSON };
        location.hash = '#/details?id=ab901';
    }

    afterEach(() => {
        location.hash = '';
        delete window.ApiClient;
        window.sessionStorage.clear();
        document.body.innerHTML = '';
    });

    it('reads UserSettings once when no other answer is coming, and arms nothing for a user who turned JellyUX off', async () => {
        const getJSON = vi.fn().mockResolvedValue({ Enabled: false, Sections: [] });
        signIn(getJSON);
        const modules = [vi.fn(), vi.fn(), vi.fn()];
        modules.forEach((onChange) => juxCore.watchDetailPages(onChange));
        await flush();
        await mutate();

        modules.forEach((onChange) => expect(onChange).not.toHaveBeenCalled());
        expect(getJSON.mock.calls.map((c) => c[0])).toEqual(['JuxHomepage/UserSettings']);
    });

    it('waits for the answer another module already asked for, without a request of its own', async () => {
        const getJSON = vi.fn();
        signIn(getJSON);
        let answer;
        juxCore.reportUserStateRequest('h1', new Promise((resolve) => { answer = resolve; })
            .then(() => juxCore.setUserEnabled('h1', true)));
        const onChange = vi.fn();
        juxCore.watchDetailPages(onChange);
        await flush();
        expect(onChange).not.toHaveBeenCalled();

        answer();
        await flush();
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(getJSON).not.toHaveBeenCalled();
    });

    it('asks a registered provider before reading UserSettings, and reads it only when no provider sent a request', async () => {
        const getJSON = vi.fn().mockResolvedValue({ Enabled: true, Sections: [] });
        signIn(getJSON);
        // JuxCore keeps its providers for the page's life: this one stops answering after the test.
        let active = true;
        const provider = vi.fn((userId) => active && juxCore.reportUserStateRequest(userId, Promise.resolve().then(() => juxCore.setUserEnabled(userId, true))));
        juxCore.provideUserState(provider);
        const onChange = vi.fn();
        juxCore.watchDetailPages(onChange);
        await flush();

        expect(provider).toHaveBeenCalledWith('h1');
        expect(getJSON).not.toHaveBeenCalled();
        expect(onChange).toHaveBeenCalledTimes(1);
        active = false;
    });

    it('reads UserSettings once per tab when it fails, however many mutations follow (R11)', async () => {
        const getJSON = vi.fn().mockRejectedValue(new Error('502'));
        signIn(getJSON);
        vi.spyOn(console, 'error').mockImplementation(() => {});
        const onChange = vi.fn();
        juxCore.watchDetailPages(onChange);
        await flush();
        for (let i = 0; i < 5; i++) {
            await mutate();
        }

        expect(getJSON).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledTimes(6);
        vi.restoreAllMocks();
    });

    it('runs a module once when the answer arrives, not once per mutation that waited for it (R11)', async () => {
        signIn(vi.fn(), 'h3');
        let answer;
        juxCore.reportUserStateRequest('h3', new Promise((resolve) => { answer = resolve; })
            .then(() => juxCore.setUserEnabled('h3', true)));
        const onChange = vi.fn();
        juxCore.watchDetailPages(onChange);
        await flush();
        for (let i = 0; i < 20; i++) {
            await mutate();
        }

        answer();
        await flush();
        expect(onChange).toHaveBeenCalledTimes(1);
    });

    it('leaves the detail page when an off answer arrives after the modules armed', async () => {
        signIn(vi.fn());
        juxCore.setUserEnabled('h1', true);
        const onChange = vi.fn();
        const onLeave = vi.fn();
        juxCore.watchDetailPages(onChange, onLeave);
        await flush();
        expect(onChange).toHaveBeenCalledTimes(1);

        juxCore.setUserEnabled('h1', false);
        await flush();
        await mutate();
        expect(onLeave).toHaveBeenCalled();
        expect(onChange).toHaveBeenCalledTimes(1);
    });
});

describe('error state without labels (Annexe.b)', () => {
    it('renders a retry button instead of throwing when the error labels are missing', () => {
        const dictionaries = window.JuxI18n;
        window.JuxI18n = {};
        try {
            const html = errorHtml({ name: 'Watchlist' });
            expect(html).toContain('jux-error-retry');
        } finally {
            window.JuxI18n = dictionaries;
        }
    });
});
