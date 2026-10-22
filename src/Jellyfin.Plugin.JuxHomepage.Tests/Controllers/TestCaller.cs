using System.Globalization;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Controllers;

/// <summary>
/// The caller of a controller test, described the way Jellyfin describes an authenticated request: the
/// claims its authentication handler sets on the principal (Jellyfin.Api <c>InternalClaimTypes</c>,
/// <c>CustomAuthenticationHandler</c>, 10.11.9), and the same answer from the authorization context.
/// </summary>
internal static class TestCaller
{
    /// <summary>The principal of a user session, or of a server API key when <paramref name="user"/> is null.</summary>
    internal static ClaimsPrincipal Principal(User? user, bool isAdministrator = false)
    {
        var isApiKey = user is null;
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, isApiKey || isAdministrator ? "Administrator" : "User"),
                new Claim("Jellyfin-UserId", (user?.Id ?? Guid.Empty).ToString("N", CultureInfo.InvariantCulture)),
                new Claim("Jellyfin-IsApiKey", isApiKey.ToString(CultureInfo.InvariantCulture))
            ],
            "test"));
    }

    /// <summary>The authorization context answering for a user session, or a server API key when <paramref name="user"/> is null.</summary>
    internal static Mock<IAuthorizationContext> AuthContext(User? user)
    {
        var mock = new Mock<IAuthorizationContext>();
        mock.Setup(a => a.GetAuthorizationInfo(It.IsAny<HttpContext>()))
            .ReturnsAsync(new AuthorizationInfo { User = user, IsApiKey = user is null });
        return mock;
    }
}
