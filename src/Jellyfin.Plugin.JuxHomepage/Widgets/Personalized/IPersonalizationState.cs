namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Tells the layout cache whether a layout was built while a user's first scoring computation was
/// still running, in which case its personalized sections are missing and it must not be cached.
/// </summary>
public interface IPersonalizationState
{
    /// <summary>
    /// Returns whether an empty, still-computing result was served to <paramref name="userId"/>
    /// since the last call, and clears that flag.
    /// </summary>
    /// <param name="userId">The user whose layout was just built.</param>
    /// <returns>True if the layout lacks personalized sections that will be ready later.</returns>
    bool TakePendingServed(Guid userId);

    /// <summary>
    /// Computes the user's scores ahead of a layout computation, waiting for the full ones (DR-033): a layout computed
    /// after it reads fresh scores, is never one served before the scoring finished, and is not made due at once by a
    /// scoring it started.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Stops waiting; the computation itself goes on.</param>
    /// <returns>True when fresh scores are held for the user; false when the scoring queue is full.</returns>
    Task<bool> WarmAsync(Guid userId, CancellationToken cancellationToken);
}
