using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

public sealed class WatchlistAutoRemoveServiceTests
{
    [Fact]
    public async Task OnUserDataSaved_PlaybackFinishedWithLikedAndPlayed_RemovesFromWatchlist()
    {
        var user = new User("test", "Default", "Default");
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        var userData = new UserItemData { Key = "k1", Played = true, Likes = true };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var userDataManagerMock = new Mock<IUserDataManager>();

        var service = new WatchlistAutoRemoveService(
            userDataManagerMock.Object,
            userManagerMock.Object,
            NullLogger<WatchlistAutoRemoveService>.Instance);

        await service.StartAsync(CancellationToken.None);

        userDataManagerMock.Raise(
            m => m.UserDataSaved += null,
            userDataManagerMock.Object,
            new UserDataSaveEventArgs
            {
                Item = item,
                UserData = userData,
                UserId = user.Id,
                SaveReason = UserDataSaveReason.PlaybackFinished
            });

        Assert.False(userData.Likes);
        userDataManagerMock.Verify(
            m => m.SaveUserData(user, item, userData, UserDataSaveReason.UpdateUserRating, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnUserDataSaved_TogglePlayedManualMarkAsWatched_DoesNotRemoveFromWatchlist()
    {
        var user = new User("test", "Default", "Default");
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        var userData = new UserItemData { Key = "k1", Played = true, Likes = true };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var userDataManagerMock = new Mock<IUserDataManager>();

        var service = new WatchlistAutoRemoveService(
            userDataManagerMock.Object,
            userManagerMock.Object,
            NullLogger<WatchlistAutoRemoveService>.Instance);

        await service.StartAsync(CancellationToken.None);

        // Manual "mark as watched" -- must NOT trigger auto-removal (TODO_V3.md Phase 5.3's own
        // caution: only a genuine end-of-playback should remove the item from the watchlist).
        userDataManagerMock.Raise(
            m => m.UserDataSaved += null,
            userDataManagerMock.Object,
            new UserDataSaveEventArgs
            {
                Item = item,
                UserData = userData,
                UserId = user.Id,
                SaveReason = UserDataSaveReason.TogglePlayed
            });

        Assert.True(userData.Likes);
        userDataManagerMock.Verify(
            m => m.SaveUserData(It.IsAny<User>(), It.IsAny<MediaBrowser.Controller.Entities.BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnUserDataSaved_PlaybackFinishedButNotLiked_DoesNothing()
    {
        var user = new User("test", "Default", "Default");
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        var userData = new UserItemData { Key = "k1", Played = true, Likes = false };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var userDataManagerMock = new Mock<IUserDataManager>();

        var service = new WatchlistAutoRemoveService(
            userDataManagerMock.Object,
            userManagerMock.Object,
            NullLogger<WatchlistAutoRemoveService>.Instance);

        await service.StartAsync(CancellationToken.None);

        userDataManagerMock.Raise(
            m => m.UserDataSaved += null,
            userDataManagerMock.Object,
            new UserDataSaveEventArgs
            {
                Item = item,
                UserData = userData,
                UserId = user.Id,
                SaveReason = UserDataSaveReason.PlaybackFinished
            });

        userDataManagerMock.Verify(
            m => m.SaveUserData(It.IsAny<User>(), It.IsAny<MediaBrowser.Controller.Entities.BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StopAsync_UnsubscribesHandler()
    {
        var userDataManagerMock = new Mock<IUserDataManager>();
        var service = new WatchlistAutoRemoveService(
            userDataManagerMock.Object,
            new Mock<IUserManager>().Object,
            NullLogger<WatchlistAutoRemoveService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var user = new User("test", "Default", "Default");
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        var userData = new UserItemData { Key = "k1", Played = true, Likes = true };

        userDataManagerMock.Raise(
            m => m.UserDataSaved += null,
            userDataManagerMock.Object,
            new UserDataSaveEventArgs
            {
                Item = item,
                UserData = userData,
                UserId = user.Id,
                SaveReason = UserDataSaveReason.PlaybackFinished
            });

        // Unsubscribed -- Likes must remain untouched since the handler no longer runs.
        Assert.True(userData.Likes);
    }

    [Fact]
    public async Task OnUserDataSaved_SaveThrows_DoesNotFailTheJellyfinOperation()
    {
        var user = new User("test", "Default", "Default");
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        var userData = new UserItemData { Key = "k1", Played = true, Likes = true };
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var userDataManagerMock = new Mock<IUserDataManager>();
        userDataManagerMock
            .Setup(m => m.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("database locked"));
        var service = new WatchlistAutoRemoveService(
            userDataManagerMock.Object,
            userManagerMock.Object,
            NullLogger<WatchlistAutoRemoveService>.Instance);
        await service.StartAsync(CancellationToken.None);

        var raise = Record.Exception(() => userDataManagerMock.Raise(
            m => m.UserDataSaved += null,
            userDataManagerMock.Object,
            new UserDataSaveEventArgs
            {
                Item = item,
                UserData = userData,
                UserId = user.Id,
                SaveReason = UserDataSaveReason.PlaybackFinished
            }));

        Assert.Null(raise);
    }

    [Fact]
    public async Task SwitchOff_FinishedMovieStaysInTheWatchlist()
    {
        var user = new User("test", "Default", "Default");
        var userData = new UserItemData { Key = "k1", Played = true, Likes = true };
        var userDataManager = new Mock<IUserDataManager>();
        var service = new WatchlistAutoRemoveService(
            userDataManager.Object,
            TestMocks.UserManagerReturning(user).Object,
            NullLogger<WatchlistAutoRemoveService>.Instance,
            new Mock<ILibraryManager>().Object,
            () => new PluginConfiguration { WatchlistAutoRemove = false });
        await service.StartAsync(CancellationToken.None);

        userDataManager.Raise(m => m.UserDataSaved += null, userDataManager.Object, Finished(user, new Movie { Id = Guid.NewGuid() }, userData));

        Assert.True(userData.Likes);
        userDataManager.Verify(m => m.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task LastEpisodeFinished_RemovesALikedSeries_OtherwiseKeepsIt(int unplayedLeft, bool removed)
    {
        var user = new User("test", "Default", "Default");
        var series = new Series { Id = Guid.NewGuid(), Name = "Show" };
        var episode = new Episode { Id = Guid.NewGuid(), SeriesId = series.Id };
        var seriesData = new UserItemData { Key = "s", Likes = true };
        var userDataManager = new Mock<IUserDataManager>();
        userDataManager.Setup(m => m.GetUserData(user, series)).Returns(seriesData);
        InternalItemsQuery? seen = null;
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemById(series.Id)).Returns(series);
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => seen = q)
            .Returns(Enumerable.Range(0, unplayedLeft).Select(_ => Guid.NewGuid()).ToList());
        var service = new WatchlistAutoRemoveService(
            userDataManager.Object,
            TestMocks.UserManagerReturning(user).Object,
            NullLogger<WatchlistAutoRemoveService>.Instance,
            library.Object,
            () => new PluginConfiguration());
        await service.StartAsync(CancellationToken.None);

        userDataManager.Raise(m => m.UserDataSaved += null, userDataManager.Object, Finished(user, episode, new UserItemData { Key = "e", Played = true }));

        Assert.Equal(!removed, seriesData.Likes);
        Assert.Equal((series.Id, false, BaseItemKind.Episode), (seen!.AncestorIds.Single(), seen.IsPlayed!.Value, seen.IncludeItemTypes.Single()));
        userDataManager.Verify(m => m.SaveUserData(user, series, seriesData, UserDataSaveReason.UpdateUserRating, It.IsAny<CancellationToken>()), removed ? Times.Once() : Times.Never());
    }

    private static UserDataSaveEventArgs Finished(User user, BaseItem item, UserItemData data) => new()
    {
        Item = item,
        UserData = data,
        UserId = user.Id,
        SaveReason = UserDataSaveReason.PlaybackFinished
    };
}
