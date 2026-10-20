using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Widgets.Native;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Native;

public sealed class NativeWidgetTests
{
    // -------------------------------------------------------------------------
    // Descriptor tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ContinueWatching_GetDescriptor_HasExpectedProperties()
    {
        var widget = new ContinueWatchingWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.continue-watching", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Landscape, d.ViewMode);
        Assert.Equal(1, d.MinItems);
        Assert.Null(d.Route);
    }

    [Fact]
    public void NextUp_GetDescriptor_HasExpectedProperties()
    {
        var widget = new NextUpWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<ITVSeriesManager>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.next-up", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Landscape, d.ViewMode);
        Assert.Equal(1, d.MinItems);
        Assert.Equal("nextup", d.Route);
    }

    [Fact]
    public void RecentlyAddedMovies_GetDescriptor_HasExpectedProperties()
    {
        var widget = new RecentlyAddedMoviesWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<IUserViewManager>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.recently-added-movies", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Equal(4, d.MinItems);
        Assert.Equal("movies", d.Route);
    }

    [Fact]
    public void RecentlyAddedShows_GetDescriptor_HasExpectedProperties()
    {
        var widget = new RecentlyAddedShowsWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<IUserViewManager>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.recently-added-shows", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Equal(4, d.MinItems);
        Assert.Equal("tvshows", d.Route);
    }

    [Fact]
    public void RecentlyAddedEpisodes_GetDescriptor_HasExpectedProperties()
    {
        var widget = new RecentlyAddedEpisodesWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<IUserViewManager>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.recently-added-episodes", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Landscape, d.ViewMode);
        Assert.Equal(4, d.MinItems);
        Assert.Equal("tvshows", d.Route);
    }

    // PERF-03.c: the server probe and the client's native read take LimitFactor and MaxPerSeries from
    // one place, the widget, through its descriptor.
    [Theory]
    [InlineData("jux.native.recently-added-movies", 1, null)]
    [InlineData("jux.native.recently-added-shows", 3, null)]
    [InlineData("jux.native.recently-added-episodes", 10, 2)]
    [InlineData("jux.native.continue-watching", 1, null)]
    public void NativeParameters_AreCarriedByTheDescriptor(string widgetType, int limitFactor, int? maxPerSeries)
    {
        IWidget[] widgets =
        [
            new RecentlyAddedMoviesWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IUserViewManager>()),
            new RecentlyAddedShowsWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IUserViewManager>()),
            new RecentlyAddedEpisodesWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IUserViewManager>()),
            new ContinueWatchingWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>())
        ];

        var d = widgets.Single(w => w.WidgetType == widgetType).GetDescriptor();

        Assert.Equal(limitFactor, d.LimitFactor);
        Assert.Equal(maxPerSeries, d.MaxPerSeries);
    }

    [Fact]
    public void NativeParameters_AreReadByTheClientFromTheDescriptor()
    {
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web", "jux-homepage.js"));

        Assert.Contains("descriptor." + nameof(WidgetDescriptor.LimitFactor), script, StringComparison.Ordinal);
        Assert.Contains("descriptor." + nameof(WidgetDescriptor.MaxPerSeries), script, StringComparison.Ordinal);
        Assert.DoesNotContain("limitFactor:", script, StringComparison.Ordinal);
        Assert.DoesNotContain("perSeries:", script, StringComparison.Ordinal);
    }

    // PERF-03.b: the probe keeps a section's page only when the client reads it from the Section
    // endpoint, so the widgets marked ReadNatively must be exactly the client's native routes.
    [Fact]
    public void ReadNatively_MatchesTheClientsNativeRoutes()
    {
        IWidget[] widgets =
        [
            new ContinueWatchingWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>()),
            new NextUpWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<ITVSeriesManager>()),
            new RecentlyAddedMoviesWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IUserViewManager>()),
            new RecentlyAddedShowsWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IUserViewManager>()),
            new RecentlyAddedEpisodesWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IUserViewManager>()),
            new MyMediaWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IUserViewManager>()),
            new WatchlistWidget(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(), Mock.Of<IWatchlistService>())
        ];
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web", "jux-homepage.js"));
        var start = script.IndexOf("var NATIVE_ROUTES = {", StringComparison.Ordinal);
        var routes = script[start..script.IndexOf("};", start, StringComparison.Ordinal)];

        var nativeRoutes = System.Text.RegularExpressions.Regex.Matches(routes, @"'(jux\.[a-z.-]+)':")
            .Select(m => m.Groups[1].Value)
            .Order(StringComparer.Ordinal);
        var readNatively = widgets.Where(w => w.GetDescriptor().ReadNatively).Select(w => w.WidgetType).Order(StringComparer.Ordinal);

        Assert.Equal(nativeRoutes, readNatively);
    }

    [Fact]
    public void MyMedia_GetDescriptor_HasExpectedProperties()
    {
        var widget = new MyMediaWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<IUserViewManager>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.my-media", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Landscape, d.ViewMode);
        Assert.Equal(1, d.MinItems);
        Assert.Null(d.Route);
    }

    [Fact]
    public void Watchlist_GetDescriptor_HasExpectedProperties()
    {
        var widget = new WatchlistWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<IWatchlistService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.watchlist", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Equal(1, d.MinItems);
        Assert.Equal("watchlist", d.Route);
    }

    // -------------------------------------------------------------------------
    // Resolve test
    // -------------------------------------------------------------------------

    [Fact]
    public void ContinueWatching_Resolve_ReturnsSameInstance()
    {
        var widget = new ContinueWatchingWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var resolved = widget.Resolve(Guid.NewGuid(), new WidgetInstanceConfig(), 1);

        Assert.Same(widget, resolved);
    }

    // -------------------------------------------------------------------------
    // User-null defensive tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ContinueWatching_UserNotFound_ReturnsEmpty()
    {
        var widget = new ContinueWatchingWidget(
            TestMocks.UserManagerReturningNull().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task NextUp_UserNotFound_ReturnsEmpty()
    {
        var widget = new NextUpWidget(
            TestMocks.UserManagerReturningNull().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<ITVSeriesManager>().Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    // RecentlyAddedMoviesWidget, RecentlyAddedShowsWidget, and RecentlyAddedEpisodesWidget all inherit
    // GetItemsAsync unmodified from RecentlyAddedWidgetBase (none of them override it), so this also
    // covers RecentlyAddedShowsWidget's and RecentlyAddedEpisodesWidget's null-user check. Do not add a
    // duplicate test for either -- it would exercise the exact same code path (see Phase 2 test triage,
    // TODO_V2.md).
    [Fact]
    public async Task RecentlyAddedMovies_UserNotFound_ReturnsEmpty()
    {
        var widget = new RecentlyAddedMoviesWidget(
            TestMocks.UserManagerReturningNull().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<IUserViewManager>().Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecentlyAddedShows_ReadsTheNativeLatestMediaGroupedBySeries(bool hidePlayed)
    {
        var user = TestMocks.DefaultUser();
        user.HidePlayedInLatest = hidePlayed;
        var series = new MediaBrowser.Controller.Entities.TV.Series { Id = Guid.NewGuid(), Name = "Show" };
        var lone = new MediaBrowser.Controller.Entities.TV.Episode { Id = Guid.NewGuid(), Name = "Lone episode" };
        LatestItemsQuery? seen = null;
        var views = new Mock<IUserViewManager>();
        views.Setup(v => v.GetLatestItems(It.IsAny<LatestItemsQuery>(), It.IsAny<DtoOptions>()))
            .Callback<LatestItemsQuery, DtoOptions>((q, _) => seen = q)
            .Returns(
            [
                new Tuple<BaseItem, List<BaseItem>>(series, [new MediaBrowser.Controller.Entities.TV.Episode(), new MediaBrowser.Controller.Entities.TV.Episode()]),
                new Tuple<BaseItem, List<BaseItem>>(series, [lone])
            ]);
        var dtos = new Mock<IDtoService>();
        dtos.Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => items.Select(i => new BaseItemDto { Id = i.Id }).ToList());
        var widget = new RecentlyAddedShowsWidget(TestMocks.UserManagerReturning(user).Object, new Mock<ILibraryManager>().Object, dtos.Object, views.Object);

        var result = await widget.GetItemsAsync(new WidgetPayload { UserId = user.Id, Limit = 4 }, CancellationToken.None);

        Assert.Equal([series.Id, lone.Id], result.Items.Select(d => d.Id));
        Assert.Equal((BaseItemKind.Episode, true, 12), (seen!.IncludeItemTypes.Single(), seen.GroupItems, seen.Limit));
        Assert.Equal(hidePlayed ? false : null, seen.IsPlayed);
        Assert.Same(user, seen.User);
    }

    [Fact]
    public async Task RecentlyAddedEpisodes_ShowsAtMostTwoEpisodesOfASeries_AndReadsTenTimesMore()
    {
        // TODO_V4.md 9.3 (F-65): a season added at once filled the row (15 + 5 of two series).
        var user = TestMocks.DefaultUser();
        var bulkSeries = Guid.NewGuid();
        var bulk = Enumerable.Range(0, 10).Select(_ => new MediaBrowser.Controller.Entities.TV.Episode { Id = Guid.NewGuid(), SeriesId = bulkSeries }).ToList();
        var others = Enumerable.Range(0, 5).Select(_ => new MediaBrowser.Controller.Entities.TV.Episode { Id = Guid.NewGuid(), SeriesId = Guid.NewGuid() }).ToList();
        LatestItemsQuery? seen = null;
        var views = new Mock<IUserViewManager>();
        views.Setup(v => v.GetLatestItems(It.IsAny<LatestItemsQuery>(), It.IsAny<DtoOptions>()))
            .Callback<LatestItemsQuery, DtoOptions>((q, _) => seen = q)
            .Returns(bulk.Concat(others).Select(e => new Tuple<BaseItem, List<BaseItem>>(null!, [e])).ToList());
        var dtos = new Mock<IDtoService>();
        dtos.Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => items.Select(i => new BaseItemDto { Id = i.Id }).ToList());
        var widget = new RecentlyAddedEpisodesWidget(TestMocks.UserManagerReturning(user).Object, new Mock<ILibraryManager>().Object, dtos.Object, views.Object);

        var result = await widget.GetItemsAsync(new WidgetPayload { UserId = user.Id, Limit = 6 }, CancellationToken.None);

        Assert.Equal(bulk.Take(2).Concat(others.Take(4)).Select(e => e.Id), result.Items.Select(d => d.Id));
        Assert.Equal((false, 60), (seen!.GroupItems, seen.Limit));
    }

    [Fact]
    public async Task MyMedia_UserNotFound_ReturnsEmpty()
    {
        var widget = new MyMediaWidget(
            TestMocks.UserManagerReturningNull().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new Mock<IUserViewManager>().Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Watchlist_GetItemsAsync_DelegatesToWatchlistService()
    {
        var watchlistServiceMock = new Mock<IWatchlistService>();
        watchlistServiceMock
            .Setup(m => m.GetItems(It.IsAny<Guid>(), null, null, null, null, It.IsAny<int>(), It.IsAny<int>(), false, It.IsAny<CancellationToken>()))
            .Returns(new Widgets.WidgetResult([], 3));

        var widget = new WatchlistWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            watchlistServiceMock.Object);

        var userId = Guid.NewGuid();
        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = userId, StartIndex = 0, Limit = 20 },
            CancellationToken.None);

        Assert.Equal(3, result.TotalRecordCount);
        watchlistServiceMock.Verify(
            m => m.GetItems(userId, null, null, null, null, 0, 20, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // NativeWidgetDefaults tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NativeWidgetDefaults_Build_ReturnsSevenEntries()
    {
        var configs = NativeWidgetDefaults.Build();
        Assert.Equal(7, configs.Length);
    }

    [Fact]
    public void NativeWidgetDefaults_Build_AllEnabled()
    {
        var configs = NativeWidgetDefaults.Build();
        Assert.All(configs, c => Assert.True(c.Enabled));
    }

    [Fact]
    public void NativeWidgetDefaults_Build_OrdersAreAscendingAndUnique()
    {
        var orders = NativeWidgetDefaults.Build().Select(c => c.Order).ToArray();
        Assert.Equal(orders.Length, orders.Distinct().Count());
        Assert.Equal(orders, orders.OrderBy(o => o).ToArray());
    }

    [Fact]
    public void NativeWidgetDefaults_Build_MinItemsMatchWidgetDefaults()
    {
        var configs = NativeWidgetDefaults.Build();

        Assert.Equal(1, configs.Single(c => c.WidgetType == "jux.native.continue-watching").MinItems);
        Assert.Equal(1, configs.Single(c => c.WidgetType == "jux.native.next-up").MinItems);
        Assert.Equal(4, configs.Single(c => c.WidgetType == "jux.native.recently-added-movies").MinItems);
        Assert.Equal(4, configs.Single(c => c.WidgetType == "jux.native.recently-added-shows").MinItems);
        Assert.Equal(1, configs.Single(c => c.WidgetType == "jux.native.my-media").MinItems);
        Assert.Equal(1, configs.Single(c => c.WidgetType == "jux.native.watchlist").MinItems);
    }
}
