import { describe, it, expect, beforeAll, beforeEach, afterEach, vi } from 'vitest';

// The Modern layout of Jellyfin 12 as the 12.4 bundle renders it (live DOM read on 2026-11-01): a MUI
// header bar whose stack holds the native links, a menu drawer list on narrow screens, and the home
// view #indexPage with the two native panes.
const SCRIPT = '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-tab-modern.js';

function renderModern({ bar = true, drawer = false, home = true } = {}) {
    const link = (href, text) => `<a class="MuiButtonBase-root MuiButton-root css-1f20jcn" tabindex="0" href="${href}">` +
        `<span class="MuiButton-icon MuiButton-startIcon"><svg data-testid="FavoriteIcon"></svg></span>${text}</a>`;
    const item = (href, text, selected) => `<li class="MuiListItem-root"><a class="MuiListItemButton-root${selected ? ' Mui-selected' : ''}" href="${href}">` +
        `<div class="MuiListItemIcon-root"><svg></svg></div><div class="MuiListItemText-root"><span>${text}</span></div></a></li>`;
    document.body.innerHTML =
        '<header class="MuiPaper-root MuiAppBar-root"><div class="MuiToolbar-root">' +
        (bar ? `<div class="MuiStack-root">${link('#/', 'server')}${link('#/home?tab=1', 'Favorites')}${link('#/movies', 'Movies')}</div>` : '') +
        '</div></header>' +
        (drawer ? `<div class="MuiDrawer-paper"><ul class="MuiList-root">${item('#/home', 'Home', true)}${item('#/home?tab=1', 'Favorites')}</ul></div>` : '') +
        (home ? '<div id="indexPage" class="page homePage">' +
            '<div class="tabContent pageTabContent is-active" id="homeTab" data-index="0"><div class="sections"></div></div>' +
            '<div class="tabContent pageTabContent" id="favoritesTab" data-index="1"><div class="sections"></div></div></div>' : '');
}

const modern = () => window.juxTabModern;
const entries = (scope = document) => [...scope.querySelectorAll('.jux-modern-tab')];
const active = () => [...document.querySelectorAll('#indexPage > .tabContent.is-active')].map((p) => p.id);

beforeAll(async () => {
    window.location.hash = '#/details?id=abc';
    await import(SCRIPT);
});

beforeEach(() => {
    document.documentElement.lang = 'en';
    window.location.hash = '#/home';
    window.juxWatchlist = { render: vi.fn() };
    window.juxHistoryTab = { open: vi.fn() };
    window.juxStatistics = { render: vi.fn() };
});

afterEach(() => {
    vi.restoreAllMocks();
    document.body.innerHTML = '';
    delete window.juxWatchlist;
    delete window.juxHistoryTab;
    delete window.juxStatistics;
});

describe('header entries', () => {
    it('adds the three tabs after the native links of the bar, cloned from Favorites, with their own icon', () => {
        renderModern();

        modern().sync();

        const bar = document.querySelector('.MuiStack-root');
        expect([...bar.children].map((a) => a.textContent)).toEqual(['server', 'Favorites', 'Movies', 'Watchlist', 'History', 'Statistics']);
        expect(entries().map((a) => a.getAttribute('href'))).toEqual(['#/home?tab=2', '#/home?tab=3', '#/home?tab=4']);
        expect(entries().map((a) => a.querySelector('.material-icons').className)).toEqual([
            'material-icons bookmark_border', 'material-icons history', 'material-icons insights'
        ]);
        expect(bar.querySelectorAll('.jux-modern-tab svg')).toHaveLength(0);
        expect(entries()[0].className).toContain('MuiButton-root');
    });

    it('adds them to the drawer list too, as list items, never marked selected by the clone', () => {
        renderModern({ bar: false, drawer: true });

        modern().sync();

        const list = document.querySelector('ul.MuiList-root');
        expect([...list.children].map((li) => li.textContent)).toEqual(['Home', 'Favorites', 'Watchlist', 'History', 'Statistics']);
        expect([...list.children].slice(2).every((li) => li.tagName === 'LI')).toBe(true);
        expect(entries(list).some((a) => a.classList.contains('Mui-selected'))).toBe(false);
    });

    it('adds each entry once, and puts them back last when React reorders the bar', () => {
        renderModern();
        modern().sync();
        const bar = document.querySelector('.MuiStack-root');
        bar.insertBefore(bar.querySelector('a[href="#/movies"]'), null);
        bar.insertBefore(entries()[1], bar.firstChild);

        modern().sync();

        expect(entries()).toHaveLength(3);
        expect([...bar.children].map((a) => a.textContent)).toEqual(['server', 'Favorites', 'Movies', 'Watchlist', 'History', 'Statistics']);
    });

    it('labels the entries in the language of the page', () => {
        document.documentElement.lang = 'fr';
        renderModern();

        modern().sync();

        expect(entries().map((a) => a.textContent)).toEqual(['Watchlist', 'Historique', 'Statistiques']);
    });

    it('does nothing outside the Modern layout or for a user who turned JellyUX off', () => {
        document.body.innerHTML = '<div class="emby-tabs-slider"></div><div id="favoritesTab"></div>';
        modern().sync();
        expect(document.querySelectorAll('.jux-modern-tab, [data-jux-client]')).toHaveLength(0);

        renderModern();
        vi.spyOn(window.JuxCore, 'isOffForUser').mockReturnValue(true);
        modern().sync();
        expect(document.querySelectorAll('.jux-modern-tab, [data-jux-client]')).toHaveLength(0);
    });
});

// R11: the Modern header lives through the whole session, so the entries a user who turns JellyUX off
// was given must go, with the panes, and the native pane of the address comes back.
describe('JellyUX turned off for the user', () => {
    it('takes back the entries and the panes it added, and gives the native home back', () => {
        renderModern({ drawer: true });
        window.location.hash = '#/home?tab=3';
        modern().sync();
        expect(entries()).toHaveLength(6);

        vi.spyOn(window.JuxCore, 'isOffForUser').mockReturnValue(true);
        window.location.hash = '#/home';
        modern().sync();

        expect(document.querySelectorAll('.jux-modern-tab, [data-jux-tab], [data-jux-client]')).toHaveLength(0);
        expect([...document.querySelector('ul.MuiList-root').children].map((li) => li.textContent)).toEqual(['Home', 'Favorites']);
        expect(document.querySelector('ul.MuiList-root').classList.contains('jux-modern-tab-open')).toBe(false);
        expect(active()).toEqual(['homeTab']);
    });
});

describe('panes', () => {
    it('creates the three panes after #favoritesTab, marked as made by the client', () => {
        renderModern();

        modern().sync();

        const panes = [...document.querySelectorAll('#indexPage > .tabContent')];
        expect(panes.map((p) => `${p.id}:${p.getAttribute('data-index')}`)).toEqual([
            'homeTab:0', 'favoritesTab:1', 'jux-tab-watchlist:2', 'jux-tab-history:3', 'jux-tab-statistics:4'
        ]);
        expect(panes.slice(2).every((p) => p.hasAttribute('data-jux-client') && p.querySelector('.sections'))).toBe(true);
        expect(active()).toEqual(['homeTab']);
    });

    it.each([
        ['#/home?tab=2', 'jux-tab-watchlist', 'juxWatchlist', 'render'],
        ['#/home?tab=3', 'jux-tab-history', 'juxHistoryTab', 'open'],
        ['#/home?tab=4&x=1', 'jux-tab-statistics', 'juxStatistics', 'render']
    ])('opens %s: only %s is active, its entry is current, and it is filled once', (hash, pane, owner, method) => {
        renderModern({ drawer: true });
        window.location.hash = hash;

        modern().sync();
        modern().sync();

        expect(active()).toEqual([pane]);
        expect(window[owner][method]).toHaveBeenCalledTimes(1);
        const current = entries().filter((a) => a.getAttribute('aria-current') === 'page');
        expect(current.map((a) => a.getAttribute('href'))).toEqual([`#/home?tab=${hash.match(/tab=(\d)/)[1]}`, `#/home?tab=${hash.match(/tab=(\d)/)[1]}`]);
        expect(current.filter((a) => a.classList.contains('Mui-selected'))).toHaveLength(1);
        expect(document.querySelector('ul.MuiList-root').classList.contains('jux-modern-tab-open')).toBe(true);
    });

    it('fills the tab again when it is opened again, and when React replaced its pane', () => {
        renderModern();
        window.location.hash = '#/home?tab=2';
        modern().sync();
        window.location.hash = '#/home?tab=1';
        modern().sync();
        window.location.hash = '#/home?tab=2';
        modern().sync();
        expect(window.juxWatchlist.render).toHaveBeenCalledTimes(2);

        document.getElementById('jux-tab-watchlist').remove();
        modern().sync();

        expect(window.juxWatchlist.render).toHaveBeenCalledTimes(3);
        expect(active()).toEqual(['jux-tab-watchlist']);
    });

    it('gives the native pane of the address back when a JellyUX tab is left', () => {
        renderModern({ drawer: true });
        window.location.hash = '#/home?tab=3';
        modern().sync();

        window.location.hash = '#/home';
        modern().sync();
        expect(active()).toEqual(['homeTab']);
        expect(entries().some((a) => a.hasAttribute('aria-current'))).toBe(false);
        expect(document.querySelector('ul.MuiList-root').classList.contains('jux-modern-tab-open')).toBe(false);

        window.location.hash = '#/home?tab=3';
        modern().sync();
        window.location.hash = '#/home?tab=1';
        modern().sync();
        expect(active()).toEqual(['favoritesTab']);
    });

    it('leaves the native panes alone when the native home switched itself', () => {
        renderModern();
        window.location.hash = '#/home?tab=3';
        modern().sync();
        document.getElementById('favoritesTab').classList.add('is-active');

        window.location.hash = '#/home?tab=1';
        modern().sync();

        expect(active()).toEqual(['favoritesTab']);
    });

    it('keeps the entries but creates no pane away from the home page', () => {
        renderModern();
        window.location.hash = '#/movies?topParentId=a';

        modern().sync();

        expect(entries()).toHaveLength(3);
        expect(document.querySelectorAll('[data-jux-client]')).toHaveLength(0);
    });
});

describe('tabOf', () => {
    it.each([
        ['#/home?tab=2', 2],
        ['#/home.html?tab=4', 4],
        ['#/home?serverId=s&tab=3', 3],
        ['#/home?tab=1', null],
        ['#/home?tab=5', null],
        ['#/home?tab=23', null],
        ['#/home', null],
        ['#/movies?tab=2', null]
    ])('reads %s as %s', (hash, index) => {
        expect(modern().tabOf(hash)).toBe(index);
    });
});

describe('Legacy tab bar', () => {
    it('is left to the Modern header: the hidden legacy bar gets no button', async () => {
        await import('../src/Jellyfin.Plugin.JuxHomepage/Web/jux-tab-injector.js');
        renderModern();
        document.body.insertAdjacentHTML('beforeend', '<div class="headerTabs"><div class="emby-tabs-slider"></div></div>');
        modern().sync();

        window.juxTabInjector.waitForUI();

        expect(document.querySelectorAll('[id^="jux-tabbtn-"]')).toHaveLength(0);
    });
});
