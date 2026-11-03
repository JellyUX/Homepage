using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Caching;
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
    private readonly PerUserComputedCache<MovieHistoryEntry> _perUser;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly RefreshGate _refreshGate;
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
        _refreshGate = new RefreshGate(logger, "Movie history refresh");

        var cacheDir = Path.Combine(
            applicationPaths.DataPath,
            "Jellyfin.Plugin.JuxHomepage",
            "cache",
            "watchlist",
            "movie-history");

        _cache = new DiskJsonCache<MovieHistoryEntry>(cacheDir, fileSystem, logger);
        _perUser = new PerUserComputedCache<MovieHistoryEntry>(_cache, Ttl, ComputeHistory, logger);
    }

    /// <inheritdoc/>
    public IReadOnlyList<MovieHistoryEntry> GetHistory(Guid userId) => _perUser.Get(userId);

    /// <inheritdoc/>
    public bool IsStale(Guid userId) => _perUser.IsStale(userId);

    /// <inheritdoc/>
    public void UpdateMovies(Guid userId, IReadOnlyCollection<Guid> movieIds)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null || movieIds.Count == 0)
        {
            return;
        }

        _perUser.Update(userId, entries =>
        {
            var played = movieIds
                .Select(id => _libraryManager.GetItemById(id))
                .OfType<BaseItem>()
                .Select(item => (Item: item, Data: _userDataManager.GetUserData(user, item)))
                .Where(x => x.Data?.Played == true)
                .Select(x => new MovieHistoryEntry { ItemId = x.Item.Id, Name = x.Item.Name, LastPlayedDate = x.Data!.LastPlayedDate });
            return entries
                .Where(e => !movieIds.Contains(e.ItemId))
                .Concat(played)
                .OrderByDescending(e => e.LastPlayedDate ?? DateTime.MinValue)
                .ToList();
        });
    }

    /// <inheritdoc/>
    public bool TryAcquireRefreshLock() => _refreshGate.TryEnter();

    /// <inheritdoc/>
    public Task RunRefreshLockedAsync(CancellationToken cancellationToken) =>
        _refreshGate.RunReserved(() => _perUser.RefreshUsers(
            _userManager.GetUsers().Select(u => u.Id).ToList(), "movie history", cancellationToken));

    /// <inheritdoc/>
    public Task<bool> RefreshAllAsync(CancellationToken cancellationToken) =>
        _refreshGate.TryRunAsync(() => RunRefreshLockedAsync(cancellationToken));

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
}
