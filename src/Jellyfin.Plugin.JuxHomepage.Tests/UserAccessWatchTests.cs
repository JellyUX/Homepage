using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// R11 review (DEC-P07-15): Jellyfin raises no event when an administrator changes what a user may see,
/// so the user's cached home screen is dropped when their access, read at most once a minute, changed.
/// </summary>
public sealed class UserAccessWatchTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly SessionCache _sessionCache;
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Jellyfin.Database.Implementations.Entities.User _user = TestMocks.DefaultUser();
    private readonly List<BaseItem> _watched = [];
    private readonly ScoringService _scoring;
    private readonly UserAccessWatch _watch;

    public UserAccessWatchTests()
    {
        _sessionCache = new SessionCache(_clock);
        _userManager.Setup(m => m.GetUserById(_user.Id)).Returns(_user);

        // The watched films the user's access lets the scoring read.
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
                q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie) ? _watched.ToList() : []);
        libraryManager.Setup(m => m.GetPeople(It.IsAny<BaseItem>())).Returns([]);
        _scoring = new ScoringService(_userManager.Object, libraryManager.Object, new Mock<IUserDataManager>().Object, () => new PluginConfiguration(), timeProvider: _clock);

        _watch = new UserAccessWatch(_userManager.Object, _sessionCache, _scoring, NullLogger<UserAccessWatch>.Instance, _clock);
    }

    public void Dispose()
    {
        _scoring.Dispose();
        _sessionCache.Dispose();
    }

    [Fact]
    public void LibraryRemoved_DropsTheCachedHomeScreen_WithinTheCheckInterval()
    {
        _watch.Check(_user.Id);
        _sessionCache.Set(_user.Id, "en", []);

        _user.SetPreference(PreferenceKind.EnabledFolders, [Guid.NewGuid().ToString("N")]);
        _clock.Advance(UserAccessWatch.CheckInterval / 2);
        _watch.Check(_user.Id);
        Assert.True(Cached());

        _clock.Advance(UserAccessWatch.CheckInterval / 2);
        _watch.Check(_user.Id);
        Assert.False(Cached());
    }

    [Fact]
    public void UnchangedAccess_KeepsTheCachedHomeScreen_AndIsReadOncePerInterval()
    {
        _watch.Check(_user.Id);
        _sessionCache.Set(_user.Id, "en", []);

        for (var i = 0; i < 10; i++)
        {
            _watch.Check(_user.Id);
        }

        _clock.Advance(UserAccessWatch.CheckInterval);
        _watch.Check(_user.Id);

        Assert.True(Cached());
        _userManager.Verify(m => m.GetUserById(_user.Id), Times.Exactly(2));
    }

    [Fact]
    public void ParentalRatingLowered_DropsTheCachedHomeScreen()
    {
        _watch.Check(_user.Id);
        _sessionCache.Set(_user.Id, "en", []);

        _user.MaxParentalRatingScore = 12;
        _clock.Advance(UserAccessWatch.CheckInterval);
        _watch.Check(_user.Id);

        Assert.False(Cached());
    }

    [Fact]
    public async Task AccessChanged_DropsTheScoresComputedWithTheFormerAccess()
    {
        // Scored with the former access, under which only the adventure film was visible.
        _watch.Check(_user.Id);
        _watched.Add(new Movie { Id = Guid.NewGuid(), Name = "Family Film", Genres = ["Adventure"] });
        _scoring.GetTopGenres(_user.Id, 1);
        await _scoring.WhenIdleForTesting();

        // Read the same day, the snapshot would otherwise still be served (fresh until midnight).
        _user.MaxParentalRatingScore = 18;
        _watched.Clear();
        _watched.Add(new Movie { Id = Guid.NewGuid(), Name = "Adult Film", Genres = ["Drama"] });
        _clock.Advance(UserAccessWatch.CheckInterval);
        _watch.Check(_user.Id);

        Assert.Equal("Drama", Assert.Single(_scoring.GetTopGenres(_user.Id, 1)).Value);
    }

    [Fact]
    public void UserThatCannotBeRead_KeepsServing()
    {
        _userManager.Setup(m => m.GetUserById(_user.Id)).Throws(new InvalidOperationException("database is locked"));
        _sessionCache.Set(_user.Id, "en", []);

        _watch.Check(_user.Id);

        Assert.True(Cached());
    }

    private bool Cached() => _sessionCache.TryGet(_user.Id, "en", TimeSpan.FromMinutes(15), out _, out _);
}
