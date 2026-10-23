using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Connected;

/// <summary>
/// Tests the generic mechanics shared by every <see cref="Widgets.Connected.ConnectedWidgetBase{T}"/>
/// subclass (descriptor shape, pagination, filtering of unmatched/deleted library items, unknown
/// user) -- not Trending-specific behavior. <see cref="TrendingMoviesWidget"/> is used only as a
/// concrete stand-in to exercise the shared base class; per-widget cache wiring is covered in
/// <c>ConnectedWidgetsTests.cs</c> instead.
/// </summary>
public sealed class ConnectedWidgetBaseTests
{
    private static TrendingMoviesWidget BuildWidget(
        IReadOnlyList<TMDbMovie> cachedMovies,
        Mock<IUserManager>? userManagerMock = null,
        Mock<ILibraryManager>? libraryManagerMock = null,
        Mock<IDtoService>? dtoServiceMock = null,
        ILogger<TrendingMoviesWidget>? logger = null)
    {
        var cacheServiceMock = new Mock<ITMDbCacheService>();
        cacheServiceMock.Setup(c => c.GetTrendingMovies()).Returns(cachedMovies);

        userManagerMock ??= new Mock<IUserManager>();
        libraryManagerMock ??= new Mock<ILibraryManager>();

        // Only apply the default "return empty" setup when the caller didn't supply their own
        // mock -- re-applying it here would shadow a caller-configured Callback (Moq resolves
        // multiple matching setups by "last configured wins").
        dtoServiceMock ??= TestMocks.DtoServiceReturningEmpty();

        return new TrendingMoviesWidget(
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            cacheServiceMock.Object,
            logger ?? NullLogger<TrendingMoviesWidget>.Instance);
    }

    // -------------------------------------------------------------------------
    // Descriptor
    // -------------------------------------------------------------------------

    [Fact]
    public void GetDescriptor_HasExpectedProperties()
    {
        var widget = BuildWidget([]);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.connected.trending-movies", d.WidgetType);
        Assert.Equal(WidgetCategory.Connected, d.Category);
        Assert.Equal(4, d.MinItems);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Null(d.Route);
    }

    [Fact]
    public void Resolve_ReturnsSameInstance()
    {
        var widget = BuildWidget([]);

        var resolved = widget.Resolve(Guid.NewGuid(), new WidgetInstanceConfig(), 1);

        Assert.Same(widget, resolved);
    }

    // -------------------------------------------------------------------------
    // GetItemsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetItemsAsync_EmptyCache_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(TestMocks.DefaultUser());

        var widget = BuildWidget([], userManagerMock);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), Limit = 20 },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetItemsAsync_UnknownUser_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var widget = BuildWidget(
            [new TMDbMovie { Id = 1, Title = "A", LibraryItemId = Guid.NewGuid() }],
            userManagerMock);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), Limit = 20 },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public async Task GetItemsAsync_UnmatchedItemsAreExcluded_TotalRecordCountReflectsOnlyMatched()
    {
        var user = TestMocks.DefaultUser();
        var matchedId1 = Guid.NewGuid();
        var matchedId2 = Guid.NewGuid();
        var libraryItem1 = new TestMovie { Id = matchedId1, Name = "Owned 1" };
        var libraryItem2 = new TestMovie { Id = matchedId2, Name = "Owned 2" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        var libraryItemsById = new Dictionary<Guid, BaseItem> { [matchedId1] = libraryItem1, [matchedId2] = libraryItem2 };
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns<InternalItemsQuery>(q => q.ItemIds.Where(libraryItemsById.ContainsKey).Select(id => libraryItemsById[id]).ToList());

        IReadOnlyList<BaseItem>? capturedItems = null;
        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Callback<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => capturedItems = items)
            .Returns([]);

        var cachedMovies = new List<TMDbMovie>
        {
            new() { Id = 1, Title = "Not owned", LibraryItemId = null },
            new() { Id = 2, Title = "Owned 1", LibraryItemId = matchedId1 },
            new() { Id = 3, Title = "Also not owned", LibraryItemId = null },
            new() { Id = 4, Title = "Owned 2", LibraryItemId = matchedId2 }
        };

        var widget = BuildWidget(cachedMovies, userManagerMock, libraryManagerMock, dtoServiceMock);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), StartIndex = 0, Limit = 20 },
            CancellationToken.None);
        _ = result.Items; // the page is projected on its first read (PERF-03.a)

        Assert.Equal(2, result.TotalRecordCount);
        Assert.NotNull(capturedItems);
        Assert.Equal(2, capturedItems!.Count);
    }

    // KTW-01.a, KTW-02.a, R11 constat 7: a featured TMDb list keeps its titles rated at least the row's
    // minimum, those with a backdrop first, in the TMDb order inside each group.
    [Fact]
    public async Task GetItemsAsync_FeaturedWithFeaturedMinRating_KeepsRatedTitlesBackdropsFirst()
    {
        var user = TestMocks.DefaultUser();
        TestMovie Owned(string name, float rating, bool backdrop) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            CommunityRating = rating,
            ImageInfos = backdrop ? [new ItemImageInfo { Type = MediaBrowser.Model.Entities.ImageType.Backdrop, Path = "/b.jpg" }] : []
        };
        TestMovie[] owned = [Owned("Bare 8", 8, false), Owned("Low 6", 6, true), Owned("Lit 7.5", 7.5f, true), Owned("Lit 9", 9, true)];
        var byId = owned.ToDictionary(m => m.Id, m => (BaseItem)m);
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns<InternalItemsQuery>(q => q.ItemIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList());
        IReadOnlyList<BaseItem>? capturedItems = null;
        DtoOptions? capturedOptions = null;
        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Callback<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, options, _, _) => (capturedItems, capturedOptions) = (items, options))
            .Returns([]);
        var widget = BuildWidget(
            [.. owned.Select((m, i) => new TMDbMovie { Id = i + 1, Title = m.Name, LibraryItemId = m.Id })],
            userManagerMock, libraryManagerMock, dtoServiceMock);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, Limit = 20, Featured = true, FeaturedMinRating = 7 },
            CancellationToken.None);
        _ = result.Items;

        Assert.Equal(["Lit 7.5", "Lit 9", "Bare 8"], capturedItems!.Select(i => i.Name));
        Assert.Equal(3, result.TotalRecordCount);
        Assert.Contains(MediaBrowser.Model.Querying.ItemFields.Overview, capturedOptions!.Fields);
    }

    [Fact]
    public async Task GetItemsAsync_DeletedLibraryItem_IsSkippedNotAnError()
    {
        var user = TestMocks.DefaultUser();
        var deletedId = Guid.NewGuid();

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns([]);

        IReadOnlyList<BaseItem>? capturedItems = null;
        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Callback<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => capturedItems = items)
            .Returns([]);

        var loggerMock = new Mock<ILogger<TrendingMoviesWidget>>();
        var widget = BuildWidget(
            [new TMDbMovie { Id = 1, Title = "Gone", LibraryItemId = deletedId }],
            userManagerMock,
            libraryManagerMock,
            dtoServiceMock,
            loggerMock.Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), Limit = 20 },
            CancellationToken.None);
        _ = result.Items;

        // The total counts only items that resolve and are visible to the user, so a cached match
        // whose library item was deleted is excluded from both the page and the total.
        Assert.Equal(0, result.TotalRecordCount);
        Assert.NotNull(capturedItems);
        Assert.Empty(capturedItems!);

        // The unresolved cached item (Phase 4.2 of TODO_V2.md) must surface as an aggregated debug
        // log, not silently vanish.
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetItemsAsync_Pagination_RespectsStartIndexAndLimit()
    {
        var user = TestMocks.DefaultUser();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        // GetItemList does not preserve the order of ItemIds passed in (it defaults to sorting
        // alphabetically absent an explicit sort, per Jellyfin's own query builder) -- deliberately
        // return the matched items in REVERSE order here, so this test only passes if
        // ConnectedWidgetBase re-sorts the batched result back into the original ranking order
        // itself, rather than trusting whatever order the query happens to return.
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns<InternalItemsQuery>(q => q.ItemIds
                .Select(id => (BaseItem)new TestMovie { Id = id, Name = id.ToString() })
                .Reverse()
                .ToList());

        IReadOnlyList<BaseItem>? capturedItems = null;
        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Callback<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => capturedItems = items)
            .Returns([]);

        var cachedMovies = ids.Select((id, i) => new TMDbMovie { Id = i, Title = "M" + i, LibraryItemId = id }).ToList();
        var widget = BuildWidget(cachedMovies, userManagerMock, libraryManagerMock, dtoServiceMock);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), StartIndex = 2, Limit = 2 },
            CancellationToken.None);
        _ = result.Items;

        Assert.Equal(5, result.TotalRecordCount);
        Assert.NotNull(capturedItems);
        Assert.Equal(2, capturedItems!.Count);
        Assert.Equal(ids[2], capturedItems[0].Id);
        Assert.Equal(ids[3], capturedItems[1].Id);
    }

    [Fact]
    public async Task GetItemsAsync_ItemOutsideUserRights_ExcludedFromItemsAndTotal()
    {
        var user = TestMocks.DefaultUser();
        var allowed = new TestMovie { Id = Guid.NewGuid(), Name = "Allowed" };
        var hidden = new TestMovie { Id = Guid.NewGuid(), Name = "Other library", VisibleToUser = false };

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([allowed, hidden]);

        IReadOnlyList<BaseItem>? capturedItems = null;
        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Callback<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => capturedItems = items)
            .Returns([]);

        var cachedMovies = new List<TMDbMovie>
        {
            new() { Id = 1, Title = "Other library", LibraryItemId = hidden.Id },
            new() { Id = 2, Title = "Allowed", LibraryItemId = allowed.Id }
        };

        var widget = BuildWidget(cachedMovies, TestMocks.UserManagerReturning(user), libraryManagerMock, dtoServiceMock);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, StartIndex = 0, Limit = 20 },
            CancellationToken.None);
        _ = result.Items;

        Assert.Equal(1, result.TotalRecordCount);
        Assert.Equal(allowed.Id, Assert.Single(capturedItems!).Id);
    }
}
