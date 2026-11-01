'use strict';

// The JellyUX tabs in the Modern layout of Jellyfin 12 (D-13, DR-025). That layout builds its header
// with React and MUI: the legacy tab bar is still in the document but hidden, and the home page never
// loads the home-html template the server splices the tab panes into. So this script, on the model of
// Custom Tabs 0.3.2.0 (technique only, no code copied):
// - adds one entry per tab to the header bar (wide screens) and to the menu drawer (narrow screens),
//   each cloned from the native Favorites entry (#/home?tab=1) and pointing at #/home?tab=2..4;
// - creates the three panes after #favoritesTab, as the server does for the Legacy layout, and makes
//   the one the address names the active pane (the native home hides the panes that are not active);
// - fills a pane each time its tab is opened, as a click on a Legacy tab button does.
// React re-renders the header on navigation and drops what it does not own, so the shared body observer
// (JuxCore.onDomMutation) puts the entries back; a pass that changes nothing adds nothing, so the pass its
// own changes trigger is the last one. The MUI classes are matched by their stable component names, never by the Emotion
// hashes (css-xxxx) that change with each jellyfin-web build; they are inventoried with the bundle
// fragment that builds them (Web/jux-native-selectors.json, lines "12", composedClasses).
(function () {
    if (typeof window.juxTabModern !== 'undefined') {
        return;
    }

    var TABS = [
        { index: 2, paneId: 'jux-tab-watchlist', label: 'client.tabs.watchlist', icon: 'bookmark_border', open: function () { _call(window.juxWatchlist, 'render'); } },
        { index: 3, paneId: 'jux-tab-history', label: 'client.tabs.history', icon: 'history', open: function () { _call(window.juxHistoryTab, 'open'); } },
        { index: 4, paneId: 'jux-tab-statistics', label: 'client.tabs.statistics', icon: 'insights', open: function () { _call(window.juxStatistics, 'render'); } }
    ];

    function _call(target, method) {
        if (target && typeof target[method] === 'function') {
            target[method]();
        }
    }

    function _header() {
        return document.querySelector('header.MuiAppBar-root');
    }

    function _bar() {
        return document.querySelector('header.MuiAppBar-root .MuiStack-root');
    }

    function _drawerList() {
        return document.querySelector('.MuiDrawer-paper ul.MuiList-root');
    }

    function isModern() {
        return !!_header();
    }

    // The JellyUX tab the address names (2 to 4) on the home route, or null.
    function tabOf(hash) {
        var match = /^#\/home(?:\.html)?\?(?:[^#]*&)?tab=(\d+)(?:&|$)/.exec(hash || '');
        var index = match ? parseInt(match[1], 10) : null;
        return TABS.some(function (tab) { return tab.index === index; }) ? index : null;
    }

    function _isHome(hash) {
        return /^#\/home(?:\.html)?(?:\?|$)/.test(hash || '') || hash === '' || hash === '#/';
    }

    // -------------------------------------------------------------------------
    // Entries of the header bar and the drawer
    // -------------------------------------------------------------------------

    function _favoritesLink(container) {
        var links = container.querySelectorAll('a[href^="#/home?tab="]');
        return links.length > 0 ? links[0] : null;
    }

    // The label is the last text of the native entry (a trailing text node in the bar, the text of
    // the list item in the drawer); its icon becomes the Material icon of the tab.
    function _decorate(link, tab) {
        link.setAttribute('href', '#/home?tab=' + tab.index);
        link.removeAttribute('aria-current');
        link.removeAttribute('id');
        link.classList.remove('Mui-selected');
        link.classList.add('jux-modern-tab');
        link.setAttribute('data-jux-tab', String(tab.index));

        var svg = link.querySelector('svg');
        if (svg) {
            var icon = document.createElement('span');
            icon.className = 'material-icons ' + tab.icon;
            icon.setAttribute('aria-hidden', 'true');
            svg.parentNode.replaceChild(icon, svg);
        }

        var walker = document.createTreeWalker(link, NodeFilter.SHOW_TEXT);
        var last = null;
        while (walker.nextNode()) {
            if (walker.currentNode.textContent.trim() !== '') {
                last = walker.currentNode;
            }
        }
        var text = window.JuxCore.t(tab.label);
        if (last) {
            last.textContent = text;
        } else {
            link.appendChild(document.createTextNode(text));
        }
    }

    // One entry per tab, cloned from the native Favorites entry: in the bar the link itself, in the
    // drawer its list item. Returns true when an entry was added.
    function _ensureEntries(container, wrap) {
        var favorites = _favoritesLink(container);
        if (!favorites) {
            return false;
        }
        var template = wrap ? favorites.closest('li') : favorites;
        if (!template) {
            return false;
        }

        var added = false;
        TABS.forEach(function (tab) {
            if (container.querySelector('[data-jux-tab="' + tab.index + '"]')) {
                return;
            }
            var entry = template.cloneNode(true);
            _decorate(wrap ? entry.querySelector('a') : entry, tab);
            container.appendChild(entry);
            added = true;
        });
        _keepLast(container);
        return added;
    }

    // React reconciles its own children around ours: keep the JellyUX entries last, in tab order.
    function _keepLast(container) {
        var indexOf = function (el) {
            var link = el.matches('[data-jux-tab]') ? el : el.querySelector('[data-jux-tab]');
            return link ? parseInt(link.getAttribute('data-jux-tab'), 10) : -1;
        };
        var ours = Array.prototype.filter.call(container.children, function (el) { return indexOf(el) >= 0; })
            .sort(function (a, b) { return indexOf(a) - indexOf(b); });
        var tail = Array.prototype.slice.call(container.children, -ours.length);
        if (ours.some(function (el, i) { return tail[i] !== el; })) {
            ours.forEach(function (el) { container.appendChild(el); });
        }
    }

    function _markSelected(index) {
        Array.prototype.forEach.call(document.querySelectorAll('.jux-modern-tab'), function (link) {
            var on = index !== null && link.getAttribute('data-jux-tab') === String(index);
            if (on) {
                link.setAttribute('aria-current', 'page');
            } else {
                link.removeAttribute('aria-current');
            }
            if (link.closest('li')) {
                link.classList.toggle('Mui-selected', on);
            }
        });
        // The native drawer entry of the home page stays selected under a JellyUX tab: jux-ui.css
        // dims it while this class is on the drawer list.
        var list = _drawerList();
        if (list) {
            list.classList.toggle('jux-modern-tab-open', index !== null);
        }
    }

    // -------------------------------------------------------------------------
    // Panes
    // -------------------------------------------------------------------------

    // The panes of the home view on screen, created after #favoritesTab when missing. data-jux-client
    // tells the chunk guard (jux-homepage.js) that the client made them, not the server patch.
    function _ensurePanes() {
        var favorites = document.querySelector('.homePage:not(.hide) #favoritesTab');
        if (!favorites) {
            return null;
        }
        var view = favorites.parentNode;
        var previous = favorites;
        TABS.forEach(function (tab) {
            var pane = view.querySelector('[id="' + tab.paneId + '"]');
            if (!pane) {
                pane = document.createElement('div');
                pane.className = 'tabContent pageTabContent';
                pane.id = tab.paneId;
                pane.setAttribute('data-index', String(tab.index));
                pane.setAttribute('data-jux-client', '');
                pane.innerHTML = '<div class="sections"></div>';
                previous.parentNode.insertBefore(pane, previous.nextSibling);
            }
            previous = pane;
        });
        return view;
    }

    // The JellyUX pane of index is the only active one; leaving a JellyUX tab gives the native pane of
    // the address back when the native home has not switched to it itself.
    function _showPane(view, index) {
        var panes = Array.prototype.filter.call(view.querySelectorAll('.tabContent'), function (pane) {
            return pane.parentNode === view;
        });
        if (index !== null) {
            panes.forEach(function (pane) {
                var shown = pane.getAttribute('data-index') === String(index) && pane.hasAttribute('data-jux-client');
                pane.classList.toggle('is-active', shown);
            });
            return;
        }
        var ours = panes.filter(function (pane) { return pane.hasAttribute('data-jux-client'); });
        var wasOurs = ours.some(function (pane) { return pane.classList.contains('is-active'); });
        ours.forEach(function (pane) { pane.classList.remove('is-active'); });
        if (wasOurs && !panes.some(function (pane) { return pane.classList.contains('is-active'); })) {
            var native = /[?&]tab=1(?:&|$)/.test(window.location.hash) ? '1' : '0';
            panes.forEach(function (pane) {
                var shown = pane.getAttribute('data-index') === native;
                pane.classList.toggle('is-active', shown);
            });
        }
    }

    // -------------------------------------------------------------------------
    // Sync
    // -------------------------------------------------------------------------

    var _shown = { index: null, pane: null };
    var _subscribed = false;
    var _pending = false;

    // A user who turns JellyUX off keeps the Modern header for the whole session: the entries and the panes
    // JellyUX added go, and the native pane of the address comes back (R11).
    function _removeAll() {
        Array.prototype.forEach.call(document.querySelectorAll('.jux-modern-tab'), function (link) {
            var item = link.closest('li');
            (item && item.querySelector('.jux-modern-tab') === link ? item : link).remove();
        });
        var list = _drawerList();
        if (list) {
            list.classList.remove('jux-modern-tab-open');
        }
        var panes = document.querySelectorAll('[data-jux-client]');
        var view = panes.length > 0 ? panes[0].parentNode : null;
        if (view) {
            _showPane(view, null);
        }
        Array.prototype.forEach.call(panes, function (pane) { pane.remove(); });
        _shown = { index: null, pane: null };
    }

    function sync() {
        if (!isModern() || !window.JuxCore) {
            return;
        }
        if (window.JuxCore.isOffForUser()) {
            _removeAll();
            return;
        }
        var bar = _bar();
        if (bar) {
            _ensureEntries(bar, false);
        }
        var list = _drawerList();
        if (list) {
            _ensureEntries(list, true);
        }

        var hash = window.location.hash;
        var index = tabOf(hash);
        _markSelected(index);
        var view = _isHome(hash) ? _ensurePanes() : null;
        if (view) {
            _showPane(view, index);
            _openShown(view, index);
        }
    }

    // Fills a pane when its tab is opened, or when React replaced the pane of the tab on screen.
    function _openShown(view, index) {
        var tab = TABS.filter(function (t) { return t.index === index; })[0];
        var pane = tab ? view.querySelector('[id="' + tab.paneId + '"]') : null;
        if (_shown.index === index && _shown.pane === pane) {
            return;
        }
        _shown = { index: index, pane: pane };
        if (tab && pane) {
            tab.open();
        }
    }

    function _schedule() {
        if (_pending) {
            return;
        }
        _pending = true;
        window.requestAnimationFrame(function () {
            _pending = false;
            sync();
        });
    }

    function init() {
        if (!_subscribed && window.JuxCore && typeof window.JuxCore.onDomMutation === 'function') {
            window.JuxCore.onDomMutation(_schedule);
            _subscribed = true;
        }
        sync();
    }

    window.juxTabModern = {
        tabs: TABS,
        isModern: isModern,
        tabOf: tabOf,
        sync: sync
    };

    window.addEventListener('hashchange', _schedule);
    window.addEventListener('popstate', _schedule);
    document.addEventListener('viewshow', _schedule);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
