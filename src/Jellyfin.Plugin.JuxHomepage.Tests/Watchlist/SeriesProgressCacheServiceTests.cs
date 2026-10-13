using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

public sealed class SeriesProgressCacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-series-progress-tests-" + Guid.NewGuid());

    private SeriesProgressCacheService BuildService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new SeriesProgressCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            userManager,
            libraryManager,
            userDataManager,
            NullLogger<SeriesProgressCacheService>.Instance);
    }

    [Fact]
    public async Task RefreshAllAsync_UserWithPartiallyWatchedSeries_CachesWatchedAndTotalCounts()
    {
        var user = new User("test", "Default", "Default");
        var seriesId = Guid.NewGuid();

        var episode1 = new Episode { Id = Guid.NewGuid(), Name = "E1", SeriesId = seriesId, SeriesName = "My Show", ParentIndexNumber = 1, IndexNumber = 3 };
        var episode2 = new Episode { Id = Guid.NewGuid(), Name = "E2", SeriesId = seriesId, SeriesName = "My Show" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Episode))))
            .Returns([episode1, episode2]);

        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock
            .Setup(m => m.GetUserData(user, episode1))
            .Returns(new UserItemData { Key = "k1", Played = true, LastPlayedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        userDataManagerMock
            .Setup(m => m.GetUserData(user, episode2))
            .Returns(new UserItemData { Key = "k2", Played = false });

        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, userDataManagerMock.Object);

        var refreshed = await service.RefreshAllAsync(CancellationToken.None);
        Assert.True(refreshed);

        var progress = service.GetProgress(user.Id);

        var entry = Assert.Single(progress);
        Assert.Equal(seriesId, entry.SeriesId);
        Assert.Equal("My Show", entry.SeriesName);
        Assert.Equal(1, entry.WatchedEpisodes);
        Assert.Equal(2, entry.TotalEpisodes);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), entry.LastPlayedDate);
        Assert.Equal(episode1.Id, entry.LastEpisodeId);
        Assert.Equal("E1", entry.LastEpisodeName);
        Assert.Equal(1, entry.LastEpisodeSeasonNumber);
        Assert.Equal(3, entry.LastEpisodeIndexNumber);
    }

    [Fact]
    public async Task RefreshAllAsync_MultipleWatchedEpisodes_LastEpisodeMatchesMostRecentPlayDate()
    {
        var user = new User("test", "Default", "Default");
        var seriesId = Guid.NewGuid();

        // Deliberately out of chronological order in the source list, to prove the "last episode" is
        // picked by comparing LastPlayedDate, not by list/insertion order.
        var episodeOld = new Episode { Id = Guid.NewGuid(), Name = "Old", SeriesId = seriesId, SeriesName = "My Show", ParentIndexNumber = 1, IndexNumber = 1 };
        var episodeNewest = new Episode { Id = Guid.NewGuid(), Name = "Newest", SeriesId = seriesId, SeriesName = "My Show", ParentIndexNumber = 2, IndexNumber = 5 };
        var episodeMiddle = new Episode { Id = Guid.NewGuid(), Name = "Middle", SeriesId = seriesId, SeriesName = "My Show", ParentIndexNumber = 1, IndexNumber = 2 };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Episode))))
            .Returns([episodeOld, episodeNewest, episodeMiddle]);

        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock.Setup(m => m.GetUserData(user, episodeOld))
            .Returns(new UserItemData { Key = "k1", Played = true, LastPlayedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        userDataManagerMock.Setup(m => m.GetUserData(user, episodeNewest))
            .Returns(new UserItemData { Key = "k2", Played = true, LastPlayedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) });
        userDataManagerMock.Setup(m => m.GetUserData(user, episodeMiddle))
            .Returns(new UserItemData { Key = "k3", Played = true, LastPlayedDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) });

        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, userDataManagerMock.Object);

        await service.RefreshAllAsync(CancellationToken.None);

        var entry = Assert.Single(service.GetProgress(user.Id));
        Assert.Equal(episodeNewest.Id, entry.LastEpisodeId);
        Assert.Equal("Newest", entry.LastEpisodeName);
        Assert.Equal(2, entry.LastEpisodeSeasonNumber);
        Assert.Equal(5, entry.LastEpisodeIndexNumber);
    }

    [Fact]
    public async Task RefreshAllAsync_NoWatchedEpisodesForASeries_ExcludesItFromResults()
    {
        var user = new User("test", "Default", "Default");
        var seriesId = Guid.NewGuid();
        var episode = new Episode { Id = Guid.NewGuid(), Name = "E1", SeriesId = seriesId, SeriesName = "Unwatched Show" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.Episode))))
            .Returns([episode]);

        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock.Setup(m => m.GetUserData(user, episode)).Returns(new UserItemData { Key = "k2", Played = false });

        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, userDataManagerMock.Object);

        await service.RefreshAllAsync(CancellationToken.None);

        Assert.Empty(service.GetProgress(user.Id));
    }

    [Fact]
    public async Task RefreshAllAsync_LoadsEpisodesOfWatchedSeriesOnly()
    {
        // F-06: only series with a played episode have their episodes read.
        var user = new User("test", "Default", "Default");
        var watchedSeries = Guid.NewGuid();
        var played = new Episode { Id = Guid.NewGuid(), Name = "E1", SeriesId = watchedSeries, SeriesName = "Watched" };
        var queries = new List<InternalItemsQuery>();

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns([played]);

        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock.Setup(m => m.GetUserData(user, played)).Returns(new UserItemData { Key = "k1", Played = true });

        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, userDataManagerMock.Object);

        await service.RefreshAllAsync(CancellationToken.None);

        Assert.Equal(2, queries.Count);
        Assert.True(queries[0].IsPlayed);
        Assert.Equal([watchedSeries], queries[1].AncestorIds);
        Assert.Equal(1, Assert.Single(service.GetProgress(user.Id)).WatchedEpisodes);
    }

    [Fact]
    public async Task RefreshAllAsync_NothingPlayed_ReadsNoEpisodeList()
    {
        var user = new User("test", "Default", "Default");
        var queries = new List<InternalItemsQuery>();
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns([]);

        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, new Mock<IUserDataManager>().Object);

        await service.RefreshAllAsync(CancellationToken.None);

        Assert.True(Assert.Single(queries).IsPlayed);
        Assert.Empty(service.GetProgress(user.Id));
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

    [Fact]
    public void GetProgress_NewUserWithoutHistory_IsComputedOnceNotOnEveryOpening()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var queries = 0;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback(() => queries++)
            .Returns([]);
        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, new Mock<IUserDataManager>().Object);

        Assert.Empty(service.GetProgress(user.Id));
        Assert.Empty(service.GetProgress(user.Id));

        Assert.Equal(1, queries);
        Assert.False(service.IsStale(user.Id));
    }

    [Fact]
    public async Task UpdateSeries_EpisodeFinished_RecomputesThatSeriesOnlyAndKeepsTheOthers()
    {
        var user = new User("test", "Default", "Default");
        var showA = Guid.NewGuid();
        var showB = Guid.NewGuid();
        var a1 = new Episode { Id = Guid.NewGuid(), Name = "A1", SeriesId = showA, SeriesName = "Show A" };
        var a2 = new Episode { Id = Guid.NewGuid(), Name = "A2", SeriesId = showA, SeriesName = "Show A" };
        var b1 = new Episode { Id = Guid.NewGuid(), Name = "B1", SeriesId = showB, SeriesName = "Show B" };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUsers()).Returns([user]);
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => q.AncestorIds.Length == 1 && q.AncestorIds[0] == showA ? [a1, a2] : [a1, a2, b1]);
        var played = new Dictionary<Guid, DateTime?> { [a1.Id] = new DateTime(2026, 1, 1), [b1.Id] = new DateTime(2026, 1, 2) };
        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock
            .Setup(m => m.GetUserData(user, It.IsAny<BaseItem>()))
            .Returns((User _, BaseItem item) => new UserItemData { Key = "k", Played = played.ContainsKey(item.Id), LastPlayedDate = played.GetValueOrDefault(item.Id) });
        var service = BuildService(userManagerMock.Object, libraryManagerMock.Object, userDataManagerMock.Object);
        await service.RefreshAllAsync(CancellationToken.None);

        played[a2.Id] = new DateTime(2026, 1, 3);
        service.UpdateSeries(user.Id, [showA]);

        var progress = service.GetProgress(user.Id);
        Assert.Equal([showA, showB], progress.Select(e => e.SeriesId));
        Assert.Equal((2, 2, "A2"), (progress[0].WatchedEpisodes, progress[0].TotalEpisodes, progress[0].LastEpisodeName));
        Assert.Equal(1, progress[1].WatchedEpisodes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
