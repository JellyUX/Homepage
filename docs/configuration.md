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

## Cache

| Field | Default | Description |
|---|---|---|
| Session cache TTL (minutes) | 15 | How long the home screen widget layout is cached per user. Saving the configuration clears this cache at once, so a lower value is never needed to see a configuration change. Reduce to see new library content sooner; increase to lower server load. |
| TMDb refresh interval (hours) | 24 | How often TMDb data is refreshed. Increase to reduce external API calls; decrease for fresher content. |

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
