namespace Jellyfin.Plugin.JuxHomepage.Watchlist.Models;

/// <summary>One row of the Top 5 block of the Statistics view (TODO_V4.md 8.5, F-55).</summary>
public sealed class TopSeriesEntry
{
    /// <summary>Gets the series' item identifier.</summary>
    public Guid SeriesId { get; init; }

    /// <summary>Gets the series' current display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the number of episodes this user has watched.</summary>
    public int WatchedEpisodes { get; init; }

    /// <summary>Gets the number of episodes of the series.</summary>
    public int TotalEpisodes { get; init; }
}
