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

    /// <summary>
    /// Reads the user's configuration (a new one when none is saved), applies <paramref name="change"/>
    /// and saves it, under one lock: no other save can land between the read and the write. A file that
    /// exists but cannot be read is left unchanged; a corrupt one is replaced.
    /// </summary>
    /// <param name="userId">The Jellyfin user identifier.</param>
    /// <param name="change">Changes the configuration; returns false when there is nothing to save.</param>
    /// <returns>False when the configuration could not be read or written.</returns>
    bool UpdateUserConfiguration(Guid userId, Func<UserConfiguration, bool> change);
}
