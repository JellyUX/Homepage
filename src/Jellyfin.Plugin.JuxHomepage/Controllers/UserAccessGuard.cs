namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// Pure authorization decision for endpoints that accept a userId query parameter, to prevent an
/// authenticated user from reading another user's data by supplying an arbitrary userId (IDOR).
/// </summary>
internal static class UserAccessGuard
{
    /// <summary>
    /// Resolves whose data a request may read. A regular user always reads their own data: an omitted
    /// userId is taken from their token, and any other user's id is refused. An administrator may name
    /// any user (viewing another user's page) and defaults to themselves. A server API key is not tied
    /// to a user, so it must name one.
    /// </summary>
    /// <param name="requestedUserId">The userId supplied in the request query string, or <see cref="Guid.Empty"/>.</param>
    /// <param name="authenticatedUserId">The userId resolved from the request's authorization info.</param>
    /// <param name="isApiKey">Whether the request was authenticated via a server API key.</param>
    /// <param name="isAdministrator">Whether the authenticated user is an administrator.</param>
    /// <returns>The user whose data the request reads, or null when the request must be refused.</returns>
    internal static Guid? ResolveUserId(
        Guid requestedUserId,
        Guid authenticatedUserId,
        bool isApiKey,
        bool isAdministrator)
    {
        if (isApiKey)
        {
            return requestedUserId == Guid.Empty ? null : requestedUserId;
        }

        if (authenticatedUserId == Guid.Empty)
        {
            return null;
        }

        if (requestedUserId == Guid.Empty || requestedUserId == authenticatedUserId)
        {
            return authenticatedUserId;
        }

        return isAdministrator ? requestedUserId : null;
    }
}
