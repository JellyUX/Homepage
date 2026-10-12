using System;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage;

/// <summary>
/// Runs the body of a Jellyfin event handler. Jellyfin raises its events synchronously inside its own
/// operations (e.g. <c>UserDataSaved</c> inside <c>SaveUserData</c>), so an exception escaping a
/// JellyUX handler would fail the server operation that raised it (TODO_V4.md 5.5, F-23): it is
/// logged instead.
/// </summary>
internal static class EventHandlerGuard
{
    /// <summary>
    /// Runs <paramref name="body"/>, logging any exception it throws instead of propagating it.
    /// </summary>
    /// <param name="logger">The logger of the handling class.</param>
    /// <param name="handler">The handler name, for the log.</param>
    /// <param name="body">The handler body.</param>
    public static void Run(ILogger logger, string handler, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JellyUX event handler {Handler} failed.", handler);
        }
    }
}
