using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Computes, on demand, the integration problems the administrator must know about: a missing
/// FileTransformation plugin, or another installed plugin JellyUX cannot coexist with. Nothing is
/// persisted, so a warning disappears as soon as its cause is gone (TODO_V4.md 4.1, F-12).
/// </summary>
public class CoexistenceWarnings
{
    /// <summary>Warning code: FileTransformation is not loaded, nothing can be injected.</summary>
    public const string FileTransformationMissing = "FileTransformationMissing";

    /// <summary>Warning code: Custom Tabs is installed and competes for the home-html chunk.</summary>
    public const string CustomTabsInstalled = "CustomTabsInstalled";

    // CustomTabsPlugin.Id in jellyfin-plugin-custom-tabs. FileTransformation runs only one pipeline
    // per file pattern: both plugins patch the home-html chunk, so one of them loses its tabs.
    private static readonly Guid CustomTabsId = Guid.Parse("fbacd0b6-fd46-4a05-b0a4-2045d6a135b0");

    private readonly FileTransformationDetector _detector;
    private readonly IPluginManager _pluginManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoexistenceWarnings"/> class.
    /// </summary>
    /// <param name="detector">FileTransformation reflection bridge.</param>
    /// <param name="pluginManager">Jellyfin plugin manager, listing the installed plugins.</param>
    public CoexistenceWarnings(FileTransformationDetector detector, IPluginManager pluginManager)
    {
        _detector = detector;
        _pluginManager = pluginManager;
    }

    /// <summary>
    /// Returns the current warning codes, in display order.
    /// </summary>
    /// <returns>The warning codes; empty when JellyUX can run normally.</returns>
    public IReadOnlyList<string> Get() => Build(_detector.IsAvailable(), _pluginManager.Plugins);

    /// <summary>
    /// Builds the warning codes from the FileTransformation state and the installed plugins.
    /// </summary>
    /// <param name="fileTransformationAvailable">Whether FileTransformation is loaded.</param>
    /// <param name="plugins">The installed plugins.</param>
    /// <returns>The warning codes.</returns>
    public static IReadOnlyList<string> Build(bool fileTransformationAvailable, IEnumerable<LocalPlugin> plugins)
    {
        var warnings = new List<string>();
        if (!fileTransformationAvailable)
        {
            warnings.Add(FileTransformationMissing);
        }

        // Restart covers a Custom Tabs install pending the next restart, which will then take effect.
        if (plugins.Any(p => p.Id == CustomTabsId
            && p.Manifest.Status is PluginStatus.Active or PluginStatus.Restart))
        {
            warnings.Add(CustomTabsInstalled);
        }

        return warnings;
    }
}
