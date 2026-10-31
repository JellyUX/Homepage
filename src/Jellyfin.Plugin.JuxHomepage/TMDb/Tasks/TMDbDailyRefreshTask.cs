using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.TMDb.Tasks;

/// <summary>
/// Scheduled task that runs every hour and refreshes the TMDb lists that are due under
/// <see cref="Configuration.CacheConfig.TMDbRefreshIntervalHours"/> (QUAL-01, DEC-P06-3).
/// Auto-discovered by Jellyfin -- not registered in DI (its dependencies are DI-registered, but the
/// task itself is instantiated by Jellyfin's scheduled task subsystem, exactly like
/// <see cref="Inject.StartupService"/>).
/// </summary>
public sealed class TMDbDailyRefreshTask : IScheduledTask
{
    private readonly ITMDbCacheService _cacheService;
    private readonly ILogger<TMDbDailyRefreshTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TMDbDailyRefreshTask"/> class.
    /// </summary>
    /// <param name="cacheService">The TMDb cache service to refresh.</param>
    /// <param name="logger">Logger.</param>
    public TMDbDailyRefreshTask(ITMDbCacheService cacheService, ILogger<TMDbDailyRefreshTask> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "JellyUX - TMDb Refresh";

    /// <inheritdoc/>
    public string Key => "Jellyfin.Plugin.JuxHomepage.TMDbRefresh";

    /// <inheritdoc/>
    public string Description => "Refreshes cached TMDb trending, airing, top rated, and now playing data.";

    /// <inheritdoc/>
    public string Category => "JellyUX Homepage";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // RefreshDueAsync is independently fault-tolerant per list (TMDbCacheService logs and
        // swallows its own failures) and does nothing when no API key is configured, so no
        // special-casing is needed here beyond delegating to it. The admin "Refresh now" action
        // (JuxHomepageAdminController) runs the same sequence for every list.
        if (!await _cacheService.RefreshDueAsync(progress, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("TMDb refresh task skipped: a TMDb refresh is already running.");
            return;
        }

        _logger.LogInformation("TMDb refresh task completed.");
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TMDbCacheService.RefreshTaskPeriod.Ticks
        };
    }
}
