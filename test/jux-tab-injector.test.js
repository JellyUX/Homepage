import { describe, it, expect, beforeAll, beforeEach, afterEach, vi } from 'vitest';

// jux-tab-injector.js exports nothing: it installs window.juxTabInjector, arms its navigation
// listeners once, and switches the panes through its private _activatePane, reached here through
// the button click handler and the tab bar MutationObserver, the same way the browser reaches it.
const SCRIPT = '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-tab-injector.js';

const EXPECTED_TABS = [
    { id: 'jux-tabbtn-watchlist', index: '2', en: 'Watchlist', fr: 'Watchlist' },
    { id: 'jux-tabbtn-progress', index: '3', en: 'Progress', fr: 'Progression' },
    { id: 'jux-tabbtn-history', index: '4', en: 'History', fr: 'Historique' },
    { id: 'jux-tabbtn-statistics', index: '5', en: 'Statistics', fr: 'Statistiques' }
];

// The home page as Jellyfin renders it: the native Home/Favorites buttons in the header, then the
// home view (#indexPage) holding the 6 panes in DOM order (2 native, 4 spliced server-side by
// TransformationPatches.HomeHtmlChunk). thirdPartyTab inserts another plugin's button and pane
// before the JellyUX ones; cachedViewBefore keeps another view with its own .tabContent panes
// earlier in the document, as Jellyfin does for up to 3 views.
function renderHomePage({ withSlider = true, thirdPartyTab = false, cachedViewBefore = false } = {}) {
    const slider = withSlider
        ? '<div class="emby-tabs-slider">'
            + '<button class="emby-tab-button emby-tab-button-active" data-index="0"></button>'
            + '<button class="emby-tab-button" data-index="1"></button>'
            + (thirdPartyTab ? '<button class="emby-tab-button" id="other-tabbtn" data-index="2"></button>' : '')
            + '</div>'
        : '';
    const native = [0, 1]
        .map((i) => `<div class="tabContent pageTabContent${i === 0 ? ' is-active' : ''}" data-index="${i}"></div>`)
        .join('');
    const other = thirdPartyTab ? '<div class="tabContent pageTabContent" id="other-tab"></div>' : '';
    const jux = ['watchlist', 'progress', 'history', 'statistics']
        .map((name, i) => `<div class="tabContent pageTabContent" id="jux-tab-${name}" data-index="${i + 2}"></div>`)
        .join('');
    const cached = cachedViewBefore
        ? '<div class="page hide" id="moviesPage"><div class="tabContent" id="cached-0"></div>'
            + '<div class="tabContent" id="cached-1"></div><div class="tabContent" id="cached-2"></div></div>'
        : '';
    document.body.innerHTML = `<div class="headerTabs">${slider}</div>${cached}`
        + `<div class="page homePage" id="indexPage">${native}${other}${jux}</div>`;
}

const injector = () => window.juxTabInjector;
const juxButtons = () => [...document.querySelectorAll('.emby-tabs-slider [id^="jux-tabbtn-"]')];
const activePaneIndexes = () =>
    [...document.querySelectorAll('.tabContent.is-active')].map((p) => p.getAttribute('data-index'));
const activePaneIds = () => [...document.querySelectorAll('.tabContent.is-active')].map((p) => p.id);

beforeAll(async () => {
    // A non-home route at load time, so the init() run on import returns without polling.
    window.location.hash = '#/details?id=abc';
    await import(SCRIPT);
});

beforeEach(() => {
    vi.useFakeTimers();
    document.documentElement.lang = 'en';
    window.location.hash = '#/home';
    vi.clearAllTimers();
});

afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
    vi.restoreAllMocks();
    document.body.innerHTML = '';
});

describe('createTabs', () => {
    it('appends the 4 JellyUX buttons after the native tabs, with data-index 2..5', () => {
        renderHomePage();

        injector().createTabs();

        const buttons = [...document.querySelectorAll('.emby-tabs-slider button')];
        expect(buttons.map((b) => b.getAttribute('data-index'))).toEqual(['0', '1', '2', '3', '4', '5']);
        expect(juxButtons().map((b) => b.id)).toEqual(EXPECTED_TABS.map((t) => t.id));
        juxButtons().forEach((button) => {
            expect(button.className).toBe('emby-tab-button emby-button');
            expect(button.type).toBe('button');
            expect(button.getAttribute('is')).toBe('emby-button');
            expect(button.querySelector('.emby-button-foreground')).not.toBeNull();
        });
    });

    it.each([
        ['en', 'en'],
        ['fr-FR', 'fr'],
        ['', 'en']
    ])('labels the buttons for lang "%s" in %s', (lang, expected) => {
        document.documentElement.lang = lang;
        renderHomePage();

        injector().createTabs();

        expect(juxButtons().map((b) => b.textContent)).toEqual(EXPECTED_TABS.map((t) => t[expected]));
    });

    it('does not inject twice into the same tab bar', () => {
        renderHomePage();

        injector().createTabs();
        injector().createTabs();

        expect(juxButtons()).toHaveLength(4);
    });

    it('numbers each button after the real position of its pane when another tab comes first', () => {
        renderHomePage({ thirdPartyTab: true });

        injector().createTabs();

        expect(juxButtons().map((b) => b.getAttribute('data-index'))).toEqual(['3', '4', '5', '6']);
    });

    it('creates no button for a pane missing from the home view', () => {
        renderHomePage();
        document.getElementById('jux-tab-history').remove();

        injector().createTabs();

        expect(juxButtons().map((b) => b.id)).toEqual(['jux-tabbtn-watchlist', 'jux-tabbtn-progress', 'jux-tabbtn-statistics']);
    });

    it('does nothing when the tab bar is absent', () => {
        renderHomePage({ withSlider: false });

        expect(() => injector().createTabs()).not.toThrow();
        expect(document.querySelectorAll('[id^="jux-tabbtn-"]')).toHaveLength(0);
    });
});

describe('pane activation', () => {
    it.each(EXPECTED_TABS)('clicking $id activates only the pane at DOM position $index', ({ id, index }) => {
        renderHomePage();
        injector().createTabs();

        document.getElementById(id).click();
        expect(activePaneIndexes()).toEqual(['0']);

        vi.advanceTimersByTime(50);

        expect(activePaneIndexes()).toEqual([index]);
    });

    it('activates the pane as soon as the native controller marks a JellyUX button active', async () => {
        renderHomePage();
        injector().createTabs();

        document.getElementById('jux-tabbtn-history').classList.add('emby-tab-button-active');
        await Promise.resolve();

        expect(activePaneIndexes()).toEqual(['4']);
    });

    it('opens its own pane when another plugin tab sits before the JellyUX tabs', () => {
        renderHomePage({ thirdPartyTab: true });
        injector().createTabs();

        document.getElementById('jux-tabbtn-watchlist').click();
        vi.advanceTimersByTime(50);

        expect(activePaneIds()).toEqual(['jux-tab-watchlist']);
    });

    it('leaves the panes of a cached view untouched', async () => {
        renderHomePage({ cachedViewBefore: true });
        injector().createTabs();

        document.getElementById('jux-tabbtn-progress').classList.add('emby-tab-button-active');
        await Promise.resolve();

        expect(activePaneIds()).toEqual(['jux-tab-progress']);
    });

    it('ignores class changes on the native buttons', async () => {
        renderHomePage();
        injector().createTabs();

        document.querySelector('.emby-tab-button[data-index="1"]').classList.add('emby-tab-button-active');
        await Promise.resolve();

        expect(activePaneIndexes()).toEqual(['0']);
    });
});

describe('waitForUI', () => {
    it.each(['', '#/home', '#/home.html', '#/home?tab=1', '#/home.html?tab=1'])(
        'injects on the home route "%s"',
        (hash) => {
            window.location.hash = hash;
            renderHomePage();

            injector().waitForUI();

            expect(juxButtons()).toHaveLength(4);
        }
    );

    it.each(['#/details?id=abc', '#/homevideos', '#/movies.html'])('skips the non-home route "%s"', (hash) => {
        window.location.hash = hash;
        renderHomePage();

        injector().waitForUI();
        vi.advanceTimersByTime(1000);

        expect(juxButtons()).toHaveLength(0);
    });

    it('polls every 200 ms until the tab bar is rendered', () => {
        renderHomePage({ withSlider: false });

        injector().waitForUI();
        vi.advanceTimersByTime(400);
        renderHomePage();
        expect(juxButtons()).toHaveLength(0);

        vi.advanceTimersByTime(200);

        expect(juxButtons()).toHaveLength(4);
    });
});

describe('navigation re-arming', () => {
    it('re-injects into the tab bar rebuilt after a pushState navigation', () => {
        renderHomePage();
        injector().createTabs();

        renderHomePage();
        history.pushState(null, '', '#/home');
        vi.advanceTimersByTime(799);
        expect(juxButtons()).toHaveLength(0);

        vi.advanceTimersByTime(1);

        expect(juxButtons()).toHaveLength(4);
    });

    it.each([
        ['replaceState', () => history.replaceState(null, '', '#/home'), 800],
        ['focus', () => window.dispatchEvent(new Event('focus')), 800],
        ['popstate', () => window.dispatchEvent(new PopStateEvent('popstate')), 800],
        ['visibilitychange', () => document.dispatchEvent(new Event('visibilitychange')), 300]
    ])('re-runs init after %s', (_name, trigger, delay) => {
        const init = vi.spyOn(injector(), 'init');

        trigger();
        vi.advanceTimersByTime(delay - 1);
        expect(init).not.toHaveBeenCalled();

        vi.advanceTimersByTime(1);

        expect(init).toHaveBeenCalledTimes(1);
    });

    it('arms its listeners only once when the script is loaded twice', async () => {
        const first = injector();
        vi.resetModules();
        await import(SCRIPT);
        const init = vi.spyOn(injector(), 'init');

        history.pushState(null, '', '#/home');
        vi.advanceTimersByTime(800);

        expect(injector()).toBe(first);
        expect(init).toHaveBeenCalledTimes(1);
    });
});
