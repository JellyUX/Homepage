using System.Security.Claims;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// Who sent an authenticated request: read from the claims Jellyfin's authentication handler set on the
/// request (<c>CustomAuthenticationHandler</c>, which already looked the token up), instead of asking
/// <see cref="IAuthorizationContext"/> again, which looks the token up in the database a second time per
/// request (its per-request memo is filled only by the <see cref="HttpContext"/> overload, never by the
/// handler).
/// </summary>
internal static class RequestCaller
{
    /// <summary>The user id claim, Guid in "N" format (Jellyfin.Api <c>InternalClaimTypes.UserId</c>, 10.11.9 and 10.11.10).</summary>
    internal const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>The API key claim, "True" or "False" (Jellyfin.Api <c>InternalClaimTypes.IsApiKey</c>, 10.11.9 and 10.11.10).</summary>
    internal const string IsApiKeyClaim = "Jellyfin-IsApiKey";

    /// <summary>
    /// Returns the authenticated user id and whether the request used a server API key. A request
    /// without both claims, or with one that does not parse, is resolved by the authorization context.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="authContext">Jellyfin's authorization context, the fallback.</param>
    /// <returns>The caller's user id (empty for an API key) and whether it is an API key.</returns>
    internal static async Task<(Guid UserId, bool IsApiKey)> ResolveAsync(HttpContext context, IAuthorizationContext authContext)
    {
        if (TryRead(context.User, out var caller))
        {
            return caller;
        }

        var authInfo = await authContext.GetAuthorizationInfo(context).ConfigureAwait(false);
        return (authInfo.UserId, authInfo.IsApiKey);
    }

    private static bool TryRead(ClaimsPrincipal? principal, out (Guid UserId, bool IsApiKey) caller)
    {
        caller = default;
        if (Guid.TryParseExact(principal?.FindFirstValue(UserIdClaim), "N", out var userId)
            && bool.TryParse(principal?.FindFirstValue(IsApiKeyClaim), out var isApiKey))
        {
            caller = (userId, isApiKey);
            return true;
        }

        return false;
    }
}
