# JellyUX Homepage

A modular home screen engine for Jellyfin that replaces the default home page with a fully
configurable widget system.

This site documents every widget and admin setting in detail. For installation instructions and a
quick overview, see the [README](https://github.com/JellyUX/Homepage#readme).

## Where to start

- **[Configuration](configuration.md)** - global settings shared across widgets: cache TTLs, TMDb API
  keys, and TMDb list pagination.
- **Widgets** (see the navigation menu) - one page per widget, grouped by category:
  - **Native** - built from your own Jellyfin library, no configuration needed beyond the shared
    parameters.
  - **Admin** - the administrator picks a specific value (a genre, an actor, a year, etc.).
  - **Personalized** - one row per rank, computed per user from their watch history.
  - **Connected** - powered by The Movie Database (TMDb), cross-referenced against your library.
- **[Troubleshooting](troubleshooting.md)** - known issues and their fixes.
