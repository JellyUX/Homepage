# Troubleshooting

## JellyUX tabs, or Custom Tabs tabs, open empty

JellyUX and [Custom Tabs](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs) cannot be
installed together. Both add tabs by patching the same `home-html` chunk through File
Transformation, which runs only one transformation per file pattern: the plugin whose patch is
applied keeps its panes, and the other plugin's buttons switch to the wrong, empty panes. The JellyUX
configuration page warns about it whenever Custom Tabs is installed.

**Fix**: uninstall Custom Tabs by removing its folder from the Jellyfin plugins directory, then
restart Jellyfin. Do not use **Disable** in **Administration > Plugins** for this: on Jellyfin 10.11 a
disabled plugin is deleted at the next restart (see the README).

## "Cannot find module './'" in the browser console when opening a JellyUX tab

Each click on a JellyUX tab logs one `Uncaught (in promise) Error: Cannot find module './'` in the
browser console. It is harmless: the tab opens normally. Jellyfin Web loads a page controller for
every home tab it is told about, and has one only for Home and Favorites; for any other tab (the
JellyUX ones, or those of other plugins that add home tabs) that lookup fails, and Jellyfin does not
catch the error. JellyUX leaves this native code path untouched rather than block it, since blocking
it would also stop Jellyfin from pausing the Home tab while another tab is shown.

## Items hidden in Jellyfin Enhanced still show in some JellyUX sections

The JellyUX native sections (Continue Watching, Next Up, Recently Added Movies, Shows and Episodes)
read the same Jellyfin routes as the native home page, so Jellyfin Enhanced hidden content, Remove
from Continue Watching and Spoiler Guard apply to them exactly as on the native home page.

The other JellyUX sections (genres, collections, recommendations, TMDb and so on) come from the
JellyUX server, which Jellyfin Enhanced does not filter. There, a hidden item is only removed by the
Jellyfin Enhanced web script when it scans the page, which does not always happen on the first
render, and Spoiler Guard does not replace episode titles.

## Plugin becomes unreachable (HTTP 500) after an update

A simple Jellyfin restart may not be enough after updating the plugin. Do a full restart of the
Jellyfin process itself (for example `docker restart <container>`) to clear the File Transformation
plugin's static state.

## JellyUX sections don't appear on Jellyfin Media Player (Windows)

Jellyfin Media Player keeps an internal auto-connect cache (`userWebClient` in
`%LOCALAPPDATA%\JellyfinMediaPlayer\jellyfinmediaplayer.conf`, `main` section) that isn't invalidated
when you switch servers in the app. It keeps loading the web client, so every plugin (not just
JellyUX), from the previously connected server, regardless of which server is currently selected.

**Recommended fix**: in the app's settings, use the **Reset Saved Server** button (shown automatically
whenever a server is cached).

**Fallback**: edit `jellyfinmediaplayer.conf` and reset `userWebClient` to an empty string. Don't
confuse this with the `%LOCALAPPDATA%\Jellyfin Media Player` cache folder (with spaces): clearing that
one does not fix this specific issue, since it's an auto-connect cache, not a browser cache.

## "Forgot password?" button missing on the login page with ZestyTheme

This is unrelated to JellyUX. ZestyTheme's `theme.css` intentionally hides `.btnForgotPassword` on the
login page. To use password recovery, temporarily disable the ZestyTheme custom CSS (or remove the
button's `display: none` override locally), recover the password, then re-enable the theme.

## TMDb requests stop working after a network outage

Connected widgets rely on TMDb's public API. After 3 consecutive network-level failures (not an
invalid API key, which is reported separately), a circuit breaker opens and TMDb requests are skipped
for 5 minutes rather than retried on every widget load. Look for a log line similar to:

```
TMDb circuit breaker opened after 3 consecutive failures; TMDb requests will be skipped for 5 minute(s).
```

This is expected behavior during a sustained TMDb outage and requires no action: normal requests
resume automatically once the 5-minute window elapses and TMDb is reachable again.

## Sections don't appear even after clearing the browser cache and using incognito mode

JellyUX first repairs the browser side by itself: when the home page is rendered by a native copy of
the patched chunks (typically a chunk the browser cached before the plugin was installed), it
refetches the cached chunks, bypassing the browser cache, and reloads the page once, with a short
"updating cached files" message. If the home page is still native after that reload, the message
"a cache between this browser and the server (CDN or proxy) still serves an old copy" stays on screen:
the stale copy then comes from outside the browser, as described below.

If the home page still shows native Jellyfin sections after installing or updating JellyUX, and this
persists even in a private/incognito window or after fully clearing browser data, the cause is
probably **not** the browser at all: a CDN or a caching reverse proxy in front of your Jellyfin server
may be serving a cached copy of the patched chunk from before the install/update.

This was confirmed on a real deployment (TrueNAS app, Jellyfin behind an **nginx reverse proxy**, DNS
managed through **Cloudflare**): the chunk containing `loadSections` was served with
`cf-cache-status: HIT` at Cloudflare's edge, from before the plugin was installed. No amount of
browser-side cache clearing can bust an edge/CDN cache, since it lives entirely outside the browser.

**How to check**: open your browser's developer tools (F12), go to the **Network** tab, reload the
home page, find the request for the `*.chunk.js` file containing your home screen bundle, and inspect
its response headers. A `cf-cache-status: HIT` (Cloudflare) or similar cache-hit header from any other
CDN/reverse proxy confirms this is the cause rather than a plugin issue.

**Fix**: purge the cache at the CDN/proxy level, not the browser:
- **Cloudflare**: dashboard > your zone > **Caching > Configuration > Purge Cache** (Purge Everything
  is simplest, since the chunk's filename hash changes on every Jellyfin Web update anyway).
- **nginx reverse proxy with `proxy_cache`**: clear the configured cache path, or reload nginx if it
  was configured to bypass its own cache for the relevant location.
- Other CDNs: use their equivalent cache purge/invalidation feature.

If you consistently run into this after every JellyUX update, consider adding a cache rule at your
CDN/proxy that bypasses caching for `*.chunk.js`/`*.bundle.js` paths, or that purges automatically as
part of your plugin update routine.

## JellyUX or File Transformation disappeared after a restart

On Jellyfin 10.11, **Disable** in **Administration > Plugins** can delete the plugin at the next
restart (Jellyfin issue [#15897](https://github.com/jellyfin/jellyfin/issues/15897), fixed in 12.0).
At startup Jellyfin sorts the plugins by name and walks the list from the end: a disabled plugin
found after an active one is deleted, and this is only logged at the Debug level. Whether it happens
depends on the other plugins installed: on a server with Media Bar or TMDb Box Sets, both sort after
JellyUX Homepage.

This applies to File Transformation too: once deleted, nothing injects JellyUX into the web client
and the home page is native. The JellyUX admin page warns in its banner while JellyUX or File
Transformation is disabled, before the restart: enable it again before restarting.

To turn JellyUX off, uncheck **Enable JellyUX Homepage** on its configuration page instead. If the
plugin is already gone, reinstall it from the plugin catalog: its settings are kept, as the
configuration file and the data folder are not deleted.

## Home page falls back to native rendering after a Jellyfin update

JellyUX patches a specific Jellyfin Web chunk to render its widgets. If Jellyfin Web's internal
bundle changes shape (a "drift"), the patch may no longer apply, and a warning appears in the server
logs:

```
Chunk <file>.chunk.js contains ',loadSections:' but the minified hook could not be resolved.
```

If you see this line, the home page falls back to Jellyfin's native rendering rather than showing a
broken page. The admin page of JellyUX shows the same problem in its banner, and the home page shows
"Jellyfin Web has changed: JellyUX went back to the native home screen" instead of reloading: purging
a CDN or proxy cache does not help in this case. This is a known limitation of patching a third-party bundle that isn't part of JellyUX's
public API. Check the project's GitHub Issues for a compatibility update, or open a new issue if none
exists yet.

## A Rewards or Discover section shows nothing, or the admin page refuses to save a Rewards section

Before version 2.15, saving the admin page could drop the filters of a Rewards section (ceremony and
category) and of a Discover section (person, keyword, company, and the genre when TMDb returned no
genre list), even when nothing was changed. The update stops this from happening again, but it
cannot bring back a filter that was already lost: the section stays empty until its filter is set
again.

A Rewards section with neither a ceremony nor a category is now flagged on the admin page, which
asks for one before saving, and the server logs this line at startup:

```
Rewards section '<id>' has no ceremony or category and shows nothing: set one on the JellyUX admin page.
```

Open the section on the admin page, search for its ceremony or category again, and save. A Discover
section that lost its filters is not flagged, because a Discover section without a filter is valid:
check its fields and set them again if needed.
