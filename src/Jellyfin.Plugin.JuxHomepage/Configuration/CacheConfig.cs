namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Cache tuning parameters for the widget engine.
/// </summary>
public sealed class CacheConfig
{
    /// <summary>
    /// Gets or sets the session cache TTL in minutes, counted from when a user's widget layout was
    /// built, not from its last use. Past it the layout is still served while it is rebuilt in the
    /// background (PERF-01, <c>Sections_AfterTtl_ServesPreviousLayoutAndRebuildsOnce</c>).
    /// </summary>
    public int SessionTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets how often each TMDb list is refreshed, in hours. The hourly TMDb task refreshes
    /// the lists older than this less one hour (QUAL-01, DEC-P06-3,
    /// <c>TMDbRefreshIntervalHours_72h_DailyTaskSkipsFreshLists</c>); "Refresh now" refreshes all.
    /// </summary>
    public int TMDbRefreshIntervalHours { get; set; } = 24;
}
