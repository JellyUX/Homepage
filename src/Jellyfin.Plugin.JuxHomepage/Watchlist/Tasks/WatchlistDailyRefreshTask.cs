using Jellyfin.Plugin.JuxHomepage.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist.Tasks;

/// <summary>
/// Scheduled task that refreshes the 3 Watchlist-related server-side caches once a day: Series
/// Progress, Movie History (both per-user), and the Collections reverse index (global). Bundled into
/// one task -- mirroring how <see cref="TMDb.Tasks.TMDbDailyRefreshTask"/> refreshes 4 distinct TMDb
/// cache types in a single task -- rather than registering 3 separate tasks for closely related
/// caches. Auto-discovered by Jellyfin -- not registered in DI (its dependencies are DI-registered,
/// but the task itself is instantiated by Jellyfin's scheduled task subsystem).
/// </summary>
public sealed class WatchlistDailyRefreshTask : IScheduledTask
{
    private readonly ISeriesProgressCacheService _seriesProgressCache;
    private readonly IMovieHistoryCacheService _movieHistoryCache;
    private readonly ICollectionsIndexCacheService _collectionsIndexCache;
    private readonly LikedItemIdsCache _likedIds;
    private readonly ILogger<WatchlistDailyRefreshTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistDailyRefreshTask"/> class.
    /// </summary>
    /// <param name="seriesProgressCache">Series progress cache service.</param>
    /// <param name="movieHistoryCache">Movie history cache service.</param>
    /// <param name="collectionsIndexCache">Collections reverse index cache service.</param>
    /// <param name="likedIds">The in-memory liked ids, dropped once a day (DEC-P07-7).</param>
    /// <param name="logger">Logger.</param>
    public WatchlistDailyRefreshTask(
        ISeriesProgressCacheService seriesProgressCache,
        IMovieHistoryCacheService movieHistoryCache,
        ICollectionsIndexCacheService collectionsIndexCache,
        LikedItemIdsCache likedIds,
        ILogger<WatchlistDailyRefreshTask> logger)
    {
        _seriesProgressCache = seriesProgressCache;
        _movieHistoryCache = movieHistoryCache;
        _collectionsIndexCache = collectionsIndexCache;
        _likedIds = likedIds;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "JellyUX - Watchlist Refresh";

    /// <inheritdoc/>
    public string Key => "Jellyfin.Plugin.JuxHomepage.WatchlistRefresh";

    /// <inheritdoc/>
    public string Description => "Refreshes cached Series Progress, Movie History, and Collections index data.";

    /// <inheritdoc/>
    public string Category => "JellyUX Homepage";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Each cache is independently fault-tolerant (logs and swallows its own per-item failures),
        // so no special-casing is needed here beyond delegating to each in sequence.
        await _seriesProgressCache.RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(33);

        await _movieHistoryCache.RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(66);

        await _collectionsIndexCache.RefreshAsync(cancellationToken).ConfigureAwait(false);

        // Read again on the next request: covers what no event reports, such as a library access change.
        _likedIds.Clear();
        progress.Report(100);

        _logger.LogInformation("Watchlist daily refresh completed.");
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(5).Ticks
        };
    }
}
