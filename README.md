# JellyUX Homepage

<p align="center">
  <img src="docs/banner.png" alt="JellyUX Homepage banner" width="80%">
</p>

<p align="center">
  <img src="https://github.com/JellyUX/Homepage/actions/workflows/ci.yml/badge.svg" alt="Build">
  <img src="https://img.shields.io/github/v/release/JellyUX/Homepage" alt="Version">
  <img src="https://img.shields.io/badge/Jellyfin-10.11.10-orange" alt="Jellyfin">
  <img src="https://img.shields.io/badge/license-GPL--3.0-green" alt="License">
  <a href="https://jellyux.github.io/Homepage/"><img src="https://img.shields.io/badge/docs-online-blue" alt="Documentation"></a>
</p>

A modular home screen engine for Jellyfin that replaces the default home page with a fully configurable widget system.

**[Full documentation](https://jellyux.github.io/Homepage/)** - a detailed page for every widget, all global settings, and troubleshooting.

---

## Prerequisites

- **Jellyfin** 10.11.9 or later in the 10.11 series (validated on 10.11.10). The catalog only offers
  JellyUX to servers at 10.11.9 or above. A copy installed by hand on an older server is marked
  *NotSupported* by Jellyfin and may be deleted at the next restart.
- **[File Transformation plugin](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)** 3.0 or later (required - JellyUX uses it to inject its scripts into the Jellyfin web client; from 3.0 the transformed `index.html` is revalidated on every load, so a change to the module switches applies at the next page reload)

---

## Installation

1. In your Jellyfin dashboard, go to **Administration > Plugins > Repositories**
2. Click **Add** and paste the following URL:
   ```
   https://raw.githubusercontent.com/JellyUX/Homepage/main/manifest.json
   ```
3. Go to **Administration > Plugins > Catalog**, find **JellyUX Homepage** and install it
4. Restart Jellyfin
5. Go to **Administration > Plugins > JellyUX Homepage** to configure your widgets

---

## Configuration

After installation, the plugin configuration page lets you:
- Enable or disable individual widgets
- Reorder widgets via drag and drop
- Configure per-widget parameters (minimum items, source library, etc.)

See the **[documentation site](https://jellyux.github.io/Homepage/)** for a detailed
breakdown of every widget's parameters and all global settings.

---

## Screenshots

![Home screen with JellyUX widget sections](docs/screenshots/home.png)

![Admin configuration page](docs/screenshots/admin.png)

---

## Compatibility

| Client | Status |
|---|---|
| Web browsers (Chrome, Firefox, etc.) | Supported |
| Jellyfin Media Player (Windows) | Supported |
| Jellyfin app (Android) | Supported |
| TV clients (Android TV, Apple TV, Roku) | Auto-disabled - native home screen is preserved |

---

## Synergies

JellyUX Homepage is built to coexist cleanly with other popular Jellyfin customization projects:

- **[Media Bar](https://github.com/IAmParadox27/jellyfin-plugin-media-bar)** by IAmParadox27 - adds a cinematic slideshow banner above the home screen. Both plugins patch the web client independently and render side by side without conflict.
- **[ZestyTheme](https://github.com/stpnwf/ZestyTheme)** and other CSS themes - JellyUX widgets reuse native Jellyfin CSS classes and variables instead of redefining them, so they automatically inherit whichever theme is active.

---

## Widgets

29 widgets across 4 categories, each independently enabled, reordered, and configured from the admin panel.

| Widget | Category | Description |
|---|---|---|
| Continue Watching | Native | Items you started watching but haven't finished |
| Next Up | Native | Next unwatched episode for shows you're following |
| Recently Added Movies | Native | Latest movies added to your library |
| Recently Added Shows | Native | Shows that received episodes most recently |
| Recently Added Episodes | Native | Latest episodes added to your library |
| My Media | Native | Quick links to your library sections |
| Watchlist | Native | Items you've added to your Watchlist |
| Genre | Admin | Items from a genre chosen by the admin |
| Actor | Admin | Items featuring an actor chosen by the admin |
| Director | Admin | Items directed by a person chosen by the admin |
| Studio | Admin | Items from a studio chosen by the admin |
| Collection | Admin | Items from a collection chosen by the admin |
| Tag | Admin | Items matching a tag chosen by the admin |
| Year | Admin | Items released in a year chosen by the admin |
| Recently Released | Admin | The library's latest releases, newest release date first (movies, series or both), no TMDb needed |
| Top Rated in Library | Admin | The library's best community-rated titles (movies, series or both), no TMDb needed |
| Seasonal | Admin | Items shown during an admin-configured recurring date window (e.g. Halloween, Christmas), optionally filtered by genre/tag |
| Because You Watched | Personalized | Recommendations based on a recently watched item |
| Favorite Genre | Personalized | Recommendations from the user's most-watched genre |
| Favorite Actor | Personalized | Recommendations from the user's most-watched actor |
| Favorite Director | Personalized | Recommendations from the user's most-watched director |
| Trending Movies | Connected (TMDb) | Movies currently trending on TMDb |
| Trending Shows | Connected (TMDb) | Shows currently trending on TMDb |
| On TV | Connected (TMDb) | Shows airing today, per TMDb |
| Top Rated Movies | Connected (TMDb) | TMDb's top-rated movies |
| Top Rated Shows | Connected (TMDb) | TMDb's top-rated shows |
| Now Playing | Connected (TMDb) | Movies currently in theaters, per TMDb |
| Discover Movies | Connected (TMDb) | Movie discovery filtered by admin-configured criteria |
| Rewards | Connected (Wikidata) | Library films that won an admin-configured award (ceremony/category/year) |

---

## Troubleshooting

**Turning JellyUX off**

Uncheck **Enable JellyUX Homepage** at the top of the plugin's configuration page: the native home
screen comes back immediately. Never use **Disable** on the plugin in **Administration > Plugins**:
on Jellyfin 10.11 a disabled plugin is deleted at the next restart (Jellyfin issue
[#15897](https://github.com/jellyfin/jellyfin/issues/15897), fixed in 12.0 by
[#17521](https://github.com/jellyfin/jellyfin/pull/17521) and not backported to 10.11).

**Custom Tabs cannot be installed alongside JellyUX**

JellyUX and [Custom Tabs](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs) both add
tabs by patching the same home page template through File Transformation, which applies only one
patch per file: one plugin's tabs then open on empty panes. When Custom Tabs is installed, the
JellyUX configuration page shows a warning. Uninstall Custom Tabs by removing its folder from the
plugins directory, then restart Jellyfin.

**Plugin becomes unreachable (HTTP 500) after an update**

A simple Jellyfin restart may not be enough after updating the plugin - do a full restart of the
Jellyfin process itself (e.g. `docker restart <container>`) to clear the File Transformation plugin's
static state.

**JellyUX sections don't appear on Jellyfin Media Player (Windows)**

Jellyfin Media Player keeps an internal auto-connect cache (`userWebClient` in
`%LOCALAPPDATA%\JellyfinMediaPlayer\jellyfinmediaplayer.conf`, `main` section) that isn't invalidated
when you switch servers in the app - it keeps loading the web client (so every plugin, not just
JellyUX) from the previously connected server, regardless of which server is currently selected.
**Recommended fix**: in the app's settings, use the **"Reset Saved Server"** button (shown
automatically whenever a server is cached). Fallback: edit `jellyfinmediaplayer.conf` and reset
`userWebClient` to an empty string. Don't confuse this with the
`%LOCALAPPDATA%\Jellyfin Media Player` cache folder (with spaces) - clearing that one does not fix
this specific issue, since it's an auto-connect cache, not a browser cache.

**"Forgot password?" button missing on the login page with ZestyTheme**

This is unrelated to JellyUX - ZestyTheme's `theme.css` intentionally hides
`.btnForgotPassword` on the login page. To use password recovery, temporarily disable the
ZestyTheme custom CSS (or remove the button's `display: none` override locally), recover the
password, then re-enable the theme.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, conventions, and guidelines.

---

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE.md) file for details.
