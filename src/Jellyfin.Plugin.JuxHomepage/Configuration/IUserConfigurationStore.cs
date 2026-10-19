namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Reads and writes per-user <see cref="UserConfiguration"/> objects.
/// </summary>
public interface IUserConfigurationStore
{
    /// <summary>
    /// Reads the user configuration for the given user.
    /// </summary>
    /// <param name="userId">The Jellyfin user identifier.</param>
    /// <returns>The user configuration, or null if none has been saved yet.</returns>
    UserConfiguration? GetUserConfiguration(Guid userId);

    /// <summary>
    /// Persists the given user configuration.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <returns>False when the configuration could not be written.</returns>
    bool SaveUserConfiguration(UserConfiguration config);
}
