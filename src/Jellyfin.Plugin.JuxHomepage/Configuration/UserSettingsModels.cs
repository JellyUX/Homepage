namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// A user's own JellyUX settings, as <c>GET JuxHomepage/UserSettings</c> returns them (TODO_V4.md 8.3).
/// </summary>
/// <param name="Enabled">Whether JellyUX runs for the user.</param>
/// <param name="Sections">The home sections the user may hide, in home screen order.</param>
public sealed record UserSettingsView(bool Enabled, IReadOnlyList<UserSectionSetting> Sections);

/// <summary>One home section of <see cref="UserSettingsView"/>.</summary>
/// <param name="Key">The section key stored in <see cref="UserConfiguration.HiddenSections"/>.</param>
/// <param name="Name">The section name shown to the user.</param>
/// <param name="Hidden">Whether the user hid it.</param>
public sealed record UserSectionSetting(string Key, string Name, bool Hidden);

/// <summary>
/// The body of <c>POST JuxHomepage/UserSettings</c>.
/// </summary>
public sealed class UserSettingsUpdate
{
    /// <summary>Gets or sets a value indicating whether JellyUX runs for the user.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the keys of the sections the user hides.</summary>
    public string[]? HiddenSections { get; set; }
}
