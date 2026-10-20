# Configuration

All settings below are found in **Administration > Plugins > JellyUX Homepage**.

## Modules

Each switch below turns one client module on or off. All are on by default. A disabled module is not
loaded at all: its script is left out of the web client, so it costs nothing on any page. Changes
apply at the next page reload. Unchecking **Enable JellyUX Homepage** leaves out every JellyUX script.

| Switch | What it adds |
|---|---|
| Home tabs | Watchlist, Progress, History and Statistics tabs on the home screen. |
| Card watchlist button and infinite scroll | Watchlist button on movie and series cards, and infinite scrolling on library pages. |
| Single-season series | The episodes of a single-season series shown directly on its page. |
| Collections | "Included In" section on detail pages, and sort controls on collection pages. |
| Breadcrumb | Library breadcrumb in the header of detail pages. |

## Common per-widget parameters

Every widget section shares this set of fields, no matter its category:

| Field | Description |
|---|---|
| Enabled | Toggles the section on or off without removing it. |
| Min items | Section is hidden if fewer items than this are available. |
| Max items | Maximum number of items fetched per section, at most 100. |
| View mode | Card shape: Landscape, Portrait, or Square. |
| Name | Custom display name shown instead of the widget's default title. Not available for Personalized widgets, whose section names are generated per user (e.g. "Because you watched ..."). |

Widget pages in this site only document parameters that are specific to that widget, on top of this
shared set.

## Sort order

Admin, Personalized (except Because You Watched) and Seasonal sections also have a **Sort order**:
name, date added, release date or rating, in either direction, or **Random**. Sections saved before
this setting existed keep their alphabetical order.

Random is drawn once a day for each user and section, from the items that user can see, and stays the
same on every visit of that day; it changes at midnight (server time). Editing the section (its value,
filters or Max items) draws again at once. Native sections (Continue Watching, Next Up, Recently Added),
TMDb sections and Because You Watched keep their own order.

## Watchlist

**Remove watched titles from the watchlist** (on by default): a movie leaves the watchlist when its
playback finishes, a series when its last unwatched episode does. Marking a title as watched by hand
never removes it. The watchlist is Jellyfin's "liked" list, which other plugins read too: Jellyfin
Enhanced, for one, syncs it to Seerr, so a removed title also leaves the Seerr watchlist. Untick the
setting to never change the list automatically.

## User settings

Each user finds a **JellyUX** block in *Settings > Home*: untick **Use JellyUX** to get Jellyfin's own
home screen, tabs and pages back for that account, or untick a section to hide it from their own home
screen. A hidden section keeps every setting chosen here; only its display is removed for that user.
While JellyUX is on, the native *Home screen section* lists of that page are hidden, since the JellyUX
sections replace them. Recently Added sections also follow the user's native *Latest media* choices
(excluded libraries, hidden watched items).

## Cache

| Field | Default | Description |
|---|---|---|
| Session cache TTL (minutes) | 15 | How long the home screen widget layout is cached per user, counted from when it was built. The first visit after that still shows the previous layout while the new one is built in the background. Saving the configuration clears this cache at once, so a lower value is never needed to see a configuration change. Reduce to see new library content sooner; increase to lower server load. |
| TMDb refresh interval (hours) | 24 | How often each TMDb list is refreshed, checked every hour. Increase to reduce external API calls; decrease for fresher content. |

## API Keys

| Field | Description |
|---|---|
| TMDb API Key | Required for every TMDb-backed [Connected](widgets/connected/trending-movies.md) widget. Leave empty if not needed. Accepted formats: a 32-character hexadecimal key (v3), or a v4 bearer token starting with `ey`. **Not required** for the [Rewards](widgets/connected/rewards.md) widget, which uses Wikidata's free public data instead. |

## TMDb Cache

The **TMDb Cache** section shows, per TMDb list type, the last refresh time and the number of cached
items, plus a **Refresh now** button. Each page is roughly 20 items from TMDb, so setting "Pages: 3"
fetches about 60 items for that list.

| Field | Applies to | Default |
|---|---|---|
| Pages | Trending Movies, Trending Shows, On TV, Top Rated Movies, Top Rated Shows, Now Playing | 1 (1-5) |
| Min votes | Top Rated Movies and Top Rated Shows (shared field) | 200 |
| Region | Now Playing | Default (all regions) |

Note: [Discover Movies](widgets/connected/discover-movies.md) has its own `Pages` field per
configured section, independent of the global TMDb Lists pages above.

**Refresh now** also refreshes every configured [Rewards](widgets/connected/rewards.md) section --
Rewards has no separate refresh button of its own, even though it is not TMDb data.
