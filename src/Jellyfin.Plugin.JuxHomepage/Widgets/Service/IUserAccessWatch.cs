namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Drops a user's cached home screen after an administrator changed what the user may see
/// (<see cref="UserAccessWatch"/>).
/// </summary>
public interface IUserAccessWatch
{
    /// <summary>
    /// Reads the user's access when it was last read more than <see cref="UserAccessWatch.CheckInterval"/> ago, and
    /// drops their cached home screen when it changed.
    /// </summary>
    /// <param name="userId">The user.</param>
    void Check(Guid userId);
}
