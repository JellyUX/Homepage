using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Widgets;
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
    private readonly UserAccessWatch _watch;

    public UserAccessWatchTests()
    {
        _sessionCache = new SessionCache(_clock);
        _userManager.Setup(m => m.GetUserById(_user.Id)).Returns(_user);
        _watch = new UserAccessWatch(_userManager.Object, _sessionCache, NullLogger<UserAccessWatch>.Instance, _clock);
    }

    public void Dispose() => _sessionCache.Dispose();

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
    public void UserThatCannotBeRead_KeepsServing()
    {
        _userManager.Setup(m => m.GetUserById(_user.Id)).Throws(new InvalidOperationException("database is locked"));
        _sessionCache.Set(_user.Id, "en", []);

        _watch.Check(_user.Id);

        Assert.True(Cached());
    }

    private bool Cached() => _sessionCache.TryGet(_user.Id, "en", TimeSpan.FromMinutes(15), out _, out _);
}
