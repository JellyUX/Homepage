using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

// TODO_V2.md Phase 15.1: empirically measure the concurrency behavior of SessionCache (via
// WidgetService, its real production consumer) and ScoringService under ~100 concurrent requests --
// previously assumed by the design but never actually measured. Both services share the same
// "check cache, else compute and store" shape, which is NOT protected by a lock/single-flight: on a
// cold cache, concurrent callers can all miss and all recompute. The warm-cache tests assert the
// expected, load-bearing guarantee (a request storm against an already-populated cache must not
// recompute); the cold-cache tests only assert correctness (no exceptions, consistent results for
// every caller) and report the observed recomputation count, rather than assuming a stronger
// guarantee the current code does not actually provide.
public sealed class LoadTests
{
    private const int ConcurrentCallers = 100;

    // -------------------------------------------------------------------------
    // WidgetService / SessionCache
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WidgetService_ConcurrentRequestsAfterWarmCache_ComputesLayoutOnce()
    {
        var callCount = 0;
        var widget = MakeCountingWidget("jux.native.continue-watching", () => Interlocked.Increment(ref callCount));
        var service = BuildWidgetService(widget);
        var userId = Guid.NewGuid();

        // Warm-up: first call populates SessionCache.
        await service.GetWidgetsForUser(userId, page: 0, lang: "en", CancellationToken.None);
        Assert.Equal(1, callCount);

        var tasks = Enumerable.Range(0, ConcurrentCallers)
            .Select(_ => service.GetWidgetsForUser(userId, page: 0, lang: "en", CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Single(r.Sections));
        Assert.Equal(1, callCount); // still 1: every concurrent call hit the warm cache, none recomputed.
    }

    [Fact]
    public async Task WidgetService_ConcurrentRequestsOnColdCache_ReturnsConsistentResultsWithoutThrowing()
    {
        var callCount = 0;
        var widget = MakeCountingWidget("jux.native.continue-watching", () => Interlocked.Increment(ref callCount));
        var service = BuildWidgetService(widget);
        var userId = Guid.NewGuid();

        var tasks = Enumerable.Range(0, ConcurrentCallers)
            .Select(_ => service.GetWidgetsForUser(userId, page: 0, lang: "en", CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        // Every caller must get a correct, single-descriptor result -- no exceptions, no partial or
        // corrupted layouts -- regardless of how many times the layout was actually recomputed.
        Assert.All(results, r => Assert.Single(r.Sections));

        // Not asserted as exactly 1: WidgetService.GetWidgetsForUser has no lock around its
        // check-cache/compute/store sequence, so a cold-cache stampede can recompute more than
        // once. This is the empirical measurement TODO_V2.md Phase 15.1 asked for -- reported here,
        // not silently assumed. The stampede is tracked as PERF-01 (a single rebuild on expiry); it
        // is not itself a defect this test fixes.
        Assert.True(callCount >= 1 && callCount <= ConcurrentCallers, $"Observed {callCount} recomputations for {ConcurrentCallers} concurrent cold-cache callers.");
    }

    // -------------------------------------------------------------------------
    // ScoringService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ScoringService_ConcurrentRequestsAfterWarmCache_ComputesSnapshotOnce()
    {
        var callCount = 0;
        var user = new User("test", "Default", "Default");
        var service = BuildScoringService(user, () => Interlocked.Increment(ref callCount));

        // Warm-up: first call populates ScoringService's internal snapshot cache.
        service.GetTopGenres(user.Id, 5);
        Assert.Equal(1, callCount);

        var tasks = Enumerable.Range(0, ConcurrentCallers)
            .Select(_ => Task.Run(() => service.GetTopGenres(user.Id, 5)));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("Action", r[0].Value));
        Assert.Equal(1, callCount); // still 1: every concurrent call hit the warm snapshot cache.
    }

    [Fact]
    public async Task ScoringService_ConcurrentRequestsOnColdCache_ReturnsConsistentResultsWithoutThrowing()
    {
        var callCount = 0;
        var user = new User("test", "Default", "Default");
        var service = BuildScoringService(user, () => Interlocked.Increment(ref callCount));

        var tasks = Enumerable.Range(0, ConcurrentCallers)
            .Select(_ => Task.Run(() => service.GetTopGenres(user.Id, 5)));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("Action", r[0].Value));

        // Single-flight (F-03): every cold caller joins the one running computation.
        Assert.Equal(1, callCount);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static WidgetService BuildWidgetService(IWidget widget)
    {
        var registry = new WidgetRegistry();
        registry.Register(widget);

        var userConfigStoreMock = new Mock<IUserConfigurationStore>();
        userConfigStoreMock.Setup(s => s.GetUserConfiguration(It.IsAny<Guid>())).Returns((UserConfiguration?)null);

        var config = new PluginConfiguration
        {
            Widgets = [new WidgetConfig { WidgetType = widget.WidgetType, Enabled = true, MinItems = 0, Order = 0 }],
            Cache = new CacheConfig { SessionTtlMinutes = 15 }
        };

        var layoutResolver = new WidgetLayoutResolver(
            registry,
            userConfigStoreMock.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => config,
            NullLogger<WidgetLayoutResolver>.Instance);

        return new WidgetService(
            registry,
            new SessionCache(),
            layoutResolver,
            () => config,
            NullLogger<WidgetService>.Instance);
    }

    private static IWidget MakeCountingWidget(string widgetType, Action onFetch)
    {
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.DefaultDisplayName).Returns(widgetType);

        mock.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>()))
            .Returns(() => mock.Object);

        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                onFetch();
                return new WidgetResult([], 10);
            });

        mock.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = widgetType });

        return mock.Object;
    }

    private static ScoringService BuildScoringService(User user, Action onCompute)
    {
        var action1 = new Movie { Name = "Action 1", Genres = ["Action"] };
        var action2 = new Movie { Name = "Action 2", Genres = ["Action"] };
        var drama1 = new Movie { Name = "Drama 1", Genres = ["Drama"] };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns(() =>
            {
                onCompute();
                return [action1, action2, drama1];
            });
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Series }))))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Episode }))))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IsFavorite == true)))
            .Returns([]);
        libraryManagerMock.Setup(m => m.GetPeople(It.IsAny<BaseItem>())).Returns([]);

        return new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration { Cache = new CacheConfig { SessionTtlMinutes = 15 } });
    }
}
