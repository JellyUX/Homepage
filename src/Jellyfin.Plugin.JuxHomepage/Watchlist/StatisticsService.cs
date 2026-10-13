using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <inheritdoc cref="IStatisticsService"/>
public sealed class StatisticsService : IStatisticsService
{
    private readonly ISeriesProgressCacheService _seriesProgressCache;
    private readonly IMovieHistoryCacheService _movieHistoryCache;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatisticsService"/> class.
    /// </summary>
    /// <param name="seriesProgressCache">Series Progress disk cache.</param>
    /// <param name="movieHistoryCache">Movie History disk cache.</param>
    /// <param name="timeProvider">Clock of the "this month" count. Defaults to <see cref="TimeProvider.System"/>.</param>
    public StatisticsService(
        ISeriesProgressCacheService seriesProgressCache,
        IMovieHistoryCacheService movieHistoryCache,
        TimeProvider? timeProvider = null)
    {
        _seriesProgressCache = seriesProgressCache;
        _movieHistoryCache = movieHistoryCache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public WatchingStatistics GetStatistics(Guid userId)
    {
        var progress = _seriesProgressCache.GetProgress(userId);
        var history = _movieHistoryCache.GetHistory(userId);

        // The month starts at midnight server time, like the daily random order (TODO_V4.md 8.1);
        // play dates are stored in UTC.
        var now = _timeProvider.GetLocalNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).UtcDateTime;

        return new WatchingStatistics
        {
            MoviesWatched = history.Count,
            MoviesWatchedThisMonth = history.Count(e => e.LastPlayedDate >= monthStart),
            SeriesTracked = progress.Count,
            SeriesCompleted = progress.Count(e => e.WatchedEpisodes == e.TotalEpisodes),
            EpisodesWatched = progress.Sum(e => e.WatchedEpisodes)
        };
    }
}
