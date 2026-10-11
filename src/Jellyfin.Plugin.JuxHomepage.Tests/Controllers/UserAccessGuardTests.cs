using Jellyfin.Plugin.JuxHomepage.Controllers;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Controllers;

public sealed class UserAccessGuardTests
{
    private static readonly Guid Self = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void RegularUser_OwnId_ReadsOwnData()
    {
        Assert.Equal(Self, UserAccessGuard.ResolveUserId(Self, Self, isApiKey: false, isAdministrator: false));
    }

    [Fact]
    public void RegularUser_NoId_ReadsOwnDataFromToken()
    {
        Assert.Equal(Self, UserAccessGuard.ResolveUserId(Guid.Empty, Self, isApiKey: false, isAdministrator: false));
    }

    [Fact]
    public void RegularUser_OtherUsersId_IsRefused()
    {
        Assert.Null(UserAccessGuard.ResolveUserId(Other, Self, isApiKey: false, isAdministrator: false));
    }

    [Fact]
    public void Administrator_OtherUsersId_ReadsThatUser()
    {
        Assert.Equal(Other, UserAccessGuard.ResolveUserId(Other, Self, isApiKey: false, isAdministrator: true));
    }

    [Fact]
    public void Administrator_NoId_ReadsOwnData()
    {
        Assert.Equal(Self, UserAccessGuard.ResolveUserId(Guid.Empty, Self, isApiKey: false, isAdministrator: true));
    }

    [Fact]
    public void ServerApiKey_NamedUser_ReadsThatUser()
    {
        Assert.Equal(Other, UserAccessGuard.ResolveUserId(Other, Guid.Empty, isApiKey: true, isAdministrator: false));
    }

    [Fact]
    public void ServerApiKey_NoId_IsRefused()
    {
        Assert.Null(UserAccessGuard.ResolveUserId(Guid.Empty, Guid.Empty, isApiKey: true, isAdministrator: false));
    }

    [Fact]
    public void NoAuthenticatedUser_IsRefused()
    {
        Assert.Null(UserAccessGuard.ResolveUserId(Other, Guid.Empty, isApiKey: false, isAdministrator: false));
    }
}
