using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Global plugin configuration serialized to XML by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the JellyUX Homepage plugin is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the global widget configuration list.
    /// Defines which widgets are enabled, their order, and their display settings.
    /// Users may override individual entries via <see cref="UserConfiguration.WidgetOverrides"/>
    /// when <see cref="WidgetConfig.AllowUserOverride"/> is true.
    /// </summary>
    public WidgetConfig[] Widgets { get; set; } = [];

    /// <summary>Gets or sets the per-module switches for the client scripts.</summary>
    public ModulesConfig Modules { get; set; } = new();

    /// <summary>Gets or sets the API keys for external data sources.</summary>
    public ApiKeysConfig ApiKeys { get; set; } = new();

    /// <summary>Gets or sets the cache tuning parameters for the widget engine.</summary>
    public CacheConfig Cache { get; set; } = new();

    /// <summary>Gets or sets the per-list TMDb pagination and region settings.</summary>
    public TMDbListsConfig TMDbLists { get; set; } = new();

    /// <summary>
    /// Gets or sets the configuration schema version. Used to gate one-time migrations applied at
    /// load time when the on-disk schema changes between plugin versions. See the "Configuration
    /// Schema Versioning" section in CLAUDE.md for the migration policy.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
