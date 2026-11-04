# Because You Watched

**Category:** Personalized

Displays movies related to a film the requesting user recently watched, sharing at least one genre
with that reference film.

## How ranking works

Each Because You Watched section you add gets a **rank**, based on the order the sections were added.
At render time, each user's recently watched films are ranked from their own watch history, and each
section is based on the film at its rank for that user: rank 1 uses their most recently watched film,
rank 2 uses their second most recently watched film, and so on. If a user doesn't have enough recently
watched films to fill every configured rank, the extra sections are simply not shown for that user.
The titles of a section are drawn when the order of the deferred sections is computed (see "Deferred
sections freshness" in the [configuration](../../configuration.md#cache)), not at each visit; a title the
user watches meanwhile leaves the section at once when "Exclude already watched" is on.

## Parameters

In addition to the [common parameters](../../configuration.md#common-per-widget-parameters):

| Field | Description |
|---|---|
| Exclude already watched | When enabled, titles the user has already watched are not recommended. |

Note: Personalized widgets don't have a custom name field. Section names are generated per user (for
example "Because you watched ...").
