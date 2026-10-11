using System.Globalization;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.TMDb;

public sealed class TMDbCacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-tmdb-tests-" + Guid.NewGuid());

    private TMDbCacheService BuildService(
        ITMDbApiClient apiClient,
        ILibraryManager libraryManager,
        int refreshIntervalHours = 24)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new TMDbCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            apiClient,
            libraryManager,
            () => new PluginConfiguration { Cache = new CacheConfig { TMDbRefreshIntervalHours = refreshIntervalHours } },
            NullLogger<TMDbCacheService>.Instance);
    }

    private TMDbCacheService BuildServiceWithWidgets(ITMDbApiClient apiClient, ILibraryManager libraryManager, WidgetConfig[] widgets)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new TMDbCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            apiClient,
            libraryManager,
            () => new PluginConfiguration { Widgets = widgets },
            NullLogger<TMDbCacheService>.Instance);
    }

    private string CacheFilePath => Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage", "cache", "tmdb", "trending_movies.json");

    private string DiscoverCacheFilePath(string instanceId) =>
        Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage", "cache", "tmdb", $"discover_{Guid.Parse(instanceId):N}.json");

    // A library manager mock that never matches anything, for tests that only care about the
    // fetch/write/staleness mechanics, not cross-referencing itself.
    private static ILibraryManager NoMatchLibraryManager()
    {
        var mock = new Mock<ILibraryManager>();
        mock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        return mock.Object;
    }

    // -------------------------------------------------------------------------
    // IsStale
    // -------------------------------------------------------------------------

    [Fact]
    public void IsStale_FileAbsent_ReturnsTrue()
    {
        var service = BuildService(new Mock<ITMDbApiClient>().Object, new Mock<ILibraryManager>().Object);

        Assert.True(service.IsStale(TMDbCacheType.TrendingMovies));
    }

    [Fact]
    public async Task IsStale_FreshlyRefreshed_ReturnsFalse()
    {
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "A" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = BuildService(apiClientMock.Object, NoMatchLibraryManager(), refreshIntervalHours: 24);

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);

        Assert.False(service.IsStale(TMDbCacheType.TrendingMovies));
    }

    [Fact]
    public async Task IsStale_OlderThanConfiguredInterval_ReturnsTrue()
    {
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "A" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = BuildService(apiClientMock.Object, NoMatchLibraryManager(), refreshIntervalHours: 1);

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        File.SetLastWriteTimeUtc(CacheFilePath, DateTime.UtcNow.AddHours(-2));

        Assert.True(service.IsStale(TMDbCacheType.TrendingMovies));
    }

    // -------------------------------------------------------------------------
    // GetTrendingMovies before any refresh
    // -------------------------------------------------------------------------

    [Fact]
    public void GetTrendingMovies_NoRefreshYet_ReturnsEmpty()
    {
        var service = BuildService(new Mock<ITMDbApiClient>().Object, new Mock<ILibraryManager>().Object);

        Assert.Empty(service.GetTrendingMovies());
    }

    // -------------------------------------------------------------------------
    // Cross-referencing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshTrendingMoviesAsync_ImdbMatchFound_SetsLibraryItemId()
    {
        var libraryItem = new Movie { Id = Guid.NewGuid(), Name = "Inception" };

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 27205, Title = "Inception" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(27205, It.IsAny<CancellationToken>()))
            .ReturnsAsync("tt1375666");

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Imdb"))))
            .Returns([libraryItem.Id]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        var result = service.GetTrendingMovies();

        Assert.Single(result);
        Assert.Equal(libraryItem.Id, result[0].LibraryItemId);
    }

    [Fact]
    public async Task RefreshTrendingMoviesAsync_NoImdbMatch_FallsBackToTmdbId()
    {
        var libraryItem = new Movie { Id = Guid.NewGuid(), Name = "Inception" };

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 27205, Title = "Inception" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(27205, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Tmdb")
                     && q.HasAnyProviderId["Tmdb"] == "27205")))
            .Returns([libraryItem.Id]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        var result = service.GetTrendingMovies();

        Assert.Single(result);
        Assert.Equal(libraryItem.Id, result[0].LibraryItemId);
    }

    [Fact]
    public async Task RefreshTrendingMoviesAsync_NoMatchAtAll_LibraryItemIdStaysNull()
    {
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 999, Title = "Unknown" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns([]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        var result = service.GetTrendingMovies();

        Assert.Single(result);
        Assert.Null(result[0].LibraryItemId);
    }

    // -------------------------------------------------------------------------
    // Duplicate TMDb ids across pages must be collapsed (regression test: TMDb's list endpoints are
    // backed by a live, frequently reshuffled ranking, so the same movie can legitimately appear on
    // more than one fetched page -- without deduplication this made the same library item appear
    // twice in the widget).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshTrendingMoviesAsync_SameIdOnMultiplePages_CollapsesToSingleEntry()
    {
        var libraryItem = new Movie { Id = Guid.NewGuid(), Name = "Shelter" };

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[
                new TMDbMovie { Id = 555, Title = "Shelter" },
                new TMDbMovie { Id = 555, Title = "Shelter" }
            ]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(555, It.IsAny<CancellationToken>()))
            .ReturnsAsync("tt32357218");

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Imdb"))))
            .Returns([libraryItem.Id]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        var result = service.GetTrendingMovies();

        Assert.Single(result);
        Assert.Equal(libraryItem.Id, result[0].LibraryItemId);
        apiClientMock.Verify(c => c.GetMovieExternalIdsAsync(555, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Empty refresh must not clobber a previously-populated cache (regression test: a fetch
    // failure, e.g. an invalid API key producing HTTP 401s, previously overwrote a good cache with
    // an empty one, silently destroying real cross-referenced data).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshTrendingMoviesAsync_EmptyResultAfterPopulatedCache_PreservesExistingCache()
    {
        var libraryItem = new Movie { Id = Guid.NewGuid(), Name = "Inception" };

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 27205, Title = "Inception" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(27205, It.IsAny<CancellationToken>()))
            .ReturnsAsync("tt1375666");

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns([libraryItem.Id]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);

        // First refresh succeeds and populates the cache.
        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        Assert.Single(service.GetTrendingMovies());

        // A subsequent refresh fails (e.g. the configured key became invalid) and the API client
        // degrades to an empty list, per its documented contract.
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);

        // The good cache from the first refresh must still be there.
        var result = service.GetTrendingMovies();
        Assert.Single(result);
        Assert.Equal("Inception", result[0].Title);
    }

    // -------------------------------------------------------------------------
    // CrossReferenceAsync: bounded parallelism (Phase 3.1 of TODO_V2.md)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshTrendingMoviesAsync_ManyItems_CrossReferencesInParallelWithCorrectResults()
    {
        const int ItemCount = 30;
        var perCallDelay = TimeSpan.FromMilliseconds(50);

        var movies = Enumerable.Range(1, ItemCount)
            .Select(i => new TMDbMovie { Id = i, Title = "Movie " + i })
            .ToList();

        // Even-id movies have an IMDb id and a matching library item; odd-id movies match nothing --
        // a mix, so the test also confirms correctness survives running out of order/concurrently.
        var libraryItemsByTmdbId = movies
            .Where(m => m.Id % 2 == 0)
            .ToDictionary(m => m.Id, _ => new Movie { Id = Guid.NewGuid(), Name = "Owned" });

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)movies);
        apiClientMock
            .Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (int tmdbId, CancellationToken ct) =>
            {
                await Task.Delay(perCallDelay, ct).ConfigureAwait(false);
                return libraryItemsByTmdbId.ContainsKey(tmdbId) ? "tt" + tmdbId : null;
            });

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasAnyProviderId!.ContainsKey("Imdb"))))
            .Returns((InternalItemsQuery q) =>
            {
                var imdbId = q.HasAnyProviderId!["Imdb"];
                var tmdbId = int.Parse(imdbId.Substring(2), CultureInfo.InvariantCulture);
                return libraryItemsByTmdbId.TryGetValue(tmdbId, out var item) ? [item.Id] : [];
            });
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasAnyProviderId!.ContainsKey("Tmdb"))))
            .Returns([]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        stopwatch.Stop();

        var result = service.GetTrendingMovies();
        Assert.Equal(ItemCount, result.Count);
        foreach (var movie in result)
        {
            var expectedMatched = libraryItemsByTmdbId.ContainsKey(movie.Id);
            Assert.Equal(expectedMatched, movie.LibraryItemId.HasValue);
        }

        // A fully sequential run would take at least ItemCount * perCallDelay (~1.5s here). Bounded
        // concurrency (5 at a time) should take roughly ItemCount / 5 * perCallDelay (~300ms) plus
        // overhead -- well under half the sequential time, proving items are genuinely processed
        // concurrently rather than merely producing the same correct result one at a time.
        var sequentialWorstCase = perCallDelay * ItemCount;
        Assert.True(
            stopwatch.Elapsed < sequentialWorstCase / 2,
            $"Expected parallel cross-referencing to complete well under {sequentialWorstCase}, took {stopwatch.Elapsed}.");
    }

    // -------------------------------------------------------------------------
    // GetLastRefreshedUtc
    // -------------------------------------------------------------------------

    [Fact]
    public void GetLastRefreshedUtc_NoRefreshYet_ReturnsNull()
    {
        var service = BuildService(new Mock<ITMDbApiClient>().Object, new Mock<ILibraryManager>().Object);

        Assert.Null(service.GetLastRefreshedUtc(TMDbCacheType.TrendingMovies));
    }

    [Fact]
    public async Task GetLastRefreshedUtc_AfterRefresh_ReturnsRecentTimestamp()
    {
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "A" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = BuildService(apiClientMock.Object, NoMatchLibraryManager());

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        var lastRefreshed = service.GetLastRefreshedUtc(TMDbCacheType.TrendingMovies);

        Assert.NotNull(lastRefreshed);
        Assert.True(DateTime.UtcNow - lastRefreshed!.Value < TimeSpan.FromMinutes(1));
    }

    // -------------------------------------------------------------------------
    // RefreshAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_CallsAllSixFetchesAndReportsProgress()
    {
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetTrendingShowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetAiringTodayAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetTopRatedMoviesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetTopRatedShowsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetNowPlayingMoviesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);

        var service = BuildService(apiClientMock.Object, new Mock<ILibraryManager>().Object);
        var progress = new SyncProgress();

        await service.RefreshAllAsync(progress, CancellationToken.None);

        apiClientMock.Verify(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(c => c.GetTrendingShowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(c => c.GetAiringTodayAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(c => c.GetTopRatedMoviesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(c => c.GetTopRatedShowsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(c => c.GetNowPlayingMoviesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(7, progress.Values.Count);
        Assert.Equal(0d, progress.Values[0]);
        Assert.Equal(100d, progress.Values[^1]);
        Assert.True(progress.Values.SequenceEqual(progress.Values.OrderBy(v => v)), "Progress values must be non-decreasing.");
    }

    [Fact]
    public async Task RefreshAllAsync_NullProgress_DoesNotThrow()
    {
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "A" }]);
        apiClientMock.Setup(c => c.GetTrendingShowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetAiringTodayAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetTopRatedMoviesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetTopRatedShowsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetNowPlayingMoviesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = BuildService(apiClientMock.Object, NoMatchLibraryManager());

        await service.RefreshAllAsync(null, CancellationToken.None);

        Assert.False(service.IsStale(TMDbCacheType.TrendingMovies));
    }

    // -------------------------------------------------------------------------
    // RefreshAllAsync: concurrency guard (Phase 3.3 of TODO_V2.md)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_ConcurrentCalls_OnlyOneActuallyRuns()
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var callCount = 0;

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock
            .Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref callCount);
                started.Set();
                await Task.Run(() => release.Wait(TimeSpan.FromSeconds(5)));
                return (IReadOnlyList<TMDbMovie>)[];
            });
        apiClientMock.Setup(c => c.GetTrendingShowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetAiringTodayAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetTopRatedMoviesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetTopRatedShowsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetNowPlayingMoviesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);

        var service = BuildService(apiClientMock.Object, new Mock<ILibraryManager>().Object);

        var firstCallTask = service.RefreshAllAsync(null, CancellationToken.None);

        var reachedFetch = started.Wait(TimeSpan.FromSeconds(5));
        Assert.True(reachedFetch, "First refresh did not reach the fetch step in time.");

        // A second concurrent attempt must not be able to reserve the slot while the first is
        // still in progress -- this must be true regardless of the caller (a second manual click,
        // or the daily scheduled task firing at the same moment).
        var secondAcquired = service.TryAcquireRefreshLock();
        Assert.False(secondAcquired, "A second concurrent refresh should not acquire the lock.");

        release.Set();
        var firstRan = await firstCallTask;

        Assert.True(firstRan);
        Assert.Equal(1, callCount);
    }

    // -------------------------------------------------------------------------
    // RefreshAllAsync: configured Discover instances
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_ConfiguredDiscoverInstances_RefreshesEachIntoItsOwnCacheFile()
    {
        var instanceA = Guid.NewGuid().ToString();
        var instanceB = Guid.NewGuid().ToString();

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetTrendingShowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetAiringTodayAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetTopRatedMoviesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetTopRatedShowsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        apiClientMock.Setup(c => c.GetNowPlayingMoviesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        apiClientMock.Setup(c => c.DiscoverMoviesAsync(
                It.Is<TMDbDiscoverFilter>(f => f.SortBy == "popularity.desc"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "From A" }]);
        apiClientMock.Setup(c => c.DiscoverMoviesAsync(
                It.Is<TMDbDiscoverFilter>(f => f.SortBy == "vote_average.desc"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 2, Title = "From B" }]);

        var widgets = new[]
        {
            new WidgetConfig
            {
                WidgetType = TMDbWidgetTypes.DiscoverMovies,
                ExtraParams =
                [
                    new WidgetExtraParam { Key = "value", Value = instanceA },
                    new WidgetExtraParam { Key = "sortBy", Value = "popularity.desc" }
                ]
            },
            new WidgetConfig
            {
                WidgetType = TMDbWidgetTypes.DiscoverMovies,
                ExtraParams =
                [
                    new WidgetExtraParam { Key = "value", Value = instanceB },
                    new WidgetExtraParam { Key = "sortBy", Value = "vote_average.desc" }
                ]
            }
        };

        var service = BuildServiceWithWidgets(apiClientMock.Object, NoMatchLibraryManager(), widgets);

        await service.RefreshAllAsync(null, CancellationToken.None);

        Assert.True(File.Exists(DiscoverCacheFilePath(instanceA)));
        Assert.True(File.Exists(DiscoverCacheFilePath(instanceB)));

        var itemsA = service.GetDiscoverMovies(instanceA);
        var itemsB = service.GetDiscoverMovies(instanceB);
        Assert.Single(itemsA);
        Assert.Equal("From A", itemsA[0].Title);
        Assert.Single(itemsB);
        Assert.Equal("From B", itemsB[0].Title);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // Synchronous IProgress<double> test double -- unlike System.Progress<T>, which marshals
    // callbacks via SynchronizationContext.Post/ThreadPool and is not guaranteed to have delivered
    // them by the time an awaited call returns, this reports immediately and deterministically.
    private sealed class SyncProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }
}
