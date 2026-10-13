# Seasonal

**Category:** Admin

Displays items from your local library during a recurring, admin-configured date window (e.g.
Halloween, Christmas), optionally restricted to a specific genre and/or tag. Like the other Admin
widgets, Seasonal reads only your library (no TMDb or Wikidata call, no API key needed), but it has
its own editor in the admin panel (theme, dates, genre, tag) instead of the single-value picker the
other Admin widgets share.

The plugin ships with four disabled-by-default presets (Halloween, Christmas, Valentine's Day, New
Year); enable the ones you want, or add your own custom Seasonal section with any date window, theme,
and optional genre/tag filter.

A section outside its configured date window returns no items and is automatically hidden from the
home screen, the same generic "not enough items" behavior every widget already relies on -- there is
no separate "hide when out of season" setting to configure.

## Parameters

In addition to the [common parameters](../../configuration.md#common-per-widget-parameters):

| Field | Description |
|---|---|
| Theme | An optional theme label (`Halloween`, `Christmas`, `Valentine's Day`, `New Year`, or none). It has no visual effect today: it is a label for your own organization, kept on the section for a future per-theme look. |
| Start date / End date | The recurring `MM-DD` window during which this section is shown (e.g. `10-01` to `10-31` for Halloween). A window whose end date is earlier than its start date is treated as wrapping across the new year (e.g. `12-26` to `01-06`). |
| Genre (optional) | Restricts the section to one genre present in your library. Leave empty to include every genre. |
| Tag (optional) | Restricts the section to one tag present in your library. Leave empty to include every tag. |

## Presets

| Preset | Default window | Default theme |
|---|---|---|
| Halloween | October 1 - October 31 | Halloween |
| Christmas | December 1 - December 25 | Christmas |
| Valentine's Day | February 7 - February 14 | Valentine's Day |
| New Year | December 26 - January 6 | New Year |

Each preset ships disabled and with no genre/tag filter (a pre-filled genre like "Horror" could easily
match nothing in your library, since genre taxonomies vary) -- enable the ones you want and adjust the
dates or add a filter to fit your own library and timezone.
