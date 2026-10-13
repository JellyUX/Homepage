using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

public sealed class StatisticsServiceTests
{
    [Fact]
    public void GetStatistics_AggregatesCountersFromBothCaches()
    {
        var userId = Guid.NewGuid();

        var progressCacheMock = new Mock<ISeriesProgressCacheService>();
        progressCacheMock.Setup(m => m.GetProgress(userId)).Returns(
        [
            new SeriesProgressEntry { SeriesId = Guid.NewGuid(), WatchedEpisodes = 10, TotalEpisodes = 10 }, // completed
            new SeriesProgressEntry { SeriesId = Guid.NewGuid(), WatchedEpisodes = 3, TotalEpisodes = 8 } // in progress
        ]);

        var historyCacheMock = new Mock<IMovieHistoryCacheService>();
        historyCacheMock.Setup(m => m.GetHistory(userId)).Returns(
        [
            new MovieHistoryEntry { ItemId = Guid.NewGuid(), Name = "Movie A" },
            new MovieHistoryEntry { ItemId = Guid.NewGuid(), Name = "Movie B" },
            new MovieHistoryEntry { ItemId = Guid.NewGuid(), Name = "Movie C" }
        ]);

        var service = new StatisticsService(progressCacheMock.Object, historyCacheMock.Object, Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>());

        var stats = service.GetStatistics(userId);

        Assert.Equal(3, stats.MoviesWatched);
        Assert.Equal(2, stats.SeriesTracked);
        Assert.Equal(1, stats.SeriesCompleted);
        Assert.Equal(13, stats.EpisodesWatched);
    }

    [Fact]
    public void GetStatistics_CountsTheMoviesLastPlayedSinceTheFirstOfTheMonth()
    {
        var userId = Guid.NewGuid();

        var progressCacheMock = new Mock<ISeriesProgressCacheService>();
        progressCacheMock.Setup(m => m.GetProgress(userId)).Returns([]);

        var historyCacheMock = new Mock<IMovieHistoryCacheService>();
        historyCacheMock.Setup(m => m.GetHistory(userId)).Returns(
        [
            new MovieHistoryEntry { ItemId = Guid.NewGuid(), LastPlayedDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) },
            new MovieHistoryEntry { ItemId = Guid.NewGuid(), LastPlayedDate = new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc) },
            new MovieHistoryEntry { ItemId = Guid.NewGuid(), LastPlayedDate = new DateTime(2026, 9, 30, 23, 59, 0, DateTimeKind.Utc) },
            new MovieHistoryEntry { ItemId = Guid.NewGuid(), LastPlayedDate = null }
        ]);

        var clock = new ManualTimeProvider { UtcNowValue = new DateTimeOffset(2026, 10, 13, 22, 0, 0, TimeSpan.Zero) };
        var service = new StatisticsService(progressCacheMock.Object, historyCacheMock.Object, Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), clock);

        var stats = service.GetStatistics(userId);

        Assert.Equal(4, stats.MoviesWatched);
        Assert.Equal(2, stats.MoviesWatchedThisMonth);
    }

    [Fact]
    public void GetStatistics_TopSeries_RanksByEpisodesWatchedThenLastPlayed_AndSkipsHiddenSeries()
    {
        var user = new User("test", "Default", "Default");
        var series = Enumerable.Range(0, 7)
            .Select(i => new TestSeries { Id = Guid.NewGuid(), Name = "Show " + i, VisibleToUser = i != 1 })
            .ToList();

        var progressCacheMock = new Mock<ISeriesProgressCacheService>();
        progressCacheMock.Setup(m => m.GetProgress(user.Id)).Returns(
        [
            new SeriesProgressEntry { SeriesId = series[0].Id, WatchedEpisodes = 3, TotalEpisodes = 10, LastPlayedDate = new DateTime(2026, 1, 1) },
            new SeriesProgressEntry { SeriesId = series[1].Id, WatchedEpisodes = 90, TotalEpisodes = 90 },
            new SeriesProgressEntry { SeriesId = series[2].Id, WatchedEpisodes = 40, TotalEpisodes = 40 },
            new SeriesProgressEntry { SeriesId = series[3].Id, WatchedEpisodes = 3, TotalEpisodes = 5, LastPlayedDate = new DateTime(2026, 9, 1) },
            new SeriesProgressEntry { SeriesId = series[4].Id, WatchedEpisodes = 12, TotalEpisodes = 24 },
            new SeriesProgressEntry { SeriesId = series[5].Id, WatchedEpisodes = 1, TotalEpisodes = 8 },
            new SeriesProgressEntry { SeriesId = series[6].Id, WatchedEpisodes = 7, TotalEpisodes = 7 }
        ]);

        var historyCacheMock = new Mock<IMovieHistoryCacheService>();
        historyCacheMock.Setup(m => m.GetHistory(user.Id)).Returns([]);

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(series.Cast<BaseItem>().ToList());

        var service = new StatisticsService(progressCacheMock.Object, historyCacheMock.Object, userManagerMock.Object, libraryManagerMock.Object);

        var top = service.GetStatistics(user.Id).TopSeries;

        Assert.Equal(["Show 2", "Show 4", "Show 6", "Show 3", "Show 0"], top.Select(e => e.Name));
        Assert.Equal(40, top[0].WatchedEpisodes);
        Assert.Equal(40, top[0].TotalEpisodes);
    }

    [Fact]
    public void GetStatistics_NoHistoryYet_ReturnsAllZeroes()
    {
        var userId = Guid.NewGuid();

        var progressCacheMock = new Mock<ISeriesProgressCacheService>();
        progressCacheMock.Setup(m => m.GetProgress(userId)).Returns([]);

        var historyCacheMock = new Mock<IMovieHistoryCacheService>();
        historyCacheMock.Setup(m => m.GetHistory(userId)).Returns([]);

        var service = new StatisticsService(progressCacheMock.Object, historyCacheMock.Object, Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>());

        var stats = service.GetStatistics(userId);

        Assert.Equal(0, stats.MoviesWatched);
        Assert.Equal(0, stats.SeriesTracked);
        Assert.Equal(0, stats.SeriesCompleted);
        Assert.Equal(0, stats.EpisodesWatched);
    }
}
