using System.Collections.Concurrent;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
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
    /// <summary>
    /// How often the TMDb scheduled task runs (<see cref="Tasks.TMDbDailyRefreshTask"/>), and the
    /// tolerance a list's age gets before it is due (DEC-P06-3).
    /// </summary>
    internal static readonly TimeSpan RefreshTaskPeriod = TimeSpan.FromHours(1);

    /// <summary>
    /// The properties of a cached title written to disk: the widgets read only <c>libraryItemId</c>
    /// (<c>ConnectedWidgetBase</c>) and the status counts the entries; the id names the entry
    /// (Annexe.b, DEC-P07-11).
    /// </summary>
    private static readonly string[] StoredProperties = ["id", "libraryItemId"];

    private readonly DiskJsonCache<TMDbMovie> _movieCache;
    private readonly DiskJsonCache<TMDbShow> _showCache;
    private readonly TMDbExternalIdCache _movieExternalIds;
    private readonly TMDbExternalIdCache _showExternalIds;
    private readonly LibraryCrossReferencer _crossReferencer;
    private readonly ITMDbApiClient _apiClient;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<TMDbCacheService> _logger;
    private readonly SectionResultCache? _sectionResults;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // Why each fixed list last came back empty, with the API key it was asked with and when (FIAB-10.c,
    // DEC-P10-3; the time tells a new failure from an old one after Refresh now, ADM-02.a).
    private readonly ConcurrentDictionary<TMDbCacheType, (TMDbFailureKind Kind, string? ApiKey, DateTime AtUtc)> _lastErrors = new();
    private readonly TimeProvider _timeProvider;
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
    /// <param name="timeProvider">Time source of the cache ages. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="sectionResults">Home section pages, dropped when a list is rewritten.</param>
    public TMDbCacheService(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        ITMDbApiClient apiClient,
        ILibraryManager libraryManager,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<TMDbCacheService> logger,
        TimeProvider? timeProvider = null,
        SectionResultCache? sectionResults = null)
    {
        _apiClient = apiClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sectionResults = sectionResults;
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
        _movieCache = new DiskJsonCache<TMDbMovie>(cacheDir, fileSystem, logger, timeProvider, StoredProperties);
        _showCache = new DiskJsonCache<TMDbShow>(cacheDir, fileSystem, logger, timeProvider, StoredProperties);
        _movieExternalIds = new TMDbExternalIdCache(cacheDir, "external_ids_movies.json", fileSystem, apiClient.GetMovieExternalIdsAsync, logger, timeProvider);
        _showExternalIds = new TMDbExternalIdCache(cacheDir, "external_ids_shows.json", fileSystem, apiClient.GetShowExternalIdsAsync, logger, timeProvider);
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

    private Task RefreshTrendingMoviesAsync(RefreshIndexes indexes, CancellationToken cancellationToken)
    {
        var pages = _getConfiguration()?.TMDbLists?.TrendingMoviesPages ?? 1;
        return RefreshMoviesAsync(
            TMDbCacheType.TrendingMovies,
            ct => _apiClient.GetTrendingMoviesAsync(pages, ct),
            indexes,
            cancellationToken);
    }

    private Task RefreshTrendingShowsAsync(RefreshIndexes indexes, CancellationToken cancellationToken)
    {
        var pages = _getConfiguration()?.TMDbLists?.TrendingShowsPages ?? 1;
        return RefreshShowsAsync(
            TMDbCacheType.TrendingShows,
            ct => _apiClient.GetTrendingShowsAsync(pages, ct),
            indexes,
            cancellationToken);
    }

    private Task RefreshAiringTodayAsync(RefreshIndexes indexes, CancellationToken cancellationToken)
    {
        var pages = _getConfiguration()?.TMDbLists?.AiringTodayPages ?? 1;
        return RefreshShowsAsync(
            TMDbCacheType.AiringToday,
            ct => _apiClient.GetAiringTodayAsync(pages, ct),
            indexes,
            cancellationToken);
    }

    private Task RefreshTopRatedMoviesAsync(RefreshIndexes indexes, CancellationToken cancellationToken)
    {
        var tmdbLists = _getConfiguration()?.TMDbLists;
        var pages = tmdbLists?.TopRatedMoviesPages ?? 1;
        var voteCountMin = tmdbLists?.TopRatedVoteCountMin ?? 200;
        return RefreshMoviesAsync(
            TMDbCacheType.TopRatedMovies,
            ct => _apiClient.GetTopRatedMoviesAsync(pages, voteCountMin, ct),
            indexes,
            cancellationToken);
    }

    private Task RefreshTopRatedShowsAsync(RefreshIndexes indexes, CancellationToken cancellationToken)
    {
        var tmdbLists = _getConfiguration()?.TMDbLists;
        var pages = tmdbLists?.TopRatedShowsPages ?? 1;
        var voteCountMin = tmdbLists?.TopRatedVoteCountMin ?? 200;
        return RefreshShowsAsync(
            TMDbCacheType.TopRatedShows,
            ct => _apiClient.GetTopRatedShowsAsync(pages, voteCountMin, ct),
            indexes,
            cancellationToken);
    }

    private Task RefreshNowPlayingMoviesAsync(RefreshIndexes indexes, CancellationToken cancellationToken)
    {
        var tmdbLists = _getConfiguration()?.TMDbLists;
        var pages = tmdbLists?.NowPlayingMoviesPages ?? 1;
        var region = tmdbLists?.NowPlayingRegion;
        return RefreshMoviesAsync(
            TMDbCacheType.NowPlayingMovies,
            ct => _apiClient.GetNowPlayingMoviesAsync(pages, region, ct),
            indexes,
            cancellationToken);
    }

    private async Task RefreshDiscoverMoviesAsync(string instanceId, TMDbDiscoverFilter filter, RefreshIndexes indexes, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(instanceId, out _))
        {
            _logger.LogWarning("Refused to refresh Discover cache: instance id '{InstanceId}' is not a GUID.", instanceId);
            return;
        }

        try
        {
            var items = DeduplicateById(await _apiClient.DiscoverMoviesAsync(filter, cancellationToken).ConfigureAwait(false));
            var matched = await MatchAsync(items, _movieExternalIds.GetImdbIdAsync, () => indexes.Movies, cancellationToken).ConfigureAwait(false);
            _movieExternalIds.Save();
            _movieCache.WriteUnlessEmpty(GetDiscoverFileName(instanceId), items);
            _sectionResults?.Clear();
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
    public bool IsStale(TMDbCacheType type) => IsDue(GetFileName(type));

    /// <inheritdoc/>
    public DateTime? GetLastRefreshedUtc(TMDbCacheType type) => _movieCache.GetLastWriteUtc(GetFileName(type));

    /// <inheritdoc/>
    public TMDbFailureKind? GetLastError(TMDbCacheType type) => CurrentError(type)?.Kind;

    /// <inheritdoc/>
    public DateTime? GetLastErrorUtc(TMDbCacheType type) => CurrentError(type)?.AtUtc;

    private (TMDbFailureKind Kind, string? ApiKey, DateTime AtUtc)? CurrentError(TMDbCacheType type) =>
        _lastErrors.TryGetValue(type, out var error) && error.ApiKey == _getConfiguration()?.ApiKeys?.TMDb
            ? error
            : null;

    /// <inheritdoc/>
    public bool TryAcquireRefreshLock() => _refreshGate.Wait(0);

    /// <inheritdoc/>
    public Task RunRefreshLockedAsync(IProgress<double>? progress, CancellationToken cancellationToken) =>
        RunLockedAsync(force: true, progress, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> RefreshDueAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!TryAcquireRefreshLock())
        {
            _logger.LogInformation("TMDb refresh already in progress; skipping this run.");
            return false;
        }

        await RunLockedAsync(force: false, progress, cancellationToken).ConfigureAwait(false);
        return true;
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
        RefreshIndexes indexes,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await FetchRecordingErrorAsync(type, fetch, cancellationToken).ConfigureAwait(false);
            var matched = await MatchAsync(items, _movieExternalIds.GetImdbIdAsync, () => indexes.Movies, cancellationToken).ConfigureAwait(false);
            _movieExternalIds.Save();
            _movieCache.WriteUnlessEmpty(GetFileName(type), items);
            _sectionResults?.Clear();
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
        RefreshIndexes indexes,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await FetchRecordingErrorAsync(type, fetch, cancellationToken).ConfigureAwait(false);
            var matched = await MatchAsync(items, _showExternalIds.GetImdbIdAsync, () => indexes.Shows, cancellationToken).ConfigureAwait(false);
            _showExternalIds.Save();
            _showCache.WriteUnlessEmpty(GetFileName(type), items);
            _sectionResults?.Clear();
            LogRefreshOutcome(type, items.Count, matched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh TMDb cache '{Type}'.", type);
        }
    }

    /// <summary>
    /// Fetches a fixed list and keeps why it came back empty, if a request failed, or clears it
    /// (<c>RefreshAllAsync_InvalidKey_KeepsWhyEachListIsEmptyUntilALaterSuccess</c>). Only the list requests are
    /// recorded, not the external-id lookups that follow.
    /// </summary>
    private async Task<IReadOnlyList<T>> FetchRecordingErrorAsync<T>(
        TMDbCacheType type,
        Func<CancellationToken, Task<IReadOnlyList<T>>> fetch,
        CancellationToken cancellationToken)
        where T : IExternalCacheItem
    {
        var apiKey = _getConfiguration()?.ApiKeys?.TMDb;
        IReadOnlyList<T> items;
        TMDbFailureKind? failure;
        using (var recorder = TMDbFailureRecorder.Begin())
        {
            items = DeduplicateById(await fetch(cancellationToken).ConfigureAwait(false));
            failure = recorder.LastFailure;
        }

        if (items.Count == 0 && failure is { } kind)
        {
            _lastErrors[type] = (kind, apiKey, _timeProvider.GetUtcNow().UtcDateTime);
        }
        else
        {
            _lastErrors.TryRemove(type, out _);
        }

        return items;
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
    /// <summary>
    /// Matches a fetched list against the library; an empty list costs no library read (R11,
    /// <c>RefreshDueAsync_ListsReturnNothing_DoesNotReadTheLibrary</c>).
    /// </summary>
    private async Task<int> MatchAsync<T>(
        IReadOnlyList<T> items,
        Func<int, CancellationToken, Task<string?>> getExternalImdbId,
        Func<LibraryProviderIndex> index,
        CancellationToken cancellationToken)
        where T : IExternalCacheItem =>
        items.Count == 0
            ? 0
            : await _crossReferencer.CrossReferenceAsync(items, getExternalImdbId, index(), cancellationToken).ConfigureAwait(false);

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
    /// Refreshes the six fixed lists, then every configured Discover instance, all of them when
    /// <paramref name="force"/> is set, otherwise only the due ones (<see cref="IsDue"/>). Releases
    /// the refresh slot the caller reserved.
    /// </summary>
    private async Task RunLockedAsync(bool force, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            if (force)
            {
                // A forced refresh asks again for the titles TMDb had no IMDb id for (DEC-P06-4,
                // RefreshAllAsync_TitleWithoutImdbId_IsAskedAgainWhereAListRefreshIsNot).
                _movieExternalIds.ClearMissing();
                _showExternalIds.ClearMissing();
            }

            // Without a TMDb key every list would come back empty, every hour (R11,
            // RefreshDueAsync_NoApiKey_AsksTMDbAndTheLibraryNothing).
            if (!force && string.IsNullOrEmpty(_getConfiguration()?.ApiKeys?.TMDb))
            {
                _logger.LogDebug("TMDb API key not configured; skipping the scheduled TMDb refresh.");
                return;
            }

            // Six fixed refreshes, evenly spaced across 0-100, matched against one library read per
            // item kind, made only if a list needs it (DEC-P06-1, TMDb refresh budget in
            // ci/perf-budget.json).
            var indexes = new RefreshIndexes(_crossReferencer);
            (TMDbCacheType Type, Func<RefreshIndexes, CancellationToken, Task> Refresh)[] steps =
            [
                (TMDbCacheType.TrendingMovies, RefreshTrendingMoviesAsync),
                (TMDbCacheType.TrendingShows, RefreshTrendingShowsAsync),
                (TMDbCacheType.AiringToday, RefreshAiringTodayAsync),
                (TMDbCacheType.TopRatedMovies, RefreshTopRatedMoviesAsync),
                (TMDbCacheType.TopRatedShows, RefreshTopRatedShowsAsync),
                (TMDbCacheType.NowPlayingMovies, RefreshNowPlayingMoviesAsync)
            ];

            var skipped = 0;
            progress?.Report(0);
            for (var i = 0; i < steps.Length; i++)
            {
                if (force || IsStale(steps[i].Type))
                {
                    await steps[i].Refresh(indexes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    skipped++;
                }

                progress?.Report((i + 1) * 100.0 / steps.Length);
            }

            var discoverRows = _getConfiguration()?.Widgets?
                .Where(c => c.WidgetType == TMDbWidgetTypes.DiscoverMovies)
                .ToList() ?? [];

            foreach (var row in discoverRows)
            {
                var extra = row.GetExtraParamsDictionary();
                if (!extra.TryGetValue(DiscoverMoviesWidget.ValueParam, out var instanceId) || string.IsNullOrEmpty(instanceId))
                {
                    continue;
                }

                if (!force && Guid.TryParse(instanceId, out _) && !IsDue(GetDiscoverFileName(instanceId)))
                {
                    skipped++;
                    continue;
                }

                var filter = TMDbDiscoverFilter.FromExtraParams(extra);
                await RefreshDiscoverMoviesAsync(instanceId, filter, indexes, cancellationToken).ConfigureAwait(false);
            }

            if (skipped > 0)
            {
                _logger.LogInformation("TMDb refresh skipped {Count} list(s) refreshed within the configured interval.", skipped);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Returns whether a cache file is missing or due: older than
    /// <see cref="CacheConfig.TMDbRefreshIntervalHours"/> less one <see cref="RefreshTaskPeriod"/>.
    /// The tolerance keeps a list written a moment after a run started from waiting one more run,
    /// so a 24 h interval refreshes every day, not every other day (QUAL-01, DEC-P06-3,
    /// <c>TMDbRefreshIntervalHours_24h_DailyTaskRefreshesEveryDay</c>).
    /// </summary>
    private bool IsDue(string fileName)
    {
        var hours = _getConfiguration()?.Cache?.TMDbRefreshIntervalHours ?? 24;
        return _movieCache.IsStale(fileName, TimeSpan.FromHours(hours) - RefreshTaskPeriod);
    }

    /// <summary>
    /// Builds the cache file name for a Discover widget instance. <paramref name="instanceId"/> is
    /// validated as a GUID by the only two callers (<see cref="RefreshDiscoverMoviesAsync(string, TMDbDiscoverFilter, RefreshIndexes, CancellationToken)"/> and
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

    /// <summary>
    /// The library indexes of one refresh, each read on first use only: a refresh that touches no
    /// show list never reads the shows (DEC-P06-1). A failed read is not kept, so it fails its own
    /// list only (<c>RefreshAllAsync_LibraryReadFailsOnce_LaterListsStillRefresh</c>).
    /// </summary>
    private sealed class RefreshIndexes(LibraryCrossReferencer crossReferencer)
    {
        private readonly Lazy<LibraryProviderIndex> _movies = new(() => crossReferencer.BuildIndex([BaseItemKind.Movie]), LazyThreadSafetyMode.PublicationOnly);
        private readonly Lazy<LibraryProviderIndex> _shows = new(() => crossReferencer.BuildIndex([BaseItemKind.Series]), LazyThreadSafetyMode.PublicationOnly);

        public LibraryProviderIndex Movies => _movies.Value;

        public LibraryProviderIndex Shows => _shows.Value;
    }
}
