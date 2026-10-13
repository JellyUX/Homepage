namespace Jellyfin.Plugin.JuxHomepage.Watchlist.Models;

/// <summary>
/// Aggregate watch-history counters for the Statistics view (TODO_V3.md Phase 6.3). Derived entirely
/// from the already-cached Series Progress / Movie History data -- no new library collection.
/// </summary>
public sealed class WatchingStatistics
{
    /// <summary>Gets the number of movies this user has watched.</summary>
    public int MoviesWatched { get; init; }

    /// <summary>
    /// Gets the number of those movies last played since the first day of the current month (server
    /// time), the second tile of the History tab (TODO_V4.md 8.5, F-58).
    /// </summary>
    public int MoviesWatchedThisMonth { get; init; }

    /// <summary>Gets the number of series this user has watched at least one episode of.</summary>
    public int SeriesTracked { get; init; }

    /// <summary>Gets the number of series this user has watched every episode of.</summary>
    public int SeriesCompleted { get; init; }

    /// <summary>Gets the total number of episodes this user has watched, across all series.</summary>
    public int EpisodesWatched { get; init; }

    /// <summary>Gets the most watched series, at most five (TODO_V4.md 8.5, F-55).</summary>
    public IReadOnlyList<TopSeriesEntry> TopSeries { get; init; } = [];
}
