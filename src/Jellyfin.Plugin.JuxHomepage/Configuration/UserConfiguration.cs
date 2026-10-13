using Jellyfin.Plugin.JuxHomepage.Widgets;

namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Per-user configuration stored as JSON on disk.
/// Allows users to override the global widget settings with their own preferences.
/// </summary>
public sealed class UserConfiguration
{
    /// <summary>Gets or sets the Jellyfin user this configuration belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets user-specific widget overrides.
    /// Each entry overrides the matching global <see cref="PluginConfiguration.Widgets"/> entry
    /// for widgets where <see cref="WidgetConfig.AllowUserOverride"/> is true.
    /// </summary>
    public WidgetConfig[] WidgetOverrides { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the user has explicitly enabled or disabled JellyUX for themselves
    /// (TODO_V4.md 8.3, F-36). When null, the global <see cref="PluginConfiguration.Enabled"/> setting
    /// applies; false hands the home screen, the tabs and every card add-on back to Jellyfin for this
    /// user only.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the keys of the home sections this user hid (TODO_V4.md 8.3, F-36), as built by
    /// <see cref="WidgetLayoutResolver.SectionKeys"/>. A hidden section keeps following the
    /// administrator's configuration; only its display is removed for this user.
    /// </summary>
    public string[] HiddenSections { get; set; } = [];

    /// <summary>
    /// Gets or sets the configuration schema version. See
    /// <see cref="PluginConfiguration.SchemaVersion"/> and the "Configuration Schema Versioning"
    /// section in CLAUDE.md for the migration policy.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
