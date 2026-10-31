using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// Who sent a request and whose data it may read, shared by every controller that takes a userId
/// (see <see cref="UserAccessGuard.ResolveUserId"/>).
/// </summary>
public interface IRequestUserResolver
{
    /// <summary>
    /// Returns the authenticated user id and whether the request used a server API key
    /// (see <see cref="RequestCaller.ResolveAsync"/>).
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The caller's user id (empty for an API key) and whether it is an API key.</returns>
    Task<(Guid UserId, bool IsApiKey)> ResolveCallerAsync(HttpContext context);

    /// <summary>
    /// Resolves whose data the request reads: a regular user always reads their own data, whatever userId
    /// the query string carries, and is refused when it names someone else (IDOR); administrators may name
    /// any user. Also tells whether the caller reads their own data rather than an administrator or an API
    /// key reading someone else's.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="requestedUserId">The userId from the query string, or <see cref="Guid.Empty"/>.</param>
    /// <returns>The user to read, or null when the request must be refused, and whether it is the caller.</returns>
    Task<(Guid? UserId, bool IsCaller)> ResolveAsync(HttpContext context, Guid requestedUserId);
}
