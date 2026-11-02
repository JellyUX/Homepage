using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Database.Implementations.Entities;
using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

public sealed class WatchlistServiceTests
{
    [Fact]
    public void GetItems_UnknownUser_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var service = new WatchlistService(
            userManagerMock.Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var result = service.GetItems(Guid.NewGuid(), null, null, null, null, 0, 50, true, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public void GetItems_QueriesIsLikedTrueWithDefaultSortAndBothTypes()
    {
        var user = new User("test", "Default", "Default");
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Liked Movie" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([movie]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((_, _) => [new BaseItemDto { Name = "Liked Movie" }]);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object);

        var result = service.GetItems(user.Id, null, null, null, null, 0, 50, true, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.True(capturedQuery!.IsLiked);
        Assert.Equal([BaseItemKind.Movie, BaseItemKind.Series], capturedQuery.IncludeItemTypes);
        Assert.Equal(ItemSortBy.DateCreated, capturedQuery.OrderBy[0].Item1);
        Assert.Equal(SortOrder.Descending, capturedQuery.OrderBy[0].Item2);
        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalRecordCount);
    }

    [Theory]
    [InlineData("movie", BaseItemKind.Movie)]
    [InlineData("series", BaseItemKind.Series)]
    public void GetItems_IncludeItemTypesFilter_RestrictsToRequestedType(string requested, BaseItemKind expected)
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((_, _) => []);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object);

        service.GetItems(user.Id, null, null, requested, null, 0, 50, true, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal([expected], capturedQuery!.IncludeItemTypes);
    }

    // The tab speaks the WidgetSort vocabulary of the sections (QUAL-06.b); anything else, the old tab
    // names included, is the default, the date the item was added.
    [Theory]
    [InlineData("SortName", ItemSortBy.SortName)]
    [InlineData("DateCreated", ItemSortBy.DateCreated)]
    [InlineData("PremiereDate", ItemSortBy.PremiereDate)]
    [InlineData("CommunityRating", ItemSortBy.CommunityRating)]
    [InlineData("premieredate", ItemSortBy.PremiereDate)]
    [InlineData("Name", ItemSortBy.DateCreated)]
    [InlineData(null, ItemSortBy.DateCreated)]
    public void GetItems_SortBy_MapsTheWidgetSortVocabulary(string? sortBy, ItemSortBy expected)
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, Mock.Of<IDtoService>());

        service.GetItems(user.Id, sortBy, "Descending", null, null, 0, 50, true, CancellationToken.None);

        Assert.Equal(expected, capturedQuery!.OrderBy[0].Item1);
    }

    [Fact]
    public void GetItems_SortByNameAscending_MapsToSortNameAndAscendingOrder()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((_, _) => []);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object);

        service.GetItems(user.Id, "sortname", "ascending", null, null, 0, 50, true, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal(ItemSortBy.SortName, capturedQuery!.OrderBy[0].Item1);
        Assert.Equal(SortOrder.Ascending, capturedQuery.OrderBy[0].Item2);
    }

    [Theory]
    [InlineData("  DUNE ", "dune")]
    [InlineData("Colère", "colere")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void GetItems_SearchTerm_MapsToFoldedNameContains(string? searchTerm, string? expected)
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((_, _) => []);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object);

        service.GetItems(user.Id, null, null, null, searchTerm, 0, 50, true, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal(expected, capturedQuery!.NameContains);
    }

    [Fact]
    public void GetLikedItemIds_ReturnsItemIdsFromLikedQuery()
    {
        var user = new User("test", "Default", "Default");
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Liked Movie" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns([movie.Id]);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, new Mock<IDtoService>().Object);

        var ids = service.GetLikedItemIds(user.Id, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.True(capturedQuery!.IsLiked);
        Assert.Equal([BaseItemKind.Movie, BaseItemKind.Series], capturedQuery.IncludeItemTypes);
        Assert.Equal([movie.Id], ids);
    }

    [Fact]
    public void GetLikedItemIds_ReadTwice_QueriesOnce()
    {
        var user = new User("test", "Default", "Default");
        var movie = Guid.NewGuid();
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([movie]);
        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, new Mock<IDtoService>().Object, new LikedItemIdsCache(() => null));

        service.GetLikedItemIds(user.Id, CancellationToken.None);
        var ids = service.GetLikedItemIds(user.Id, CancellationToken.None);

        Assert.Equal([movie], ids);
        libraryManagerMock.Verify(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    // PERF-04: an empty watchlist is known from the liked ids kept in memory; the tab answers without
    // reading the library, with the same JSON as the query that found nothing.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetItems_EmptyLikedIdsInMemory_AnswersWithoutReadingTheLibrary(bool includeTotalRecordCount)
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var library = new TestMocks.CountingLibraryManager();
        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((items, _) => [.. items.Select(i => new BaseItemDto { Id = i.Id })]);
        var likedIds = new LikedItemIdsCache(() => new PluginConfiguration());
        Assert.True(likedIds.Set(user.Id, [], likedIds.VersionOf(user.Id)));
        var service = new WatchlistService(userManagerMock.Object, library.Object, dtoServiceMock.Object, likedIds);

        var result = service.GetItems(user.Id, null, null, "movie", "abc", 0, 50, includeTotalRecordCount, CancellationToken.None);

        Assert.Equal(0, library.Total);
        var queried = WidgetResult.Project(dtoServiceMock.Object, [], WidgetDtoOptions.Standard(), user, includeTotalRecordCount ? 0 : null);
        Assert.Equal(JsonSerializer.Serialize(queried), JsonSerializer.Serialize(result));
    }

    [Fact]
    public void GetItems_LikedIdsNotInMemory_StillQueriesTheLibrary()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var library = new TestMocks.CountingLibraryManager();
        var service = new WatchlistService(
            userManagerMock.Object,
            library.Object,
            new Mock<IDtoService>().Object,
            new LikedItemIdsCache(() => new PluginConfiguration()));

        service.GetItems(user.Id, null, null, null, null, 0, 50, true, CancellationToken.None);

        Assert.Equal(1, library.Total);
    }
}
