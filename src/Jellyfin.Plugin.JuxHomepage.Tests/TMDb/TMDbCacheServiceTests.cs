using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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
        int refreshIntervalHours = 24,
        TMDbListsConfig? lists = null,
        string? apiKey = null,
        SectionResultCache? sectionResults = null)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new TMDbCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            apiClient,
            libraryManager,
            () => new PluginConfiguration
            {
                Cache = new CacheConfig { TMDbRefreshIntervalHours = refreshIntervalHours },
                TMDbLists = lists ?? new TMDbListsConfig(),
                ApiKeys = new ApiKeysConfig { TMDb = apiKey }
            },
            NullLogger<TMDbCacheService>.Instance,
            sectionResults: sectionResults);
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
    private static ILibraryManager NoMatchLibraryManager() => LibraryWith();

    // A library manager mock whose library holds exactly these items, whatever item kinds are asked
    // for (these tests only refresh movie lists).
    private static ILibraryManager LibraryWith(params BaseItem[] libraryItems)
    {
        var mock = new Mock<ILibraryManager>();
        mock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([.. libraryItems]);
        return mock.Object;
    }

    private static Movie MovieWith(MetadataProvider provider, string value)
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        movie.SetProviderId(provider, value);
        return movie;
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
    public async Task TMDbLists_TrendingPages_AreThePagesRequestedFromTMDb()
    {
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        apiClientMock.Setup(c => c.GetTrendingShowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbShow>)[]);
        var service = BuildService(apiClientMock.Object, NoMatchLibraryManager(),
            lists: new TMDbListsConfig { TrendingMoviesPages = 3, TrendingShowsPages = 2 });

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        await service.RefreshTrendingShowsAsync(CancellationToken.None);

        apiClientMock.Verify(c => c.GetTrendingMoviesAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        apiClientMock.Verify(c => c.GetTrendingShowsAsync(2, It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Refresh_RewrittenList_DropsTheCachedHomeSectionPages()
    {
        // R11 review (DEC-P07-15): a section page cached before the refresh would keep the old list.
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "A" }]);
        using var sessionCache = new SessionCache();
        using var sections = new SectionResultCache(sessionCache);
        var user = Guid.NewGuid();
        var section = new SectionId(user, "jux.connected.trending-movies", null, null);
        Assert.True(sections.Set(section, 0, 20, new WidgetResult([], 0), sections.StampOf(user), TimeSpan.FromMinutes(15)));

        await BuildService(apiClientMock.Object, NoMatchLibraryManager(), sectionResults: sections).RefreshAllAsync(null, CancellationToken.None);

        Assert.False(sections.TryGet(section, 0, 20, out _));
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
        var libraryItem = MovieWith(MetadataProvider.Imdb, "tt1375666");

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 27205, Title = "Inception" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(27205, It.IsAny<CancellationToken>()))
            .ReturnsAsync("tt1375666");

        var service = BuildService(apiClientMock.Object, LibraryWith(libraryItem));

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        var result = service.GetTrendingMovies();

        Assert.Single(result);
        Assert.Equal(libraryItem.Id, result[0].LibraryItemId);
    }

    [Fact]
    public async Task RefreshTrendingMoviesAsync_NoImdbMatch_FallsBackToTmdbId()
    {
        var libraryItem = MovieWith(MetadataProvider.Tmdb, "27205");

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 27205, Title = "Inception" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(27205, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = BuildService(apiClientMock.Object, LibraryWith(libraryItem));

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

        var service = BuildService(apiClientMock.Object, NoMatchLibraryManager());

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
    public async Task RefreshDueAsync_NoApiKey_AsksTMDbAndTheLibraryNothing()
    {
        // R11: the hourly task does nothing at all on a server without a TMDb key.
        var apiClientMock = new Mock<ITMDbApiClient>();
        var libraryMock = new Mock<ILibraryManager>();
        var service = BuildService(apiClientMock.Object, libraryMock.Object);

        Assert.True(await service.RefreshDueAsync(null, CancellationToken.None));

        apiClientMock.VerifyNoOtherCalls();
        libraryMock.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task RefreshDueAsync_ListsReturnNothing_DoesNotReadTheLibrary()
    {
        // R11: an empty list has nothing to match, so it must not cost a library read.
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[]);
        var libraryMock = new Mock<ILibraryManager>();
        var service = BuildService(apiClientMock.Object, libraryMock.Object, apiKey: "key");

        Assert.True(await service.RefreshDueAsync(null, CancellationToken.None));

        apiClientMock.Verify(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        libraryMock.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAllAsync_LibraryReadFailsOnce_LaterListsStillRefresh()
    {
        // R11: one failed library read fails its own list only; the next list reads the library again.
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "Trending" }]);
        apiClientMock.Setup(c => c.GetTopRatedMoviesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 2, Title = "Top rated" }]);
        var libraryMock = new Mock<ILibraryManager>();
        libraryMock.SetupSequence(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Throws(new InvalidOperationException("database is locked"))
            .Returns([]);
        var service = BuildService(apiClientMock.Object, libraryMock.Object);

        await service.RefreshAllAsync(null, CancellationToken.None);

        Assert.Empty(service.GetTrendingMovies());
        Assert.Single(service.GetTopRatedMovies());
    }

    [Fact]
    public async Task RefreshAllAsync_TitleWithoutImdbId_IsAskedAgainWhereAListRefreshIsNot()
    {
        // Annexe A "TMDb" (DEC-P06-4): a title TMDb has no IMDb id for is not asked again by the
        // next refresh, but "Refresh now" (RefreshAllAsync, RunRefreshLockedAsync) asks again.
        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 555, Title = "Shelter" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(555, It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        var service = BuildService(apiClientMock.Object, NoMatchLibraryManager());

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        await service.RefreshTrendingMoviesAsync(CancellationToken.None);
        apiClientMock.Verify(c => c.GetMovieExternalIdsAsync(555, It.IsAny<CancellationToken>()), Times.Once);

        await service.RefreshAllAsync(null, CancellationToken.None);
        apiClientMock.Verify(c => c.GetMovieExternalIdsAsync(555, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RefreshTrendingMoviesAsync_SameIdOnMultiplePages_CollapsesToSingleEntry()
    {
        var libraryItem = MovieWith(MetadataProvider.Imdb, "tt32357218");

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[
                new TMDbMovie { Id = 555, Title = "Shelter" },
                new TMDbMovie { Id = 555, Title = "Shelter" }
            ]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(555, It.IsAny<CancellationToken>()))
            .ReturnsAsync("tt32357218");

        var service = BuildService(apiClientMock.Object, LibraryWith(libraryItem));

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
        var libraryItem = MovieWith(MetadataProvider.Imdb, "tt1375666");

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 27205, Title = "Inception" }]);
        apiClientMock.Setup(c => c.GetMovieExternalIdsAsync(27205, It.IsAny<CancellationToken>()))
            .ReturnsAsync("tt1375666");

        var service = BuildService(apiClientMock.Object, LibraryWith(libraryItem));

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
        Assert.Equal(27205, result[0].Id);
    }

    // -------------------------------------------------------------------------
    // CrossReferenceAsync: bounded parallelism (Phase 3.1 of TODO_V2.md)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshTrendingMoviesAsync_ManyItems_CrossReferencesInParallelWithCorrectResults()
    {
        const int ItemCount = 30;
        var perCallDelay = TimeSpan.FromMilliseconds(50);
        var inFlight = 0;
        var maxInFlight = 0;

        var movies = Enumerable.Range(1, ItemCount)
            .Select(i => new TMDbMovie { Id = i, Title = "Movie " + i })
            .ToList();

        // Even-id movies have an IMDb id and a library item carrying it; odd-id movies match nothing --
        // a mix, so the test also confirms correctness survives running out of order/concurrently.
        // The library items carry no TMDb id, so every movie goes through the IMDb lookup.
        var libraryItemsByTmdbId = movies
            .Where(m => m.Id % 2 == 0)
            .ToDictionary(m => m.Id, m => MovieWith(MetadataProvider.Imdb, "tt" + m.Id));

        var apiClientMock = new Mock<ITMDbApiClient>();
        apiClientMock.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)movies);
        apiClientMock
            .Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (int tmdbId, CancellationToken ct) =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref maxInFlight, now);
                await Task.Delay(perCallDelay, ct).ConfigureAwait(false);
                Interlocked.Decrement(ref inFlight);
                return libraryItemsByTmdbId.ContainsKey(tmdbId) ? "tt" + tmdbId : null;
            });

        var service = BuildService(apiClientMock.Object, LibraryWith([.. libraryItemsByTmdbId.Values]));

        await service.RefreshTrendingMoviesAsync(CancellationToken.None);

        var result = service.GetTrendingMovies();
        Assert.Equal(ItemCount, result.Count);
        foreach (var movie in result)
        {
            var expectedMatched = libraryItemsByTmdbId.ContainsKey(movie.Id);
            Assert.Equal(expectedMatched, movie.LibraryItemId.HasValue);
        }

        // Counting overlapping calls proves the items run concurrently, within the bound of
        // LibraryCrossReferencer.MaxConcurrency (5), without depending on how fast the runner is (an
        // elapsed-time threshold failed on CI under coverage instrumentation: 0.83 s for 0.75 s).
        Assert.InRange(maxInFlight, 2, 5);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
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
        Assert.Equal(1, itemsA[0].Id);
        Assert.Single(itemsB);
        Assert.Equal(2, itemsB[0].Id);
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
