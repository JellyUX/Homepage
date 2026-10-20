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
    /// Gets or sets how often TMDb data is refreshed, in hours.
    /// Increase to reduce external API usage; decrease for fresher trending content.
    /// </summary>
    public int TMDbRefreshIntervalHours { get; set; } = 24;
}
