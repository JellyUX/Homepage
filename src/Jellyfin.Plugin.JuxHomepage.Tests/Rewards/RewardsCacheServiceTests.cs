using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Rewards;
using Jellyfin.Plugin.JuxHomepage.Rewards.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Rewards;

public sealed class RewardsCacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-rewards-tests-" + Guid.NewGuid());

    private RewardsCacheService BuildService(IWikidataApiClient apiClient, ILibraryManager libraryManager, WidgetConfig[]? widgets = null)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new RewardsCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            apiClient,
            libraryManager,
            () => new PluginConfiguration { Widgets = widgets ?? [] },
            NullLogger<RewardsCacheService>.Instance);
    }

    private string CacheFilePath(string instanceId) =>
        Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage", "cache", "rewards", $"rewards_{Guid.Parse(instanceId):N}.json");

    // -------------------------------------------------------------------------
    // RefreshInstanceAsync: IMDb-based cross-referencing (no second HTTP round-trip needed, unlike
    // TMDb -- the IMDb id is already known directly from the Wikidata SPARQL response)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshInstanceAsync_ImdbMatchFound_WritesCacheAndSetsLibraryItemId()
    {
        var instanceId = Guid.NewGuid().ToString();
        var libraryItem = new Movie { Id = Guid.NewGuid(), Name = "Oppenheimer" };

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Imdb"))))
            .Returns([libraryItem.Id]);

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[
                new RewardsWinner { Id = 117085614, FilmQid = "Q117085614", FilmLabel = "Oppenheimer", ImdbId = "tt15398776" }
            ]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);
        var filter = new RewardsFilter { CategoryQid = "Q102427" };

        await service.RefreshInstanceAsync(instanceId, filter, CancellationToken.None);

        Assert.True(File.Exists(CacheFilePath(instanceId)));
        var cached = service.GetRewards(instanceId);
        Assert.Single(cached);
        Assert.Equal(libraryItem.Id, cached[0].LibraryItemId);
    }

    // -------------------------------------------------------------------------
    // No spurious fallback match: a RewardsWinner's Id is a parsed Wikidata Q-id number, unrelated to
    // any real TMDb id -- LibraryCrossReferencer must be called with fallbackProvider: null so a
    // missed IMDb match never falls back to comparing against MetadataProvider.Tmdb.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshInstanceAsync_NoImdbMatch_DoesNotFallBackToTmdbProviderId()
    {
        var instanceId = Guid.NewGuid().ToString();

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .Setup(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[
                new RewardsWinner { Id = 550, FilmQid = "Q550", FilmLabel = "Some Film", ImdbId = "tt0000000" }
            ]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);

        await service.RefreshInstanceAsync(instanceId, new RewardsFilter { CategoryQid = "Q102427" }, CancellationToken.None);

        // GetItemIds must never have been queried with a Tmdb provider id lookup for this item.
        libraryManagerMock.Verify(
            m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Tmdb"))),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // WriteUnlessEmpty protection (mirrors TMDbCacheServiceTests.WriteCacheUnlessEmpty)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshInstanceAsync_EmptyResult_DoesNotOverwriteExistingCache()
    {
        var instanceId = Guid.NewGuid().ToString();
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasAnyProviderId != null)))
            .Returns([Guid.NewGuid()]);

        var apiClientMock = new Mock<IWikidataApiClient>();
        apiClientMock
            .SetupSequence(c => c.GetAwardWinnersAsync(It.IsAny<RewardsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[
                new RewardsWinner { Id = 1, FilmQid = "Q1", FilmLabel = "First", ImdbId = "tt0000001" }
            ])
            .ReturnsAsync((IReadOnlyList<RewardsWinner>)[]);

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object);
        var filter = new RewardsFilter { CategoryQid = "Q102427" };

        await service.RefreshInstanceAsync(instanceId, filter, CancellationToken.None);
        Assert.Single(service.GetRewards(instanceId));

        await service.RefreshInstanceAsync(instanceId, filter, CancellationToken.None);
        Assert.Single(service.GetRewards(instanceId));
    }

    // -------------------------------------------------------------------------
    // RefreshAllInstancesAsync: filter reconstruction from ExtraParams
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAllInstancesAsync_ConfiguredInstances_ReconstructsFilterFromExtraParams()
    {
        var instanceId = Guid.NewGuid().ToString();
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);

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

        var service = BuildService(apiClientMock.Object, libraryManagerMock.Object, widgets);

        await service.RefreshAllInstancesAsync(CancellationToken.None);

        Assert.True(File.Exists(CacheFilePath(instanceId)));
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
