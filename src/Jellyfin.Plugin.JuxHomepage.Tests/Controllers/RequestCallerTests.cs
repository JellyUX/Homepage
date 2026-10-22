using System.Security.Claims;
using Jellyfin.Plugin.JuxHomepage.Controllers;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Controllers;

/// <summary>
/// DEC-P07-14: the caller is read from the claims Jellyfin's authentication handler set, without a
/// second lookup of the token; a request without them is still resolved by the authorization context.
/// </summary>
public sealed class RequestCallerTests
{
    [Fact]
    public async Task Resolve_UserSessionClaims_AreReadWithoutAskingTheAuthorizationContext()
    {
        var user = TestMocks.DefaultUser();
        var authContext = new Mock<IAuthorizationContext>(MockBehavior.Strict);

        var caller = await RequestCaller.ResolveAsync(new DefaultHttpContext { User = TestCaller.Principal(user) }, authContext.Object);

        Assert.Equal((user.Id, false), caller);
    }

    [Fact]
    public async Task Resolve_ApiKeyClaims_AreReadWithoutAskingTheAuthorizationContext()
    {
        var authContext = new Mock<IAuthorizationContext>(MockBehavior.Strict);

        var caller = await RequestCaller.ResolveAsync(new DefaultHttpContext { User = TestCaller.Principal(null) }, authContext.Object);

        Assert.Equal((Guid.Empty, true), caller);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("not-a-guid", "False")]
    [InlineData("0f8fad5bd9cb469fa16570867728950e", null)]
    [InlineData("0f8fad5bd9cb469fa16570867728950e", "maybe")]
    public async Task Resolve_WithoutReadableClaims_AsksTheAuthorizationContext(string? userId, string? isApiKey)
    {
        var user = TestMocks.DefaultUser();
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(RequestCaller.UserIdClaim, userId));
        }

        if (isApiKey is not null)
        {
            claims.Add(new Claim(RequestCaller.IsApiKeyClaim, isApiKey));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };

        var caller = await RequestCaller.ResolveAsync(context, TestCaller.AuthContext(user).Object);

        Assert.Equal((user.Id, false), caller);
    }
}
