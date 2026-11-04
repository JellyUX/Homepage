namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Cache tuning parameters for the widget engine.
/// </summary>
public sealed class CacheConfig
{
    /// <summary>
    /// Gets or sets the session cache TTL in minutes, counted from when a user's widget layout was
    /// built, not from its last use. Past it the layout is still served while it is rebuilt in the
    /// background (PERF-01, <c>Sections_AfterTtl_ServesPreviousLayoutAndRebuildsOnce</c>). The same TTL bounds
    /// the cached section pages and the liked item ids (REV-01).
    /// </summary>
    public int SessionTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets how often each TMDb list is refreshed, in hours. The hourly TMDb task refreshes
    /// the lists older than this less one hour (QUAL-01, DEC-P06-3,
    /// <c>TMDbRefreshIntervalHours_72h_DailyTaskSkipsFreshLists</c>); "Refresh now" refreshes all.
    /// </summary>
    public int TMDbRefreshIntervalHours { get; set; } = 24;

    /// <summary>The shortest deferred sections freshness, in hours.</summary>
    public const int MinDeferredFreshnessHours = 6;

    /// <summary>The longest deferred sections freshness, in hours (a week).</summary>
    public const int MaxDeferredFreshnessHours = 168;

    /// <summary>
    /// Gets or sets how long the stored order of the deferred sections is read again before it is computed again, in
    /// hours (DR-033): personalized, admin, local, TMDb, award and seasonal sections keep their order this long, their
    /// titles' watched state staying live; a new scoring, a library scan or a refreshed list computes them sooner.
    /// Clamped to <see cref="MinDeferredFreshnessHours"/>..<see cref="MaxDeferredFreshnessHours"/>.
    /// </summary>
    public int DeferredFreshnessHours { get; set; } = 24;
}
