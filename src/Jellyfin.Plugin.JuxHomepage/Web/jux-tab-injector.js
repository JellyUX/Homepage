// Injects the 4 JellyUX tab buttons (Watchlist/Progress/History/Statistics) into the home page's
// native tab bar (.emby-tabs-slider), so they sit alongside the built-in "Home"/"Favorites" tabs.
//
// TODO_V3.md Phase 4.1. Modeled directly on the jellyfin-plugin-custom-tabs approach (verified
// against its real source, not just its README): a plain <button data-index="N"> appended into
// .emby-tabs-slider is enough -- Jellyfin's native "emby-tabs" custom element handles the click and
// visibility toggling itself, keyed by DOM position among the home view's .tabContent panes. N is
// therefore read from the real position of each pane spliced by Inject/TransformationPatches.cs,
// not hardcoded: another plugin's pane before ours would shift it (TODO_V4.md 4.1, F-12).
//
// The tab bar is rebuilt from scratch by React on every view mount (home.js calls setTabs(), which
// replaces .headerTabs innerHTML entirely), so this script must re-run each time a view is shown, not
// just once at initial page load (see the viewshow listener at the end).
if (typeof window.juxTabInjector === 'undefined') {
    window.juxTabInjector = {
        // buttonId is deliberately distinct from the content pane's id (jux-tab-* -- see
        // Inject/TransformationPatches.cs's HomeTabIds, spliced server-side into the home-html
        // chunk): reusing the same id for both caused document.getElementById(paneId) to always
        // find the (always-present) pane and wrongly conclude the button already existed, so no
        // button was ever created. Bug found by live DOM inspection on jellyux-test.
        tabs: [
            { buttonId: 'jux-tabbtn-watchlist', paneId: 'jux-tab-watchlist', labelEn: 'Watchlist', labelFr: 'Watchlist' },
            { buttonId: 'jux-tabbtn-progress', paneId: 'jux-tab-progress', labelEn: 'Progress', labelFr: 'Progression' },
            { buttonId: 'jux-tabbtn-history', paneId: 'jux-tab-history', labelEn: 'History', labelFr: 'Historique' },
            { buttonId: 'jux-tabbtn-statistics', paneId: 'jux-tab-statistics', labelEn: 'Statistics', labelFr: 'Statistiques', shortEn: 'Stats', shortFr: 'Stats' }
        ],

        init: function () {
            this.waitForUI();
        },

        waitForUI: function () {
            var hash = window.location.hash;
            if (hash !== '' && hash !== '#/home' && hash !== '#/home.html' && hash.indexOf('#/home?') !== 0 && hash.indexOf('#/home.html?') !== 0) {
                return;
            }

            if (document.querySelector('.emby-tabs-slider')) {
                this.createTabs();
            } else {
                setTimeout(function () { window.juxTabInjector.waitForUI(); }, 200);
            }
        },

        createTabs: function () {
            var tabsSlider = document.querySelector('.emby-tabs-slider');
            // Turned off for this user (TODO_V4.md 8.3): the home view keeps its native tabs only.
            if (!tabsSlider || (window.JuxCore && window.JuxCore.isOffForUser())) {
                return;
            }

            if (tabsSlider.querySelector('#' + this.tabs[0].buttonId)) {
                // Already created for this view mount.
                return;
            }

            var lang = (document.documentElement.lang || 'en').toLowerCase().indexOf('fr') === 0 ? 'fr' : 'en';

            this.tabs.forEach(function (tab) {
                if (document.getElementById(tab.buttonId)) {
                    return;
                }

                var paneIndex = _paneIndex(tab.paneId);
                if (paneIndex < 0) {
                    return;
                }

                var title = document.createElement('div');
                title.className = 'emby-button-foreground';
                title.appendChild(_label('jux-tab-label', lang === 'fr' ? tab.labelFr : tab.labelEn));
                // A narrow screen shows the short label instead (jux-ui.css), so more tabs fit (F-47).
                var shortLabel = lang === 'fr' ? tab.shortFr : tab.shortEn;
                if (shortLabel) {
                    title.appendChild(_label('jux-tab-label-short', shortLabel));
                    title.firstChild.classList.add('jux-tab-label-long');
                }

                // NOT document.createElement('button', { is: 'emby-button' }): confirmed live on
                // jellyux-test that this second-argument form throws ("t.toLowerCase is not a
                // function") on this Jellyfin Web build, aborting the whole forEach before any
                // button is appended -- the exact cause of a real regression (v2.3.0.0) where the
                // entire custom tab bar silently disappeared. setAttribute after creation is inert
                // for real customized-built-in upgrade, but harmless here since the click routing
                // is delegation-based on the slider/tabs controller, not on this button being a
                // real emby-button instance (confirmed working before and after this revert).
                var button = document.createElement('button');
                button.type = 'button';
                button.setAttribute('is', 'emby-button');
                button.className = 'emby-tab-button emby-button';
                button.setAttribute('data-index', paneIndex);
                button.id = tab.buttonId;
                button.appendChild(title);

                // Belt-and-suspenders pane switch: confirmed live on jellyux-test that the very
                // first click on a freshly-injected tab (right after a page load/reload) can leave
                // the tab bar's own selection state unresolved for a beat -- the MutationObserver
                // below reacts as soon as Jellyfin's controller does settle it, but a fixed short
                // delay after our own button's click guarantees the pane switches immediately
                // regardless of that timing, without needing to fully reverse-engineer the
                // minified controller's internal warm-up behavior.
                button.addEventListener('click', function () {
                    setTimeout(function () { _activatePane(tab.paneId); }, 50);
                });

                tabsSlider.appendChild(button);
            });

            _watchActiveTabButton(tabsSlider);
        }
    };

    function _label(className, text) {
        var span = document.createElement('span');
        span.className = className;
        span.textContent = text;
        return span;
    }

    // Manual pane activation for our own tabs. Confirmed by live DOM inspection on jellyux-test:
    // clicking a freshly-injected tab can leave the corresponding ".tabContent.is-active" pane
    // unswitched even though the tab bar's own visual selection looks right (TODO_V3.md Phase 5
    // manual test report -- "obligé de rafraîchir plusieurs fois avant de voir les médias"). Every
    // click afterwards works fine, so this isn't a permanent break, just an unreliable first switch.
    // Rather than depend on fully reverse-engineering Jellyfin's minified tab controller to find
    // the exact cause, two independent, idempotent mechanisms guarantee the pane switches anyway:
    // a MutationObserver reacting to the button's own "emby-tab-button-active" class (below), and a
    // fixed-delay fallback on each button's own click handler (see createTabs above). Whichever
    // fires first wins; the other is a harmless no-op repeat.
    var _tabBarObserver = null;
    var _observedSlider = null;

    // The panes of the view holding paneId, never the whole document: Jellyfin keeps up to 3 views
    // in the DOM, each with its own .tabContent panes (E2-04).
    function _viewPanes(pane) {
        var view = pane.closest('.page') || pane.parentNode;
        return Array.prototype.slice.call(view.querySelectorAll('.tabContent'));
    }

    function _paneIndex(paneId) {
        var pane = document.getElementById(paneId);
        return pane ? _viewPanes(pane).indexOf(pane) : -1;
    }

    function _activatePane(paneId) {
        var target = document.getElementById(paneId);
        if (!target) {
            return;
        }

        _viewPanes(target).forEach(function (pane) {
            pane.classList.toggle('is-active', pane === target);
        });
    }

    function _watchActiveTabButton(tabsSlider) {
        if (_observedSlider === tabsSlider) {
            return;
        }

        if (_tabBarObserver) {
            _tabBarObserver.disconnect();
        }

        _observedSlider = tabsSlider;
        _tabBarObserver = new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var button = mutations[i].target;
                if (button.id && button.id.indexOf('jux-tabbtn-') === 0 && button.classList.contains('emby-tab-button-active')) {
                    _activatePane(button.id.replace('jux-tabbtn-', 'jux-tab-'));
                }
            }
        });

        _tabBarObserver.observe(tabsSlider, { attributes: true, attributeFilter: ['class'], subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { window.juxTabInjector.init(); });
    } else {
        window.juxTabInjector.init();
    }

    // Jellyfin dispatches viewshow (bubbling) on each view it shows, once the view's tab bar is set:
    // injecting there, without a delay, adds the JellyUX tabs in the same frame as the native ones, so
    // the centered bar never shifts under the pointer (TODO_V4.md 7.5, F-42: a fixed 800 ms delay
    // moved the Home button by 217 px). waitForUI still polls if the bar is not rendered yet.
    document.addEventListener('viewshow', function () { window.juxTabInjector.init(); });
}
