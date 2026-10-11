using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

public sealed class SeriesProgressViewServiceTests
{
    [Fact]
    public void GetItems_UnknownUser_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var service = new SeriesProgressViewService(
            new Mock<ISeriesProgressCacheService>().Object,
            userManagerMock.Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            NullLogger<SeriesProgressViewService>.Instance);

        var result = service.GetItems(Guid.NewGuid(), null, null, 0, 50, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public void GetItems_HydratesEntriesIntoDtosPreservingCacheOrder()
    {
        var user = new User("test", "Default", "Default");
        var seriesA = new TestSeries { Id = Guid.NewGuid(), Name = "A" };
        var seriesB = new TestSeries { Id = Guid.NewGuid(), Name = "B" };

        // Cache order is B then A (most-recently-played first); GetItemList below returns them in
        // the opposite order to prove the service re-sorts by the cache's own order, not the query's.
        var entries = new List<SeriesProgressEntry>
        {
            new() { SeriesId = seriesB.Id, SeriesName = "B", WatchedEpisodes = 2, TotalEpisodes = 10, LastPlayedDate = new DateTime(2026, 2, 1) },
            new() { SeriesId = seriesA.Id, SeriesName = "A", WatchedEpisodes = 5, TotalEpisodes = 5, LastPlayedDate = new DateTime(2026, 1, 1) }
        };

        var cacheMock = new Mock<ISeriesProgressCacheService>();
        cacheMock.Setup(m => m.GetProgress(user.Id)).Returns(entries);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns([seriesA, seriesB]);

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User _, BaseItem? _) =>
                items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        var service = new SeriesProgressViewService(
            cacheMock.Object,
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<SeriesProgressViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, 0, 50, CancellationToken.None);

        Assert.Equal(2, result.TotalRecordCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(seriesB.Id, result.Items[0].Item.Id);
        Assert.Equal(2, result.Items[0].WatchedEpisodes);
        Assert.Equal(seriesA.Id, result.Items[1].Item.Id);
        Assert.Equal(5, result.Items[1].WatchedEpisodes);
    }

    [Fact]
    public void GetItems_SortByNameAscending_OrdersBySeriesName()
    {
        var user = new User("test", "Default", "Default");
        var seriesZ = new TestSeries { Id = Guid.NewGuid(), Name = "Z" };
        var seriesA = new TestSeries { Id = Guid.NewGuid(), Name = "A" };

        var entries = new List<SeriesProgressEntry>
        {
            new() { SeriesId = seriesZ.Id, SeriesName = "Z", WatchedEpisodes = 1, TotalEpisodes = 1 },
            new() { SeriesId = seriesA.Id, SeriesName = "A", WatchedEpisodes = 1, TotalEpisodes = 1 }
        };

        var cacheMock = new Mock<ISeriesProgressCacheService>();
        cacheMock.Setup(m => m.GetProgress(user.Id)).Returns(entries);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([seriesA, seriesZ]);

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User _, BaseItem? _) =>
                items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        var service = new SeriesProgressViewService(
            cacheMock.Object,
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<SeriesProgressViewService>.Instance);

        var result = service.GetItems(user.Id, "Name", "Ascending", 0, 50, CancellationToken.None);

        Assert.Equal(seriesA.Id, result.Items[0].Item.Id);
        Assert.Equal(seriesZ.Id, result.Items[1].Item.Id);
    }

    [Fact]
    public void GetItems_EntryNoLongerInLibrary_IsSkippedWithoutThrowing()
    {
        var user = new User("test", "Default", "Default");
        var missingSeriesId = Guid.NewGuid();

        var entries = new List<SeriesProgressEntry>
        {
            new() { SeriesId = missingSeriesId, SeriesName = "Gone", WatchedEpisodes = 1, TotalEpisodes = 1 }
        };

        var cacheMock = new Mock<ISeriesProgressCacheService>();
        cacheMock.Setup(m => m.GetProgress(user.Id)).Returns(entries);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([]);

        var service = new SeriesProgressViewService(
            cacheMock.Object,
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IDtoService>().Object,
            NullLogger<SeriesProgressViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, 0, 50, CancellationToken.None);

        Assert.Empty(result.Items);

        // The total counts only entries that still resolve and are visible to the user.
        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public void GetItems_SeriesOutsideUserRights_ExcludedFromItemsAndTotal()
    {
        var user = new User("test", "Default", "Default");
        var allowed = new TestSeries { Id = Guid.NewGuid(), Name = "Allowed" };
        var hidden = new TestSeries { Id = Guid.NewGuid(), Name = "Hidden", VisibleToUser = false };

        var cacheMock = new Mock<ISeriesProgressCacheService>();
        cacheMock.Setup(m => m.GetProgress(user.Id)).Returns(
        [
            new() { SeriesId = hidden.Id, SeriesName = "Hidden", WatchedEpisodes = 1, TotalEpisodes = 2, LastPlayedDate = new DateTime(2026, 2, 1) },
            new() { SeriesId = allowed.Id, SeriesName = "Allowed", WatchedEpisodes = 1, TotalEpisodes = 2, LastPlayedDate = new DateTime(2026, 1, 1) }
        ]);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([allowed, hidden]);

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User _, BaseItem? _) =>
                items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        var service = new SeriesProgressViewService(
            cacheMock.Object,
            TestMocks.UserManagerReturning(user).Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<SeriesProgressViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, 0, 50, CancellationToken.None);

        Assert.Equal(1, result.TotalRecordCount);
        Assert.Equal(allowed.Id, Assert.Single(result.Items).Item.Id);
    }
}
