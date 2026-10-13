using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Reads and refreshes the on-disk, per-user Series Progress cache under
/// <c>DataPath/Jellyfin.Plugin.JuxHomepage/cache/watchlist/series-progress/</c>, one JSON file per
/// user (same convention as <see cref="Configuration.UserConfigurationStore"/>). Reuses
/// <see cref="DiskJsonCache{T}"/> (TODO_V2.md Phase 7.2), same TTL/refresh-lock shape as
/// <see cref="TMDbCacheService"/>.
/// </summary>
public sealed class SeriesProgressCacheService : ISeriesProgressCacheService, IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly DiskJsonCache<SeriesProgressEntry> _cache;
    private readonly PerUserComputedCache<SeriesProgressEntry> _perUser;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<SeriesProgressCacheService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesProgressCacheService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Provides the application data directory path.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="userManager">Jellyfin user manager, used to enumerate every user to refresh.</param>
    /// <param name="libraryManager">Jellyfin library manager, used to enumerate episodes.</param>
    /// <param name="userDataManager">Jellyfin user data manager, used to read per-episode played state.</param>
    /// <param name="logger">Logger.</param>
    public SeriesProgressCacheService(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        ILogger<SeriesProgressCacheService> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logger;

        var cacheDir = Path.Combine(
            applicationPaths.DataPath,
            "Jellyfin.Plugin.JuxHomepage",
            "cache",
            "watchlist",
            "series-progress");

        _cache = new DiskJsonCache<SeriesProgressEntry>(cacheDir, fileSystem, logger);
        _perUser = new PerUserComputedCache<SeriesProgressEntry>(_cache, Ttl, ComputeProgress, logger);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SeriesProgressEntry> GetProgress(Guid userId) => _perUser.Get(userId);

    /// <inheritdoc/>
    public bool IsStale(Guid userId) => _perUser.IsStale(userId);

    /// <inheritdoc/>
    public void UpdateSeries(Guid userId, IReadOnlyCollection<Guid> seriesIds)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null || seriesIds.Count == 0)
        {
            return;
        }

        _perUser.Update(userId, entries =>
        {
            var updated = Aggregate(user, QueryEpisodes(user, seriesIds.ToArray()));
            return entries
                .Where(e => !seriesIds.Contains(e.SeriesId))
                .Concat(updated)
                .OrderByDescending(e => e.LastPlayedDate ?? DateTime.MinValue)
                .ToList();
        });
    }

    /// <inheritdoc/>
    public bool TryAcquireRefreshLock() => _refreshGate.Wait(0);

    /// <inheritdoc/>
    public Task RunRefreshLockedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var users = _userManager.GetUsers().ToList();
            var refreshed = 0;

            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    _perUser.Refresh(user.Id);
                    refreshed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh series progress cache for user {UserId}.", user.Id);
                }
            }

            _logger.LogInformation("Series progress cache refreshed for {Count} of {Total} user(s).", refreshed, users.Count);
        }
        finally
        {
            _refreshGate.Release();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshAllAsync(CancellationToken cancellationToken)
    {
        if (!TryAcquireRefreshLock())
        {
            _logger.LogInformation("Series progress refresh already in progress; skipping this request.");
            return false;
        }

        await RunRefreshLockedAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cache.Dispose();
            _refreshGate.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Computes every series this user has watched at least one episode of, with watched/total
    /// episode counts. Exhaustive by design (no cap), unlike
    /// <see cref="Widgets.Personalized.ScoringService"/> whose watched-history scan is intentionally
    /// capped for scoring performance -- this cache backs
    /// a dedicated "Series Progress" view that must show every in-progress series, not a sample.
    /// </summary>
    private IReadOnlyList<SeriesProgressEntry> ComputeProgress(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return [];
        }

        // Only the episodes of series with at least one played episode (F-06): reading every
        // episode of the library for every user scaled with users x episodes, while the result
        // drops unwatched series anyway. Images are never read here.
        var noImages = new MediaBrowser.Controller.Dto.DtoOptions { Fields = [], EnableImages = false };
        var watchedSeriesIds = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                IsPlayed = true,
                Recursive = true,
                IsVirtualItem = false,
                DtoOptions = noImages
            })
            .OfType<Episode>()
            .Select(episode => episode.SeriesId)
            .Where(seriesId => seriesId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (watchedSeriesIds.Length == 0)
        {
            return [];
        }

        return Aggregate(user, QueryEpisodes(user, watchedSeriesIds));
    }

    private IReadOnlyList<BaseItem> QueryEpisodes(User user, Guid[] seriesIds) =>
        _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = seriesIds,
            Recursive = true,
            IsVirtualItem = false,
            DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions { Fields = [], EnableImages = false }
        });

    /// <summary>
    /// Builds one entry per series with at least one played episode, most recently played first.
    /// </summary>
    private IReadOnlyList<SeriesProgressEntry> Aggregate(User user, IReadOnlyList<BaseItem> episodes)
    {
        var bySeries = new Dictionary<Guid, (string Name, int Watched, int Total, DateTime? LastPlayed, Episode? LastEpisode)>();

        foreach (var item in episodes)
        {
            if (item is not Episode episode || episode.SeriesId == Guid.Empty)
            {
                continue;
            }

            var seriesId = episode.SeriesId;
            bySeries.TryGetValue(seriesId, out var current);

            var played = _userDataManager.GetUserData(user, episode);
            var isWatched = played?.Played == true;
            var lastPlayed = played?.LastPlayedDate;

            var newLastPlayed = current.LastPlayed;
            var newLastEpisode = current.LastEpisode;
            if (lastPlayed.HasValue && (!newLastPlayed.HasValue || lastPlayed > newLastPlayed))
            {
                newLastPlayed = lastPlayed;
                newLastEpisode = episode;
            }

            bySeries[seriesId] = (
                string.IsNullOrEmpty(current.Name) ? episode.SeriesName ?? string.Empty : current.Name,
                current.Watched + (isWatched ? 1 : 0),
                current.Total + 1,
                newLastPlayed,
                newLastEpisode);
        }

        return bySeries
            .Where(kv => kv.Value.Watched > 0)
            .Select(kv => new SeriesProgressEntry
            {
                SeriesId = kv.Key,
                SeriesName = kv.Value.Name,
                WatchedEpisodes = kv.Value.Watched,
                TotalEpisodes = kv.Value.Total,
                LastPlayedDate = kv.Value.LastPlayed,
                LastEpisodeId = kv.Value.LastEpisode?.Id,
                LastEpisodeName = kv.Value.LastEpisode?.Name,
                LastEpisodeSeasonNumber = kv.Value.LastEpisode?.ParentIndexNumber,
                LastEpisodeIndexNumber = kv.Value.LastEpisode?.IndexNumber
            })
            .OrderByDescending(e => e.LastPlayedDate ?? DateTime.MinValue)
            .ToList();
    }
}
