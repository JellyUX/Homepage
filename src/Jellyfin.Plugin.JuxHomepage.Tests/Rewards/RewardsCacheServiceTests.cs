using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Rewards;
using Jellyfin.Plugin.JuxHomepage.Rewards.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Rewards;

public sealed class RewardsCacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-rewards-tests-" + Guid.NewGuid());

    private RewardsCacheService BuildService(IWikidataApiClient apiClient, ILibraryManager libraryManager, WidgetConfig[]? widgets = null, SectionResultCache? sectionResults = null)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new RewardsCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            apiClient,
            libraryManager,
            () => new PluginConfiguration { Widgets = widgets ?? [] },
            NullLogger<RewardsCacheService>.Instance,
            sectionResults);
    }

    private string CacheFilePath(string instanceId) =>
        Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage", "cache", "rewards", $"rewards_{Guid.Parse(instanceId):N}.json");

    // One configured Rewards widget row, the only way RefreshAllInstancesAsync reaches an instance.
    private static WidgetConfig[] RowFor(string instanceId) =>
    [
        new WidgetConfig
        {
            WidgetType = RewardsWidgetTypes.Rewards,
            ExtraParams =
            [
                new WidgetExtraParam { Key = "value", Value = instanceId },
                new WidgetExtraParam { Key = "categoryQid", Value = "Q102427" }
            ]
        }
    ];

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
    // RefreshInstanceAsync: IMDb-based cross-referencing (no second HTTP round-trip needed, unlike
    // TMDb -- the IMDb id is already known directly from the Wikidata SPARQL response)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshInstanceAsync_ImdbMatchFound_WritesCacheAndSetsLibraryItemId()
    {
        var instanceId = Guid.NewGuid().ToString();
        var libraryItem = MovieWith(MetadataProvider.Imdb, "tt15398776");

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[
                new RewardsWinner { Id = 117085614, FilmQid = "Q117085614", FilmLabel = "Oppenheimer", ImdbId = "tt15398776" }
            ]);

        var service = BuildService(apiClientMock.Object, LibraryWith(libraryItem), RowFor(instanceId));

        await service.RefreshAllInstancesAsync(CancellationToken.None);

        Assert.True(File.Exists(CacheFilePath(instanceId)));
        var cached = service.GetRewards(instanceId);
        Assert.Single(cached);
        Assert.Equal(libraryItem.Id, cached[0].LibraryItemId);

        // Annexe.b, DEC-P07-11: only what is read back is written.
        using var file = System.Text.Json.JsonDocument.Parse(File.ReadAllText(CacheFilePath(instanceId)));
        Assert.Equal(["Id", "LibraryItemId", "ImdbId"], file.RootElement.GetProperty("Items")[0].EnumerateObject().Select(p => p.Name));
    }

    // -------------------------------------------------------------------------
    // No spurious fallback match: a RewardsWinner's Id is a parsed Wikidata Q-id number, unrelated to
    // any real TMDb id -- LibraryCrossReferencer must be called with fallbackProvider: null so a
    // missed IMDb match never falls back to comparing against MetadataProvider.Tmdb. The library
    // holds a movie whose TMDb id equals the winner's Id: it must stay unmatched.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshInstanceAsync_NoImdbMatch_DoesNotFallBackToTmdbProviderId()
    {
        var instanceId = Guid.NewGuid().ToString();
        var libraryManager = LibraryWith(MovieWith(MetadataProvider.Tmdb, "550"));

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[
                new RewardsWinner { Id = 550, FilmQid = "Q550", FilmLabel = "Some Film", ImdbId = "tt0000000" }
            ]);

        var service = BuildService(apiClientMock.Object, libraryManager, RowFor(instanceId));

        await service.RefreshAllInstancesAsync(CancellationToken.None);

        var cached = service.GetRewards(instanceId);
        Assert.Single(cached);
        Assert.Null(cached[0].LibraryItemId);
    }

    // -------------------------------------------------------------------------
    // WriteUnlessEmpty protection (mirrors TMDbCacheServiceTests.WriteCacheUnlessEmpty)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshInstanceAsync_EmptyResult_DoesNotOverwriteExistingCache()
    {
        var instanceId = Guid.NewGuid().ToString();
        var libraryManager = LibraryWith(MovieWith(MetadataProvider.Imdb, "tt0000001"));

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .SetupSequence(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[
                new RewardsWinner { Id = 1, FilmQid = "Q1", FilmLabel = "First", ImdbId = "tt0000001" }
            ])
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[]);

        var service = BuildService(apiClientMock.Object, libraryManager, RowFor(instanceId));

        await service.RefreshAllInstancesAsync(CancellationToken.None);
        Assert.Single(service.GetRewards(instanceId));

        await service.RefreshAllInstancesAsync(CancellationToken.None);
        Assert.Single(service.GetRewards(instanceId));
    }

    // -------------------------------------------------------------------------
    // RefreshAllInstancesAsync: filter reconstruction from ExtraParams
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllInstancesAsync_ConfiguredInstances_ReconstructsFilterFromExtraParams()
    {
        var instanceId = Guid.NewGuid().ToString();

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(
                It.Is<RewardsFilter>(f => f.CeremonyQid == "Q19020" && f.Year == 2024),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[
                new RewardsWinner { Id = 1, FilmQid = "Q1", FilmLabel = "Matched", ImdbId = "tt0000001" }
            ]);

        var widgets = new[]
        {
            new WidgetConfig
            {
                WidgetType = RewardsWidgetTypes.Rewards,
                ExtraParams =
                [
                    new WidgetExtraParam { Key = "value", Value = instanceId },
                    new WidgetExtraParam { Key = "ceremonyQid", Value = "Q19020" },
                    new WidgetExtraParam { Key = "year", Value = "2024" }
                ]
            }
        };

        var service = BuildService(apiClientMock.Object, LibraryWith(), widgets);

        await service.RefreshAllInstancesAsync(CancellationToken.None);

        Assert.True(File.Exists(CacheFilePath(instanceId)));
    }

    [Fact]
    public async Task RefreshAllInstancesAsync_TwoRows_ReadTheMovieLibraryOnce()
    {
        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[new RewardsWinner { Id = 1, FilmQid = "Q1", FilmLabel = "Film", ImdbId = "tt0000001" }]);
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([MovieWith(MetadataProvider.Imdb, "tt0000001")]);
        var widgets = Enumerable.Range(0, 2).Select(_ => new WidgetConfig
        {
            WidgetType = RewardsWidgetTypes.Rewards,
            ExtraParams =
            [
                new WidgetExtraParam { Key = "value", Value = Guid.NewGuid().ToString() },
                new WidgetExtraParam { Key = "categoryQid", Value = "Q102427" }
            ]
        }).ToArray();

        await BuildService(apiClientMock.Object, library.Object, widgets).RefreshAllInstancesAsync(CancellationToken.None);

        library.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    [Fact]
    public async Task RefreshInstanceAsync_RewrittenList_DropsTheCachedHomeSectionPages()
    {
        // R11 review (DEC-P07-15): a section page cached before the refresh would keep the old list.
        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[new RewardsWinner { Id = 1, FilmQid = "Q1", FilmLabel = "Film", ImdbId = "tt0000001" }]);
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([]);
        using var sessionCache = new SessionCache();
        using var sections = new SectionResultCache(sessionCache);
        var user = Guid.NewGuid();
        var instanceId = Guid.NewGuid().ToString();
        var section = new SectionId(user, RewardsWidgetTypes.Rewards, instanceId, null);
        Assert.True(sections.Set(section, 0, 20, new WidgetResult([], 0), sections.StampOf(user), TimeSpan.FromMinutes(15)));

        await BuildService(apiClientMock.Object, library.Object, RowFor(instanceId), sections)
            .RefreshAllInstancesAsync(CancellationToken.None);

        Assert.False(sections.TryGet(section, 0, 20, out _));
    }

    // -------------------------------------------------------------------------
    // RefreshAllInstancesAsync: concurrency guard (Phase 1.1 of TODO_V3.md)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllInstancesAsync_ConcurrentCalls_OnlyOneActuallyRuns()
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var callCount = 0;

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref callCount);
                started.Set();
                await Task.Run(() => release.Wait(TimeSpan.FromSeconds(5)));
                return (IReadOnlyList<RewardsWinner>)[];
            });

        var widgets = new[]
        {
            new WidgetConfig
            {
                WidgetType = RewardsWidgetTypes.Rewards,
                ExtraParams =
                [
                    new WidgetExtraParam { Key = "value", Value = Guid.NewGuid().ToString() },
                    new WidgetExtraParam { Key = "categoryQid", Value = "Q102427" }
                ]
            }
        };

        var service = BuildService(apiClientMock.Object, new Mock<ILibraryManager>().Object, widgets);

        var firstCallTask = service.RefreshAllInstancesAsync(CancellationToken.None);

        var reachedFetch = started.Wait(TimeSpan.FromSeconds(5));
        Assert.True(reachedFetch, "First refresh did not reach the fetch step in time.");

        // A second concurrent attempt must not be able to reserve the slot while the first is still
        // in progress -- this must be true regardless of the caller (a second manual click on the
        // combined TMDb+Rewards "Refresh now" button, or the weekly scheduled task firing at the same
        // moment).
        var secondAcquired = service.TryAcquireRefreshLock();
        Assert.False(secondAcquired, "A second concurrent refresh should not acquire the lock.");

        release.Set();
        await firstCallTask;

        Assert.Equal(1, callCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
