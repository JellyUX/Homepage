using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
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
}
