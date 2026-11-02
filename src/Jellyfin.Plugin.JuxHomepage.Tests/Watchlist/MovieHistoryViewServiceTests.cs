using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

public sealed class MovieHistoryViewServiceTests
{
    [Fact]
    public void GetItems_UnknownUser_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var service = new MovieHistoryViewService(
            new Mock<IMovieHistoryCacheService>().Object,
            userManagerMock.Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            NullLogger<MovieHistoryViewService>.Instance);

        var result = service.GetItems(Guid.NewGuid(), null, null, null, 0, 50, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public void GetItems_HydratesEntriesIntoDtosPreservingCacheOrder()
    {
        var user = new User("test", "Default", "Default");
        var movieA = new TestMovie { Id = Guid.NewGuid(), Name = "A" };
        var movieB = new TestMovie { Id = Guid.NewGuid(), Name = "B" };

        var entries = new List<MovieHistoryEntry>
        {
            new() { ItemId = movieB.Id, Name = "B", LastPlayedDate = new DateTime(2026, 2, 1) },
            new() { ItemId = movieA.Id, Name = "A", LastPlayedDate = new DateTime(2026, 1, 1) }
        };

        var cacheMock = new Mock<IMovieHistoryCacheService>();
        cacheMock.Setup(m => m.GetHistory(user.Id)).Returns(entries);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([movieA, movieB]);

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((items, _) => items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        var service = new MovieHistoryViewService(
            cacheMock.Object,
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<MovieHistoryViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, null, 0, 50, CancellationToken.None);

        Assert.Equal(2, result.TotalRecordCount);
        Assert.Equal(movieB.Id, result.Items[0].Id);
        Assert.Equal(movieA.Id, result.Items[1].Id);
    }

    [Fact]
    public void GetItems_SortByNameAscending_OrdersByName()
    {
        var user = new User("test", "Default", "Default");
        var movieZ = new TestMovie { Id = Guid.NewGuid(), Name = "Z" };
        var movieA = new TestMovie { Id = Guid.NewGuid(), Name = "A" };

        var entries = new List<MovieHistoryEntry>
        {
            new() { ItemId = movieZ.Id, Name = "Z" },
            new() { ItemId = movieA.Id, Name = "A" }
        };

        var cacheMock = new Mock<IMovieHistoryCacheService>();
        cacheMock.Setup(m => m.GetHistory(user.Id)).Returns(entries);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([movieA, movieZ]);

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((items, _) => items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        var service = new MovieHistoryViewService(
            cacheMock.Object,
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<MovieHistoryViewService>.Instance);

        var result = service.GetItems(user.Id, "sortname", "ascending", null, 0, 50, CancellationToken.None);

        Assert.Equal(movieA.Id, result.Items[0].Id);
        Assert.Equal(movieZ.Id, result.Items[1].Id);
    }

    [Fact]
    public void GetItems_SearchTerm_KeepsMatchingNamesIgnoringCaseAndAccents()
    {
        var user = new User("test", "Default", "Default");
        var amelie = new TestMovie { Id = Guid.NewGuid(), Name = "Le Fabuleux Destin d'Amélie Poulain" };
        var brazil = new TestMovie { Id = Guid.NewGuid(), Name = "Brazil" };

        var entries = new List<MovieHistoryEntry>
        {
            new() { ItemId = amelie.Id, Name = amelie.Name },
            new() { ItemId = brazil.Id, Name = brazil.Name }
        };

        var cacheMock = new Mock<IMovieHistoryCacheService>();
        cacheMock.Setup(m => m.GetHistory(user.Id)).Returns(entries);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([amelie, brazil]);

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((items, _) => items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        var service = new MovieHistoryViewService(
            cacheMock.Object,
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<MovieHistoryViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, "  AMELIE ", 0, 50, CancellationToken.None);

        Assert.Equal(1, result.TotalRecordCount);
        Assert.Equal(amelie.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public void GetItems_EntryNoLongerInLibrary_IsSkippedWithoutThrowing()
    {
        var user = new User("test", "Default", "Default");
        var entries = new List<MovieHistoryEntry> { new() { ItemId = Guid.NewGuid(), Name = "Gone" } };

        var cacheMock = new Mock<IMovieHistoryCacheService>();
        cacheMock.Setup(m => m.GetHistory(user.Id)).Returns(entries);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([]);

        var service = new MovieHistoryViewService(
            cacheMock.Object,
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IDtoService>().Object,
            NullLogger<MovieHistoryViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, null, 0, 50, CancellationToken.None);

        Assert.Empty(result.Items);

        // The total counts only entries that still resolve and are visible to the user.
        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public void GetItems_ItemOutsideUserRights_ExcludedFromItemsAndTotal()
    {
        var user = new User("test", "Default", "Default");
        var allowed = new TestMovie { Id = Guid.NewGuid(), Name = "Allowed" };
        var hidden = new TestMovie { Id = Guid.NewGuid(), Name = "Hidden", VisibleToUser = false };

        var cacheMock = new Mock<IMovieHistoryCacheService>();
        cacheMock.Setup(m => m.GetHistory(user.Id)).Returns(
        [
            new() { ItemId = hidden.Id, Name = "Hidden", LastPlayedDate = new DateTime(2026, 2, 1) },
            new() { ItemId = allowed.Id, Name = "Allowed", LastPlayedDate = new DateTime(2026, 1, 1) }
        ]);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([allowed, hidden]);

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((items, _) => items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        var service = new MovieHistoryViewService(
            cacheMock.Object,
            TestMocks.UserManagerReturning(user).Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<MovieHistoryViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, null, 0, 50, CancellationToken.None);

        Assert.Equal(1, result.TotalRecordCount);
        Assert.Equal(allowed.Id, Assert.Single(result.Items).Id);
    }

    // PERF-03 (DR-007): the whole history is checked with a lean read, never projected; the page is read
    // again and only those items reach the DTO projection.
    [Fact]
    public void GetItems_ProjectsThePageReadAgain_NeverTheLeanItems()
    {
        var user = new User("test", "Default", "Default");
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var cacheMock = new Mock<IMovieHistoryCacheService>();
        cacheMock.Setup(m => m.GetHistory(user.Id)).Returns(
            [.. ids.Select((id, i) => new MovieHistoryEntry { ItemId = id, Name = $"M{i}", LastPlayedDate = new DateTime(2026, 1, 10 - i) })]);
        var lean = ids.Select(id => new TestMovie { Id = id }).ToList();
        var full = ids.Select(id => new TestMovie { Id = id }).ToList();
        var pageQueries = new List<Guid[]>();
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
            {
                if (!q.DtoOptions.EnableUserData)
                {
                    return [.. lean];
                }

                pageQueries.Add(q.ItemIds);
                return [.. full.Where(m => q.ItemIds.Contains(m.Id))];
            });
        var projected = new List<BaseItem>();
        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock.StubBaseItemDtos((items, _) =>
        {
            projected.AddRange(items);
            return [.. items.Select(i => new BaseItemDto { Id = i.Id })];
        });
        var service = new MovieHistoryViewService(
            cacheMock.Object,
            TestMocks.UserManagerReturning(user).Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            NullLogger<MovieHistoryViewService>.Instance);

        var result = service.GetItems(user.Id, null, null, null, 1, 2, CancellationToken.None);

        Assert.Equal(5, result.TotalRecordCount);
        Assert.Equal([ids[1], ids[2]], result.Items.Select(d => d.Id));
        Assert.Equal([ids[1], ids[2]], Assert.Single(pageQueries));
        Assert.All(projected, item => Assert.Contains(item, full));
    }
}
