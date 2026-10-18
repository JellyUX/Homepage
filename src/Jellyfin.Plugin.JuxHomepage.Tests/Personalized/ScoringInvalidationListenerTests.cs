using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Personalized;

public sealed class ScoringInvalidationListenerTests
{
    private readonly User _user = new("test", "Default", "Default");
    private readonly Movie _film = new() { Id = Guid.NewGuid(), Name = "First Film", Genres = ["Drama"] };
    private readonly Mock<IUserDataManager> _userDataManagerMock = new();
    private readonly List<BaseItem> _watched = [];
    private readonly ScoringService _service;

    public ScoringInvalidationListenerTests()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(_user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
                q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie) ? _watched.ToList() : []);
        libraryManagerMock.Setup(m => m.GetPeople(It.IsAny<BaseItem>())).Returns([]);

        _service = new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            _userDataManagerMock.Object,
            () => new PluginConfiguration(),
            timeProvider: new ManualTimeProvider());
    }

    [Fact]
    public async Task PlaybackFinished_NewUserFirstFilm_PersonalizedValueAppearsSameDay()
    {
        var listener = new ScoringInvalidationListener(_userDataManagerMock.Object, _service, NullLogger<ScoringInvalidationListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        Assert.Empty(_service.GetTopGenres(_user.Id, 5));

        _watched.Add(_film);
        Raise(UserDataSaveReason.PlaybackFinished);
        _service.GetTopGenres(_user.Id, 5);
        await _service.WhenIdleForTesting();

        Assert.Equal("Drama", Assert.Single(_service.GetTopGenres(_user.Id, 5)).Value);
        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlaybackProgress_DoesNotRefreshTheSnapshot()
    {
        var listener = new ScoringInvalidationListener(_userDataManagerMock.Object, _service, NullLogger<ScoringInvalidationListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        Assert.Empty(_service.GetTopGenres(_user.Id, 5));

        _watched.Add(_film);
        Raise(UserDataSaveReason.PlaybackProgress);
        _service.GetTopGenres(_user.Id, 5);
        await _service.WhenIdleForTesting();

        Assert.Empty(_service.GetTopGenres(_user.Id, 5));
        await listener.StopAsync(CancellationToken.None);
    }

    private void Raise(UserDataSaveReason reason) =>
        _userDataManagerMock.Raise(
            m => m.UserDataSaved += null,
            _userDataManagerMock.Object,
            new UserDataSaveEventArgs
            {
                Item = _film,
                UserData = new UserItemData { Key = "k1", Played = true },
                UserId = _user.Id,
                SaveReason = reason
            });
}
