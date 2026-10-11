using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Native;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
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
            new Mock<IDtoService>().Object);

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
            new Mock<IDtoService>().Object);

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
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.native.recently-added-episodes", d.WidgetType);
        Assert.Equal(WidgetCategory.Native, d.Category);
        Assert.Equal(WidgetViewMode.Landscape, d.ViewMode);
        Assert.Equal(4, d.MinItems);
        Assert.Null(d.Route);
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
        Assert.Null(d.Route);
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
            new Mock<IDtoService>().Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
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
            .Setup(m => m.GetItems(It.IsAny<Guid>(), null, null, null, It.IsAny<int>(), It.IsAny<int>(), false, It.IsAny<CancellationToken>()))
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
            m => m.GetItems(userId, null, null, null, 0, 20, false, It.IsAny<CancellationToken>()),
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
