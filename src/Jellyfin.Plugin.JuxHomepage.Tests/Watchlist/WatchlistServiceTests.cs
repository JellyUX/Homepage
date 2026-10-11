using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
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

        var result = service.GetItems(Guid.NewGuid(), null, null, null, 0, 50, true, CancellationToken.None);

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
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([new BaseItemDto { Name = "Liked Movie" }]);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object);

        var result = service.GetItems(user.Id, null, null, null, 0, 50, true, CancellationToken.None);

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
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns([]);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object);

        service.GetItems(user.Id, null, null, requested, 0, 50, true, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal([expected], capturedQuery!.IncludeItemTypes);
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
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns([]);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object);

        service.GetItems(user.Id, "Name", "Ascending", null, 0, 50, true, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal(ItemSortBy.SortName, capturedQuery!.OrderBy[0].Item1);
        Assert.Equal(SortOrder.Ascending, capturedQuery.OrderBy[0].Item2);
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
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns([movie]);

        var service = new WatchlistService(userManagerMock.Object, libraryManagerMock.Object, new Mock<IDtoService>().Object);

        var ids = service.GetLikedItemIds(user.Id, CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.True(capturedQuery!.IsLiked);
        Assert.Equal([movie.Id], ids);
    }
}
