using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Hosted service that keeps the Progress, History and Statistics tabs current (TODO_V4.md 8.2,
/// F-54): when a user finishes a title or toggles its played state, the movie's History entry and the
/// series' Progress entry are recomputed, instead of waiting for the daily task. Other user-data
/// saves (progress, ratings, favorites) are ignored: they fire continuously during playback.
/// <para>
/// Changes are applied a short delay after the first one and grouped per user: marking a whole series
/// played saves every episode, each with its own event, and they become one update of that series.
/// </para>
/// </summary>
public sealed class TabCachesUpdateListener : UserDataSavedListener
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(2);

    private readonly ISeriesProgressCacheService _seriesProgressCache;
    private readonly IMovieHistoryCacheService _movieHistoryCache;
    private readonly ILogger<TabCachesUpdateListener> _logger;
    private readonly TimeSpan _delay;
    private readonly ConcurrentDictionary<Guid, PendingChanges> _pending = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TabCachesUpdateListener"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="seriesProgressCache">Series Progress disk cache.</param>
    /// <param name="movieHistoryCache">Movie History disk cache.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="delay">How long changes are grouped before they are applied. Defaults to 2 seconds.</param>
    public TabCachesUpdateListener(
        IUserDataManager userDataManager,
        ISeriesProgressCacheService seriesProgressCache,
        IMovieHistoryCacheService movieHistoryCache,
        ILogger<TabCachesUpdateListener> logger,
        TimeSpan? delay = null)
        : base(userDataManager, logger)
    {
        _seriesProgressCache = seriesProgressCache;
        _movieHistoryCache = movieHistoryCache;
        _logger = logger;
        _delay = delay ?? DefaultDelay;
    }

    /// <summary>
    /// Records the change of one saved item. Returns the task that applies the user's grouped
    /// changes when this call scheduled it, otherwise null.
    /// </summary>
    /// <param name="e">The user data save.</param>
    /// <returns>The scheduled application, or null.</returns>
    internal Task? Record(UserDataSaveEventArgs e)
    {
        if (e.SaveReason is not (UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed))
        {
            return null;
        }

        var (movieId, seriesId) = e.Item switch
        {
            Movie movie => (movie.Id, Guid.Empty),
            Episode episode => (Guid.Empty, episode.SeriesId),
            Season season => (Guid.Empty, season.SeriesId),
            Series series => (Guid.Empty, series.Id),
            _ => (Guid.Empty, Guid.Empty)
        };

        if (movieId == Guid.Empty && seriesId == Guid.Empty)
        {
            return null;
        }

        while (true)
        {
            var scheduled = false;
            var pending = _pending.GetOrAdd(e.UserId, _ =>
            {
                scheduled = true;
                return new PendingChanges();
            });

            lock (pending)
            {
                // Closed: its changes are being applied and it is leaving the dictionary; take a new one.
                if (pending.Closed)
                {
                    continue;
                }

                if (movieId != Guid.Empty)
                {
                    pending.MovieIds.Add(movieId);
                }

                if (seriesId != Guid.Empty)
                {
                    pending.SeriesIds.Add(seriesId);
                }
            }

            return scheduled ? ApplyAfterDelayAsync(e.UserId, pending) : null;
        }
    }

    /// <inheritdoc/>
    protected override void OnUserDataSaved(UserDataSaveEventArgs e) => Record(e);

    private async Task ApplyAfterDelayAsync(Guid userId, PendingChanges pending)
    {
        await Task.Delay(_delay).ConfigureAwait(false);

        Guid[] movieIds;
        Guid[] seriesIds;
        lock (pending)
        {
            pending.Closed = true;
            movieIds = [.. pending.MovieIds];
            seriesIds = [.. pending.SeriesIds];
        }

        _pending.TryRemove(new KeyValuePair<Guid, PendingChanges>(userId, pending));

        try
        {
            _movieHistoryCache.UpdateMovies(userId, movieIds);
            _seriesProgressCache.UpdateSeries(userId, seriesIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update the tab caches of user {UserId} after a watch.", userId);
        }
    }

    private sealed class PendingChanges
    {
        public HashSet<Guid> MovieIds { get; } = [];

        public HashSet<Guid> SeriesIds { get; } = [];

        public bool Closed { get; set; }
    }
}
