using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Reads and refreshes the on-disk, per-user Movie History cache under
/// <c>DataPath/Jellyfin.Plugin.JuxHomepage/cache/watchlist/movie-history/</c>, one JSON file per
/// user. Reuses <see cref="DiskJsonCache{T}"/> (TODO_V2.md Phase 7.2), same TTL/refresh-lock shape as
/// <see cref="TMDbCacheService"/>.
/// </summary>
public sealed class MovieHistoryCacheService : IMovieHistoryCacheService, IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly DiskJsonCache<MovieHistoryEntry> _cache;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<MovieHistoryCacheService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieHistoryCacheService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Provides the application data directory path.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="userManager">Jellyfin user manager, used to enumerate every user to refresh.</param>
    /// <param name="libraryManager">Jellyfin library manager, used to enumerate watched movies.</param>
    /// <param name="userDataManager">Jellyfin user data manager, used to read per-movie last-played date.</param>
    /// <param name="logger">Logger.</param>
    public MovieHistoryCacheService(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        ILogger<MovieHistoryCacheService> logger)
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
            "movie-history");

        _cache = new DiskJsonCache<MovieHistoryEntry>(cacheDir, fileSystem, logger);
    }

    /// <inheritdoc/>
    public IReadOnlyList<MovieHistoryEntry> GetHistory(Guid userId) => _cache.Read(GetFileName(userId));

    /// <inheritdoc/>
    public bool IsStale(Guid userId) => _cache.IsStale(GetFileName(userId), Ttl);

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
                    var entries = ComputeHistory(user.Id);
                    _cache.WriteUnlessEmpty(GetFileName(user.Id), entries);
                    refreshed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh movie history cache for user {UserId}.", user.Id);
                }
            }

            _logger.LogInformation("Movie history cache refreshed for {Count} of {Total} user(s).", refreshed, users.Count);
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
            _logger.LogInformation("Movie history refresh already in progress; skipping this request.");
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
    /// Computes every movie this user has watched, most recent first. Exhaustive by design (no
    /// cap), unlike <see cref="Widgets.Personalized.ScoringService"/>'s watched-movies query, which
    /// is intentionally capped for scoring performance -- this cache backs a dedicated "Movie
    /// History" view that must show the full history, not a sample.
    /// </summary>
    private IReadOnlyList<MovieHistoryEntry> ComputeHistory(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return [];
        }

        var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsPlayed = true,
            Recursive = true,
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions { Fields = [], EnableImages = false }
        });

        return movies
            .Select(item => new MovieHistoryEntry
            {
                ItemId = item.Id,
                Name = item.Name,
                LastPlayedDate = _userDataManager.GetUserData(user, item)?.LastPlayedDate
            })
            .ToList();
    }

    private static string GetFileName(Guid userId) => $"{userId}.json";
}
