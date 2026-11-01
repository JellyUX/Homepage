// JUX loadSections splice fragment.
// This file is embedded in the plugin DLL and spliced into the Jellyfin home chunk at
// the ",loadSections:" injection point by TransformationPatches.PatchLoadSections.
// It is NOT served as a web resource.
//
// Placeholder tokens are substituted at runtime with minified names from the Jellyfin web bundle:
//   {{cardbuilder}}     -> the cardBuilder default export, as the module reads it (u.default on
//                          Jellyfin 10.11, p.Ay on 12.3), detected at patch time (DR-024)
//   {{this_hook}}       -> resolved dynamically from the last "var X=" before ,loadSections:
// Everything else JellyUX needs is rebuilt here instead of being read from minified exports: the
// card shapes mirror jellyfin-web's own helpers (an omitted argument means overflow), and
// layoutManager.tv mirrors the "layout-tv" class that layoutManager sets on the html element.
//
// The function is spliced as:
//   ,loadSections:<fragment>,originalLoadSections:
// so this.originalLoadSections is the native implementation.
// window.JuxLoadSectionsPatched tells jux-homepage.js that the patched chunk, not a cached native
// copy, rendered the home page (TODO_V4.md 4.2, F-13).
function(e, t, r, o, page) {
    window.JuxLoadSectionsPatched = true;
    try {
        if (!window.JellyfinAPI) {
            window.JellyfinAPI = {
                cardBuilder: {{cardbuilder}},
                layoutManager: {
                    get tv() {
                        return document.documentElement.classList.contains('layout-tv');
                    }
                },
                getBackdropShape: function (overflow) {
                    return overflow === undefined || overflow ? 'overflowBackdrop' : 'backdrop';
                },
                getPortraitShape: function (overflow) {
                    return overflow === undefined || overflow ? 'overflowPortrait' : 'portrait';
                },
                getSquareShape: function (overflow) {
                    return overflow === undefined || overflow ? 'overflowSquare' : 'square';
                }
            };
        }
        if (window.JUXHomepage) {
            window.JUXHomepage.originalLoadSections = {{this_hook}}.originalLoadSections;
            return window.JUXHomepage.loadSections.call(this, e, t, r, o, page);
        }
        return {{this_hook}}.originalLoadSections.call(this, e, t, r, o);
    } catch (err) {
        console.error('[JellyUX] loadSections override failed, falling back to native rendering.', err);
        return {{this_hook}}.originalLoadSections.call(this, e, t, r, o);
    }
}
