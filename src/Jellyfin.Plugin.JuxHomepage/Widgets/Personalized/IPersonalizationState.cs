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
}
