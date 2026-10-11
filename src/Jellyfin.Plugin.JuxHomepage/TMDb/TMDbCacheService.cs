using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Reads and refreshes the on-disk TMDb data cache under
/// <c>DataPath/Jellyfin.Plugin.JuxHomepage/cache/tmdb/</c>. Deliberately under <c>DataPath</c> rather
/// than <c>PluginConfigurationsPath</c> (which lives inside Jellyfin's own <c>/config/plugins</c> tree,
/// scanned by the core <c>PluginManager</c> for candidate plugin assemblies: a stray DLL there once
/// made Jellyfin disable and wipe the whole configurations tree).
/// Mirrors <see cref="Configuration.UserConfigurationStore"/>'s disk-persistence pattern:
/// <c>System.Text.Json</c> serialization guarded by a <see cref="ReaderWriterLockSlim"/>.
/// </summary>
public sealed class TMDbCacheService : ITMDbCacheService, IDisposable
{
    private readonly DiskJsonCache<TMDbMovie> _movieCache;
    private readonly DiskJsonCache<TMDbShow> _showCache;
    private readonly TMDbExternalIdCache _movieExternalIds;
    private readonly TMDbExternalIdCache _showExternalIds;
    private readonly LibraryCrossReferencer _crossReferencer;
    private readonly ITMDbApiClient _apiClient;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<TMDbCacheService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TMDbCacheService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Provides the application data directory path.</param>
    /// <param name="fileSystem">File system abstraction, for testability, passed through to the composed <see cref="DiskJsonCache{T}"/> instances.</param>
    /// <param name="apiClient">TMDb API client used to fetch fresh data during a refresh.</param>
    /// <param name="libraryManager">Jellyfin library manager, used to cross-reference cached items.</param>
    /// <param name="getConfiguration">
    /// Factory that returns the current plugin configuration, used to read the refresh interval.
    /// Defaults to <c>Plugin.Instance?.Configuration</c> in production.
    /// </param>
    /// <param name="logger">Logger.</param>
    public TMDbCacheService(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        ITMDbApiClient apiClient,
        ILibraryManager libraryManager,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<TMDbCacheService> logger)
    {
        _apiClient = apiClient;
        _getConfiguration = getConfiguration;
        _logger = logger;
        _crossReferencer = new LibraryCrossReferencer(libraryManager, logger);

        var cacheDir = Path.Combine(
            applicationPaths.DataPath,
            "Jellyfin.Plugin.JuxHomepage",
            "cache",
            "tmdb");

        // One DiskJsonCache instance per cached item type, both rooted at the same directory: the
        // movie cache serves the fixed trending/top-rated/now-playing movie files as well as every
        // per-instance Discover file (also movies), the show cache serves the fixed trending/airing
        // today/top-rated show files.
        _movieCache = new DiskJsonCache<TMDbMovie>(cacheDir, fileSystem, logger);
        _showCache = new DiskJsonCache<TMDbShow>(cacheDir, fileSystem, logger);
        _movieExternalIds = new TMDbExternalIdCache(cacheDir, "external_ids_movies.json", fileSystem, apiClient.GetMovieExternalIdsAsync, logger);
        _showExternalIds = new TMDbExternalIdCache(cacheDir, "external_ids_shows.json", fileSystem, apiClient.GetShowExternalIdsAsync, logger);
    }

    /// <inheritdoc/>
    public IReadOnlyList<TMDbMovie> GetTrendingMovies() => _movieCache.Read(GetFileName(TMDbCacheType.TrendingMovies));

    /// <inheritdoc/>
    public IReadOnlyList<TMDbShow> GetTrendingShows() => _showCache.Read(GetFileName(TMDbCacheType.TrendingShows));

    /// <inheritdoc/>
    public IReadOnlyList<TMDbShow> GetAiringToday() => _showCache.Read(GetFileName(TMDbCacheType.AiringToday));

    /// <inheritdoc/>
    public IReadOnlyList<TMDbMovie> GetTopRatedMovies() => _movieCache.Read(GetFileName(TMDbCacheType.TopRatedMovies));

    /// <inheritdoc/>
    public IReadOnlyList<TMDbShow> GetTopRatedShows() => _showCache.Read(GetFileName(TMDbCacheType.TopRatedShows));

    /// <inheritdoc/>
    public IReadOnlyList<TMDbMovie> GetNowPlayingMovies() => _movieCache.Read(GetFileName(TMDbCacheType.NowPlayingMovies));

    /// <inheritdoc/>
    public IReadOnlyList<TMDbMovie> GetDiscoverMovies(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out _))
        {
            return [];
        }

        return _movieCache.Read(GetDiscoverFileName(instanceId));
    }

    /// <inheritdoc/>
    public Task RefreshTrendingMoviesAsync(CancellationToken cancellationToken)
    {
        var pages = _getConfiguration()?.TMDbLists?.TrendingMoviesPages ?? 1;
        return RefreshMoviesAsync(
            TMDbCacheType.TrendingMovies,
            ct => _apiClient.GetTrendingMoviesAsync(pages, ct),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task RefreshTrendingShowsAsync(CancellationToken cancellationToken)
    {
        var pages = _getConfiguration()?.TMDbLists?.TrendingShowsPages ?? 1;
        return RefreshShowsAsync(
            TMDbCacheType.TrendingShows,
            ct => _apiClient.GetTrendingShowsAsync(pages, ct),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task RefreshAiringTodayAsync(CancellationToken cancellationToken)
    {
        var pages = _getConfiguration()?.TMDbLists?.AiringTodayPages ?? 1;
        return RefreshShowsAsync(
            TMDbCacheType.AiringToday,
            ct => _apiClient.GetAiringTodayAsync(pages, ct),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task RefreshTopRatedMoviesAsync(CancellationToken cancellationToken)
    {
        var tmdbLists = _getConfiguration()?.TMDbLists;
        var pages = tmdbLists?.TopRatedMoviesPages ?? 1;
        var voteCountMin = tmdbLists?.TopRatedVoteCountMin ?? 200;
        return RefreshMoviesAsync(
            TMDbCacheType.TopRatedMovies,
            ct => _apiClient.GetTopRatedMoviesAsync(pages, voteCountMin, ct),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task RefreshTopRatedShowsAsync(CancellationToken cancellationToken)
    {
        var tmdbLists = _getConfiguration()?.TMDbLists;
        var pages = tmdbLists?.TopRatedShowsPages ?? 1;
        var voteCountMin = tmdbLists?.TopRatedVoteCountMin ?? 200;
        return RefreshShowsAsync(
            TMDbCacheType.TopRatedShows,
            ct => _apiClient.GetTopRatedShowsAsync(pages, voteCountMin, ct),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task RefreshNowPlayingMoviesAsync(CancellationToken cancellationToken)
    {
        var tmdbLists = _getConfiguration()?.TMDbLists;
        var pages = tmdbLists?.NowPlayingMoviesPages ?? 1;
        var region = tmdbLists?.NowPlayingRegion;
        return RefreshMoviesAsync(
            TMDbCacheType.NowPlayingMovies,
            ct => _apiClient.GetNowPlayingMoviesAsync(pages, region, ct),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RefreshDiscoverMoviesAsync(string instanceId, TMDbDiscoverFilter filter, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(instanceId, out _))
        {
            _logger.LogWarning("Refused to refresh Discover cache: instance id '{InstanceId}' is not a GUID.", instanceId);
            return;
        }

        try
        {
            var items = DeduplicateById(await _apiClient.DiscoverMoviesAsync(filter, cancellationToken).ConfigureAwait(false));
            var matched = await _crossReferencer.CrossReferenceAsync(
                items,
                _movieExternalIds.GetImdbIdAsync,
                [BaseItemKind.Movie],
                cancellationToken).ConfigureAwait(false);
            _movieExternalIds.Save();
            _movieCache.WriteUnlessEmpty(GetDiscoverFileName(instanceId), items);
            _logger.LogInformation(
                "TMDb Discover cache '{InstanceId}' refreshed: {ItemCount} item(s), {MatchedCount} matched to the local library.",
                instanceId,
                items.Count,
                matched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh TMDb Discover cache '{InstanceId}'.", instanceId);
        }
    }

    /// <inheritdoc/>
    public bool IsStale(TMDbCacheType type)
    {
        var hours = _getConfiguration()?.Cache?.TMDbRefreshIntervalHours ?? 24;
        return _movieCache.IsStale(GetFileName(type), TimeSpan.FromHours(hours));
    }

    /// <inheritdoc/>
    public DateTime? GetLastRefreshedUtc(TMDbCacheType type) => _movieCache.GetLastWriteUtc(GetFileName(type));

    /// <inheritdoc/>
    public bool TryAcquireRefreshLock() => _refreshGate.Wait(0);

    /// <inheritdoc/>
    public async Task RunRefreshLockedAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            // Six fixed refreshes, evenly spaced across 0-100.
            Func<CancellationToken, Task>[] steps =
            [
                RefreshTrendingMoviesAsync,
                RefreshTrendingShowsAsync,
                RefreshAiringTodayAsync,
                RefreshTopRatedMoviesAsync,
                RefreshTopRatedShowsAsync,
                RefreshNowPlayingMoviesAsync
            ];

            progress?.Report(0);
            for (var i = 0; i < steps.Length; i++)
            {
                await steps[i](cancellationToken).ConfigureAwait(false);
                progress?.Report((i + 1) * 100.0 / steps.Length);
            }

            var discoverRows = _getConfiguration()?.Widgets?
                .Where(c => c.WidgetType == TMDbWidgetTypes.DiscoverMovies)
                .ToList() ?? [];

            foreach (var row in discoverRows)
            {
                var extra = row.GetExtraParamsDictionary();
                if (!extra.TryGetValue("value", out var instanceId) || string.IsNullOrEmpty(instanceId))
                {
                    continue;
                }

                var filter = TMDbDiscoverFilter.FromExtraParams(extra);
                await RefreshDiscoverMoviesAsync(instanceId, filter, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshAllAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!TryAcquireRefreshLock())
        {
            _logger.LogInformation("TMDb refresh already in progress; skipping this request.");
            return false;
        }

        await RunRefreshLockedAsync(progress, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _movieCache.Dispose();
            _showCache.Dispose();
            _movieExternalIds.Dispose();
            _showExternalIds.Dispose();
            _refreshGate.Dispose();
            _disposed = true;
        }
    }

    private async Task RefreshMoviesAsync(
        TMDbCacheType type,
        Func<CancellationToken, Task<IReadOnlyList<TMDbMovie>>> fetch,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = DeduplicateById(await fetch(cancellationToken).ConfigureAwait(false));
            var matched = await _crossReferencer.CrossReferenceAsync(
                items,
                _movieExternalIds.GetImdbIdAsync,
                [BaseItemKind.Movie],
                cancellationToken).ConfigureAwait(false);
            _movieExternalIds.Save();
            _movieCache.WriteUnlessEmpty(GetFileName(type), items);
            LogRefreshOutcome(type, items.Count, matched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh TMDb cache '{Type}'.", type);
        }
    }

    private async Task RefreshShowsAsync(
        TMDbCacheType type,
        Func<CancellationToken, Task<IReadOnlyList<TMDbShow>>> fetch,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = DeduplicateById(await fetch(cancellationToken).ConfigureAwait(false));
            var matched = await _crossReferencer.CrossReferenceAsync(
                items,
                _showExternalIds.GetImdbIdAsync,
                [BaseItemKind.Series],
                cancellationToken).ConfigureAwait(false);
            _showExternalIds.Save();
            _showCache.WriteUnlessEmpty(GetFileName(type), items);
            LogRefreshOutcome(type, items.Count, matched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh TMDb cache '{Type}'.", type);
        }
    }

    /// <summary>
    /// Removes duplicate entries (same TMDb id) from a freshly fetched page set, keeping the first
    /// occurrence. TMDb's list endpoints are backed by a live, frequently reshuffled ranking, so the
    /// same item can legitimately appear on more than one page when multiple pages are fetched back
    /// to back (e.g. a movie shifts from page 2 to page 1 between requests) -- without this, the
    /// duplicate would be cross-referenced and cached twice, making the same local library item
    /// appear twice in the widget.
    /// </summary>
    private static IReadOnlyList<T> DeduplicateById<T>(IReadOnlyList<T> items)
        where T : IExternalCacheItem
    {
        var seenIds = new HashSet<int>();
        var deduplicated = new List<T>(items.Count);
        foreach (var item in items)
        {
            if (seenIds.Add(item.Id))
            {
                deduplicated.Add(item);
            }
        }

        return deduplicated;
    }

    /// <summary>
    /// Logs an explicit, unambiguous summary of a refresh's outcome. In particular, an empty item
    /// count almost always means the API key is missing/invalid or the request failed -- both of
    /// which are already logged in detail by <see cref="TMDbApiClient"/> -- rather than a genuine
    /// "TMDb has nothing to report" result.
    /// </summary>
    private void LogRefreshOutcome(TMDbCacheType type, int itemCount, int matchedCount)
    {
        if (itemCount == 0)
        {
            _logger.LogInformation(
                "TMDb cache '{Type}' refresh returned 0 items -- check for a preceding TMDbApiClient warning/error (missing or invalid API key, or a network failure). The previous cache, if any, was left untouched.",
                type);
            return;
        }

        _logger.LogInformation(
            "TMDb cache '{Type}' refreshed: {ItemCount} item(s), {MatchedCount} matched to the local library.",
            type,
            itemCount,
            matchedCount);
    }

    /// <summary>
    /// Builds the cache file name for a Discover widget instance. <paramref name="instanceId"/> is
    /// validated as a GUID by the only two callers (<see cref="RefreshDiscoverMoviesAsync"/> and
    /// <see cref="GetDiscoverMovies"/> via the widget's own <c>ExtraParams["value"]</c>-derived
    /// AdditionalData) before it ever reaches this method, but re-validating here is cheap defense
    /// in depth against a malformed instance id being used as a file name.
    /// </summary>
    private static string GetDiscoverFileName(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out var validated))
        {
            throw new ArgumentException("Discover instance id must be a GUID.", nameof(instanceId));
        }

        return $"discover_{validated:N}.json";
    }

    private static string GetFileName(TMDbCacheType type) => type switch
    {
        TMDbCacheType.TrendingMovies => "trending_movies.json",
        TMDbCacheType.TrendingShows => "trending_shows.json",
        TMDbCacheType.AiringToday => "airing_today.json",
        TMDbCacheType.TopRatedMovies => "top_rated_movies.json",
        TMDbCacheType.TopRatedShows => "top_rated_shows.json",
        TMDbCacheType.NowPlayingMovies => "now_playing_movies.json",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown TMDb cache type.")
    };
}
