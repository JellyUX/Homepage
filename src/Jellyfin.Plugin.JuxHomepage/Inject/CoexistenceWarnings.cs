using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Computes, on demand, the integration problems the administrator must know about: a missing or
/// outdated FileTransformation plugin, a home chunk JellyUX could not patch, or another installed
/// plugin JellyUX cannot coexist with. Nothing is persisted, so a warning disappears as soon as its
/// cause is gone (TODO_V4.md 4.1, F-12).
/// </summary>
public class CoexistenceWarnings
{
    /// <summary>
    /// Warning code: the configuration came back blank at startup while a saved copy exists; the admin
    /// page offers to restore it (FIAB-07).
    /// </summary>
    public const string ConfigurationReset = "ConfigurationReset";

    /// <summary>Warning code: FileTransformation is not loaded, nothing can be injected.</summary>
    public const string FileTransformationMissing = "FileTransformationMissing";

    /// <summary>Warning code: FileTransformation is older than 3.0, which revalidates index.html on every load.</summary>
    public const string FileTransformationOutdated = "FileTransformationOutdated";

    /// <summary>
    /// Warning code: JellyUX is disabled in the dashboard. Jellyfin 10.11 deletes a disabled plugin at
    /// the next restart when an active plugin sorts after it by name (FIAB-06).
    /// </summary>
    public const string JellyUXDisabled = "JellyUXDisabled";

    /// <summary>Warning code: FileTransformation is disabled, same deletion risk, and nothing is injected after it (FIAB-06).</summary>
    public const string FileTransformationDisabled = "FileTransformationDisabled";

    /// <summary>Warning code: the loadSections chunk drifted, the home page is native (FIAB-04).</summary>
    public const string HomeHookNotFound = "HomeHookNotFound";

    /// <summary>Warning code: no chunk holds ",loadSections:", the home page is native (FIAB-04).</summary>
    public const string HomeChunkMissing = "HomeChunkMissing";

    /// <summary>Warning code: the home-html chunk lacks the tab anchor, the JellyUX tabs are absent (FIAB-04).</summary>
    public const string TabsAnchorMissing = "TabsAnchorMissing";

    /// <summary>Warning code: Custom Tabs is installed and competes for the home-html chunk.</summary>
    public const string CustomTabsInstalled = "CustomTabsInstalled";

    // FileTransformation's plugin id (manifest of jellyfin-plugin-file-transformation, read on the
    // 8096 bench at 3.0.0.0). Its version comes from the manifest, the one the dashboard shows.
    private static readonly Guid FileTransformationId = Guid.Parse("5e87cc92-571a-4d8d-8d98-d2d4147f9f90");
    private static readonly Version FileTransformationMinimum = new(3, 0);

    // Plugin.Id: JellyUX reads its own status, which stays loaded until the restart that deletes it.
    private static readonly Guid JellyUXId = Guid.Parse("3adf1f1f-1541-4e47-b9e3-34d2d2968af6");

    // CustomTabsPlugin.Id in jellyfin-plugin-custom-tabs. FileTransformation runs only one pipeline
    // per file pattern: both plugins patch the home-html chunk, so one of them loses its tabs.
    private static readonly Guid CustomTabsId = Guid.Parse("fbacd0b6-fd46-4a05-b0a4-2045d6a135b0");

    private readonly FileTransformationDetector _detector;
    private readonly IPluginManager _pluginManager;
    private readonly PatchStatus _patchStatus;
    private readonly ConfigurationBackup _backup;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoexistenceWarnings"/> class.
    /// </summary>
    /// <param name="detector">FileTransformation reflection bridge.</param>
    /// <param name="pluginManager">Jellyfin plugin manager, listing the installed plugins.</param>
    /// <param name="patchStatus">The patch state the startup dry run found.</param>
    /// <param name="backup">The saved copy of the configuration.</param>
    /// <param name="fileSystem">File system abstraction, to read a plugin's manifest on disk.</param>
    public CoexistenceWarnings(FileTransformationDetector detector, IPluginManager pluginManager, PatchStatus patchStatus, ConfigurationBackup backup, IFileSystem fileSystem)
    {
        _detector = detector;
        _pluginManager = pluginManager;
        _patchStatus = patchStatus;
        _backup = backup;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Returns the current warning codes, in display order.
    /// </summary>
    /// <returns>The warning codes; empty when JellyUX can run normally.</returns>
    public IReadOnlyList<string> Get()
    {
        var plugins = _pluginManager.Plugins.ToList();

        // Disable writes Disabled to meta.json and leaves Restart in memory until the restart, as Enable
        // does: only the manifest on disk tells them apart (PluginManager.ProcessAlternative, 10.11.10).
        var disabledOnDisk = plugins
            .Where(p => p.Manifest.Status == PluginStatus.Restart && ManifestOnDiskSaysDisabled(p.Path, _fileSystem))
            .Select(p => p.Id)
            .ToHashSet();
        return Build(_detector.IsAvailable(), plugins, _patchStatus.LoadSections, _patchStatus.TabsAnchor, _backup.IsResetPending, disabledOnDisk);
    }

    /// <summary>
    /// Returns whether a plugin's <c>meta.json</c> says Disabled: what Disable wrote while the status in
    /// memory reads Restart.
    /// </summary>
    /// <param name="pluginPath">The plugin's folder.</param>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <returns>True when the manifest on disk is Disabled; false when it is not, or cannot be read.</returns>
    internal static bool ManifestOnDiskSaysDisabled(string? pluginPath, IFileSystem fileSystem)
    {
        if (string.IsNullOrEmpty(pluginPath))
        {
            return false;
        }

        try
        {
            using var manifest = JsonDocument.Parse(fileSystem.ReadAllText(Path.Combine(pluginPath, "meta.json")));
            return manifest.RootElement.EnumerateObject().Any(p =>
                p.NameEquals("status") && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == nameof(PluginStatus.Disabled));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the warning codes from the FileTransformation state, the patch state and the installed plugins.
    /// </summary>
    /// <param name="fileTransformationAvailable">Whether FileTransformation is loaded.</param>
    /// <param name="plugins">The installed plugins.</param>
    /// <param name="loadSections">The loadSections chunk state.</param>
    /// <param name="tabsAnchor">Whether the home-html chunk holds the tab anchor; null when not checked.</param>
    /// <param name="configurationReset">Whether a blank configuration replaced one that has a saved copy.</param>
    /// <param name="disabledOnDisk">The plugins disabled since the last restart (Restart in memory, Disabled on disk).</param>
    /// <returns>The warning codes.</returns>
    public static IReadOnlyList<string> Build(
        bool fileTransformationAvailable,
        IEnumerable<LocalPlugin> plugins,
        LoadSectionsPatchState loadSections,
        bool? tabsAnchor,
        bool configurationReset,
        IReadOnlySet<Guid>? disabledOnDisk = null)
    {
        var installed = plugins.ToList();
        bool IsDisabled(LocalPlugin p) => p.Manifest.Status == PluginStatus.Disabled || disabledOnDisk?.Contains(p.Id) == true;

        var warnings = new List<string>();
        if (configurationReset)
        {
            warnings.Add(ConfigurationReset);
        }

        if (!fileTransformationAvailable)
        {
            warnings.Add(FileTransformationMissing);
        }
        else if (installed.Any(p => p.Id == FileTransformationId && p.Version < FileTransformationMinimum))
        {
            warnings.Add(FileTransformationOutdated);
        }

        // Only the status is read, not Jellyfin's name order (DEC-P05-7): the order is internal to
        // DiscoverPlugins and the message says the deletion happens if an active plugin sorts after.
        if (installed.Any(p => p.Id == JellyUXId && IsDisabled(p)))
        {
            warnings.Add(JellyUXDisabled);
        }

        if (installed.Any(p => p.Id == FileTransformationId && IsDisabled(p)))
        {
            warnings.Add(FileTransformationDisabled);
        }

        if (loadSections == LoadSectionsPatchState.HookNotFound)
        {
            warnings.Add(HomeHookNotFound);
        }
        else if (loadSections == LoadSectionsPatchState.NoChunk)
        {
            warnings.Add(HomeChunkMissing);
        }

        if (tabsAnchor == false)
        {
            warnings.Add(TabsAnchorMissing);
        }

        // Restart covers a Custom Tabs install pending the next restart, which will then take effect, but not
        // a Custom Tabs just disabled, which reads Restart too.
        if (installed.Any(p => p.Id == CustomTabsId
            && p.Manifest.Status is PluginStatus.Active or PluginStatus.Restart
            && !IsDisabled(p)))
        {
            warnings.Add(CustomTabsInstalled);
        }

        return warnings;
    }
}
