using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <inheritdoc cref="IStatisticsService"/>
public sealed class StatisticsService : IStatisticsService
{
    private readonly ISeriesProgressCacheService _seriesProgressCache;
    /// <summary>The number of series of the Top 5 block (TODO_V4.md 8.5, F-55).</summary>
    public const int TopSeriesCount = 5;

    // Candidates checked against the user's rights to fill the Top 5: a series the user can no
    // longer open drops out and the next one moves up, without resolving the whole history.
    private const int TopSeriesCandidates = 25;

    private readonly IMovieHistoryCacheService _movieHistoryCache;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatisticsService"/> class.
    /// </summary>
    /// <param name="seriesProgressCache">Series Progress disk cache.</param>
    /// <param name="movieHistoryCache">Movie History disk cache.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="timeProvider">Clock of the "this month" count. Defaults to <see cref="TimeProvider.System"/>.</param>
    public StatisticsService(
        ISeriesProgressCacheService seriesProgressCache,
        IMovieHistoryCacheService movieHistoryCache,
        IUserManager userManager,
        ILibraryManager libraryManager,
        TimeProvider? timeProvider = null)
    {
        _seriesProgressCache = seriesProgressCache;
        _movieHistoryCache = movieHistoryCache;
        _userManager = userManager;
        _libraryManager = libraryManager;
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
            EpisodesWatched = progress.Sum(e => e.WatchedEpisodes),
            TopSeries = GetTopSeries(userId, progress)
        };
    }

    /// <summary>
    /// The most watched series: most episodes watched first, then the most recently watched,
    /// finished series included, limited to those the user may still open.
    /// </summary>
    private IReadOnlyList<TopSeriesEntry> GetTopSeries(Guid userId, IReadOnlyList<SeriesProgressEntry> progress)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null || progress.Count == 0)
        {
            return [];
        }

        var candidates = progress
            .OrderByDescending(e => e.WatchedEpisodes)
            .ThenByDescending(e => e.LastPlayedDate ?? DateTime.MinValue)
            .Take(TopSeriesCandidates)
            .ToList();
        var entriesById = candidates.GroupBy(e => e.SeriesId).ToDictionary(g => g.Key, g => g.First());

        return UserVisibleItems.Resolve(_libraryManager, user, candidates.Select(e => e.SeriesId).ToList(), out _)
            .Take(TopSeriesCount)
            .Select(item => new TopSeriesEntry
            {
                SeriesId = item.Id,
                Name = item.Name,
                WatchedEpisodes = entriesById[item.Id].WatchedEpisodes,
                TotalEpisodes = entriesById[item.Id].TotalEpisodes
            })
            .ToList();
    }
}
