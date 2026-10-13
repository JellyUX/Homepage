using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

public sealed class MovieHistoryCacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-movie-history-tests-" + Guid.NewGuid());

    private MovieHistoryCacheService BuildService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new MovieHistoryCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            userManager,
            libraryManager,
            userDataManager,
            NullLogger<MovieHistoryCacheService>.Instance);
    }

    [Fact]
    public async Task RefreshAllAsync_UserWithWatchedMovies_CachesFullHistory()
    {
        var user = new User("test", "Default", "Default");
        var movie1 = new Movie { Id = Guid.NewGuid(), Name = "Movie 1" };
        var movie2 = new Movie { Id = Guid.NewGuid(), Name = "Movie 2" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IsPlayed == true && q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns([movie1, movie2]);

        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock
            .Setup(m => m.GetUserData(user, movie1))
            .Returns(new UserItemData { Key = "k1", LastPlayedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        userDataManagerMock
            .Setup(m => m.GetUserData(user, movie2))
            .Returns(new UserItemData { Key = "k2", LastPlayedDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) });

        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, userDataManagerMock.Object);

        var refreshed = await service.RefreshAllAsync(CancellationToken.None);
        Assert.True(refreshed);

        var history = service.GetHistory(user.Id);

        Assert.Equal(2, history.Count);
        Assert.Contains(history, e => e.ItemId == movie1.Id && e.Name == "Movie 1");
        Assert.Contains(history, e => e.ItemId == movie2.Id && e.Name == "Movie 2");
    }

    [Fact]
    public void GetHistory_NoRefreshYet_ReturnsEmpty()
    {
        var service = BuildService(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IUserDataManager>().Object);

        Assert.Empty(service.GetHistory(Guid.NewGuid()));
    }

    [Fact]
    public void IsStale_NoRefreshYet_ReturnsTrue()
    {
        var service = BuildService(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IUserDataManager>().Object);

        Assert.True(service.IsStale(Guid.NewGuid()));
    }

    [Fact]
    public async Task RefreshAllAsync_ConcurrentCalls_OnlyOneActuallyRuns()
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns(() =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return [];
        });

        var service = BuildService(
            userManagerMock.Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IUserDataManager>().Object);

        var firstCallTask = Task.Run(() => service.RefreshAllAsync(CancellationToken.None));

        var reachedFetch = started.Wait(TimeSpan.FromSeconds(5));
        Assert.True(reachedFetch, "First refresh did not reach the user-enumeration step in time.");

        var secondAcquired = service.TryAcquireRefreshLock();
        Assert.False(secondAcquired, "A second concurrent refresh should not acquire the lock.");

        release.Set();
        var firstResult = await firstCallTask;
        Assert.True(firstResult);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateMovies_ListsAFinishedMovieFirstAndDropsAnUnplayedOne(bool playedNow)
    {
        var user = new User("test", "Default", "Default");
        var seen = new Movie { Id = Guid.NewGuid(), Name = "Seen" };
        var finished = new Movie { Id = Guid.NewGuid(), Name = "Finished" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([seen, finished]);
        libraryManagerMock.Setup(m => m.GetItemById(finished.Id)).Returns(finished);
        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock.Setup(m => m.GetUserData(user, seen)).Returns(new UserItemData { Key = "s", Played = true, LastPlayedDate = new DateTime(2026, 1, 1) });
        userDataManagerMock.Setup(m => m.GetUserData(user, finished)).Returns(new UserItemData { Key = "f", Played = true, LastPlayedDate = new DateTime(2025, 1, 1) });
        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, userDataManagerMock.Object);
        await service.RefreshAllAsync(CancellationToken.None);

        userDataManagerMock.Setup(m => m.GetUserData(user, finished)).Returns(new UserItemData { Key = "f", Played = playedNow, LastPlayedDate = new DateTime(2026, 2, 1) });
        service.UpdateMovies(user.Id, [finished.Id]);

        var expected = playedNow ? new[] { finished.Id, seen.Id } : [seen.Id];
        Assert.Equal(expected, service.GetHistory(user.Id).Select(e => e.ItemId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
