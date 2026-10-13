# Seasonal

**Category:** Admin

Displays items from your local library during a recurring, admin-configured date window (e.g.
Halloween, Christmas), filtered by genres and/or tags. Like the other Admin
widgets, Seasonal reads only your library (no TMDb or Wikidata call, no API key needed), but it has
its own editor in the admin panel (theme, dates, genres, tags) instead of the single-value picker the
other Admin widgets share.

The plugin ships with seven disabled-by-default presets over four seasons (Halloween, Christmas,
Valentine's Day, New Year); enable the ones you want, or add your own custom Seasonal section with any
date window, theme, and genre/tag filter. As in KefinTweaks, a season can have several sections, each
with its own filter.

A section outside its configured date window returns no items and is automatically hidden from the
home screen, the same generic "not enough items" behavior every widget already relies on -- there is
no separate "hide when out of season" setting to configure. A section with neither a genre nor a tag
is hidden too: without a filter it would show your whole library.

## Parameters

In addition to the [common parameters](../../configuration.md#common-per-widget-parameters):

| Field | Description |
|---|---|
| Theme | An optional theme label (`Halloween`, `Christmas`, `Valentine's Day`, `New Year`, or none). It has no visual effect today: it is a label for your own organization, kept on the section for a future per-theme look. |
| Start date / End date | The recurring `MM-DD` window during which this section is shown (e.g. `10-01` to `10-31` for Halloween). A window whose end date is earlier than its start date is treated as wrapping across the new year (e.g. `12-26` to `01-06`). |
| Genres | Genres of your library, separated by `\|` (e.g. `Horror\|Horreur`): a title with any of them matches. Genre names follow the language of your metadata, so list each spelling your library uses. |
| Tags | Tags of your library, separated by `\|`: a title with any of them matches. When both genres and tags are set, a title needs one of each. |

## Presets

| Preset | Default window | Default theme | Filter |
|---|---|---|---|
| Halloween | October 1 - October 31 | Halloween | tag `halloween` |
| Halloween Horror | October 1 - October 31 | Halloween | genres `Horror\|Horreur` |
| Halloween Thrillers | October 1 - October 31 | Halloween | genre `Thriller` |
| Christmas | December 1 - December 25 | Christmas | tag `christmas` |
| Valentine's Day | February 7 - February 14 | Valentine's Day | tag `valentine's day` |
| Valentine's Romance | February 7 - February 14 | Valentine's Day | genre `Romance` |
| New Year | December 26 - January 6 | New Year | tag `new year's eve` |

The tags are TMDb keyword names, the tags Jellyfin writes from TMDb metadata. Genre names follow the
language of your metadata: the Horror preset lists the English and the French name; add your own
spelling if your library uses another language. A preset whose filter matches fewer titles than its
minimum stays hidden. Each preset ships disabled; enable the ones you want and adjust the dates or the
filters to fit your library and timezone.

When upgrading, a preset section you never gave a filter receives the filter of its preset (it used to
show the whole library); a section you filtered yourself is left unchanged.

A preset section shows its name in each user's language (English or French today) until you type a
name of your own; on upgrade, a preset still carrying its original English name switches to the
translated one.
