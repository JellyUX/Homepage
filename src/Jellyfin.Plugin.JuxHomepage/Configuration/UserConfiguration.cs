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
    /// Gets or sets user-specific widget overrides, frozen by QUAL-02: read from the file and carried
    /// by the migrations, ignored by the layout, written by no page. See
    /// CONTRIBUTING.md "Configuration Schema Versioning".
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
    /// Gets or sets the language of this user's last home screen (normalized, e.g. "fr"), so the
    /// home screen built ahead after a restart is the one the user will ask for (PERF-07, DEC-P07-10).
    /// Null until the first home screen; not a setting the user edits.
    /// </summary>
    public string? HomeLanguage { get; set; }

    /// <summary>
    /// Gets or sets the configuration schema version. See
    /// <see cref="PluginConfiguration.SchemaVersion"/> and CONTRIBUTING.md
    /// "Configuration Schema Versioning" for the migration policy.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
