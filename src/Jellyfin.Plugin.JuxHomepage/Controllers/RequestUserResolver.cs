using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// <see cref="IRequestUserResolver"/> over the request's claims, with Jellyfin's authorization context as
/// the fallback (<see cref="RequestCaller"/>).
/// </summary>
public sealed class RequestUserResolver : IRequestUserResolver
{
    private readonly IAuthorizationContext _authContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestUserResolver"/> class.
    /// </summary>
    /// <param name="authContext">Jellyfin authorization context, read when the claims do not name the caller.</param>
    public RequestUserResolver(IAuthorizationContext authContext)
    {
        _authContext = authContext;
    }

    /// <inheritdoc/>
    public Task<(Guid UserId, bool IsApiKey)> ResolveCallerAsync(HttpContext context) =>
        RequestCaller.ResolveAsync(context, _authContext);

    /// <inheritdoc/>
    public async Task<(Guid? UserId, bool IsCaller)> ResolveAsync(HttpContext context, Guid requestedUserId)
    {
        var caller = await RequestCaller.ResolveAsync(context, _authContext).ConfigureAwait(false);
        var userId = UserAccessGuard.ResolveUserId(
            requestedUserId,
            caller.UserId,
            caller.IsApiKey,
            context.User.IsInRole("Administrator"));
        return (userId, !caller.IsApiKey && userId == caller.UserId);
    }
}
