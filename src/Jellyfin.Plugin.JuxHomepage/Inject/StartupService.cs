using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Hosted service that runs once at server startup to register web transformations and detect
/// required Jellyfin assets. Registered explicitly via <c>AddHostedService</c> in
/// <see cref="PluginServiceRegistrator"/> (not auto-discovered -- unlike an <c>IScheduledTask</c>,
/// an <see cref="IHostedService"/> does not show up in Dashboard &gt; Scheduled Tasks).
/// </summary>
public sealed class StartupService : IHostedService, IDisposable
{
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<StartupService> _logger;
    private readonly FileTransformationDetector _detector;
    private readonly ITMDbCacheService _tmdbCacheService;
    private readonly IFileSystem _fileSystem;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly PatchStatus _patchStatus;
    private readonly ConfigurationBackup _backup;
    private readonly ServerStartedRun _afterStart = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths, including WebPath.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="detector">FileTransformation reflection bridge.</param>
    /// <param name="tmdbCacheService">TMDb cache service, used to refresh a missing/stale cache immediately.</param>
    /// <param name="fileSystem">File system abstraction, used to locate and read the loadSections chunk.</param>
    /// <param name="lifetime">Host lifetime, used to defer the TMDb refresh until the server has started.</param>
    /// <param name="patchStatus">Receives the patch state the dry runs find (FIAB-04).</param>
    /// <param name="backup">The saved copy of the configuration, offered after a reset (FIAB-07).</param>
    public StartupService(
        IApplicationPaths applicationPaths,
        ILogger<StartupService> logger,
        FileTransformationDetector detector,
        ITMDbCacheService tmdbCacheService,
        IFileSystem fileSystem,
        IHostApplicationLifetime lifetime,
        PatchStatus patchStatus,
        ConfigurationBackup backup)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
        _detector = detector;
        _tmdbCacheService = tmdbCacheService;
        _fileSystem = fileSystem;
        _lifetime = lifetime;
        _patchStatus = patchStatus;
        _backup = backup;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterFileTransformationsIfAvailable();
        WarnRewardsRowsWithoutQid(Plugin.Instance?.Configuration, _logger);
        OfferBackupIfConfigurationLoadedBlank(Plugin.Instance?.ConfigurationLoadedBlank == true, _backup, _logger);

        // Best-effort, not awaited, and only once the server has started (F-07): run during the boot,
        // its ~80 HTTP calls per list competed with Jellyfin's own startup. FileTransformation
        // registration above must never wait for it. The shutdown token stops it with the server.
        // RefreshStaleTMDbCacheAsync wraps its own body in try/catch and logs failures, so
        // discarding the task is safe.
        _afterStart.Register(_lifetime, RefreshStaleTMDbCacheAsync);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _afterStart.Stop();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose() => _afterStart.Dispose();

    /// <summary>
    /// Logs each Rewards row that lost its Q-id, as an admin Save before FIAB-02 left it: the row loads
    /// and shows nothing, and the admin page refuses to save it until a ceremony or category is set.
    /// </summary>
    /// <param name="config">The loaded configuration, or null when the plugin is not loaded.</param>
    /// <param name="logger">Logger.</param>
    internal static void WarnRewardsRowsWithoutQid(PluginConfiguration? config, ILogger logger)
    {
        if (config is null)
        {
            return;
        }

        foreach (var row in Plugin.RewardsRowsWithoutQid(config))
        {
            logger.LogWarning(
                "Rewards section '{InstanceId}' has no ceremony or category and shows nothing: set one on the JellyUX admin page.",
                row.GetExtraParamsDictionary().GetValueOrDefault("value"));
        }
    }

    /// <summary>
    /// Records a blank configuration found at load when a saved copy exists, so the admin page offers it
    /// (FIAB-07), and logs the offer while it stands.
    /// </summary>
    /// <param name="loadedBlank">Whether the configuration loaded blank.</param>
    /// <param name="backup">The saved copy.</param>
    /// <param name="logger">Logger.</param>
    internal static void OfferBackupIfConfigurationLoadedBlank(bool loadedBlank, ConfigurationBackup backup, ILogger logger)
    {
        if (loadedBlank)
        {
            backup.MarkResetIfBackupExists();
        }

        if (backup.IsResetPending)
        {
            logger.LogWarning(
                "The JellyUX configuration file could not be read and was replaced by a blank one. A copy of the last "
                + "saved configuration exists: restore it from the warning on the JellyUX admin page.");
        }
    }

    private void RegisterFileTransformationsIfAvailable()
    {
        if (!_detector.IsAvailable())
        {
            _logger.LogError(
                "FileTransformation plugin is not installed. "
                + "JellyUX Homepage cannot inject resources into index.html. "
                + "Install jellyfin-plugin-file-transformation and restart Jellyfin.");
            return;
        }

        RegisterIndexHtmlTransformation();
        RegisterLoadSectionsTransformations();
        RegisterHomeHtmlTransformation();
    }

    /// <summary>
    /// Immediately refreshes the TMDb lists and Discover instances that are missing or due, so a
    /// fresh install does not wait up to an hour for the next scheduled run. Runs once the server has
    /// started, after the FileTransformation registration of <see cref="StartAsync"/>; best-effort, a
    /// failure is logged only.
    /// </summary>
    private async Task RefreshStaleTMDbCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _tmdbCacheService.RefreshDueAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh TMDb cache at startup.");
        }
    }

    private void RegisterIndexHtmlTransformation()
    {
        var payload = new JObject
        {
            ["id"] = Guid.Parse("3adf1f1f-1541-4e47-b9e3-34d2d2968af6"),
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = GetType().Assembly.FullName,
            ["callbackClass"] = typeof(TransformationPatches).FullName,
            ["callbackMethod"] = nameof(TransformationPatches.IndexHtml)
        };

        _detector.RegisterTransformation(payload);
        _logger.LogInformation("Registered index.html transformation for JellyUX Homepage.");
    }

    /// <summary>
    /// Registers the home-html chunk transformation that splices in the 4 JellyUX tab content panes
    /// (Watchlist/Progress/History/Statistics, TODO_V3.md Phase 4.1). Unlike loadSections, this chunk
    /// is identified by a stable filename prefix ("home-html") that does not change between Jellyfin
    /// Web builds -- only the content hash does -- so no dynamic discovery is needed, matching the
    /// approach already validated by the published jellyfin-plugin-custom-tabs plugin.
    /// </summary>
    private void RegisterHomeHtmlTransformation()
    {
        _patchStatus.SetTabsAnchor(TransformationPatches.CheckHomeHtmlChunks(_applicationPaths.WebPath, _fileSystem, _logger));

        var payload = new JObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["fileNamePattern"] = "home-html\\.[^.]+\\.chunk\\.js",
            ["callbackAssembly"] = GetType().Assembly.FullName,
            ["callbackClass"] = typeof(TransformationPatches).FullName,
            ["callbackMethod"] = nameof(TransformationPatches.HomeHtmlChunk)
        };

        _detector.RegisterTransformation(payload);
        _logger.LogInformation("Registered home-html chunk transformation for JellyUX Homepage tabs.");
    }

    private void RegisterLoadSectionsTransformations()
    {
        var chunks = TransformationPatches.FindLoadSectionsChunks(_applicationPaths.WebPath, _fileSystem, _logger);

        if (chunks.Count == 0)
        {
            _patchStatus.SetLoadSections(LoadSectionsPatchState.NoChunk);
            return;
        }

        var fragmentTemplate = TransformationPatches.LoadInjectFragmentTemplate();
        var outcomes = new List<LoadSectionsOutcome>();

        foreach (var chunkPath in chunks)
        {
            var fileName = Path.GetFileName(chunkPath);

            if (WarnIfLoadSectionsHookHasDrifted(chunkPath, fileName, fragmentTemplate, _fileSystem, _logger) is { } outcome)
            {
                outcomes.Add(outcome);
            }

            // Build a regex pattern that matches the same chunk regardless of content-hash
            // e.g. "56213.a6cde3c8ba80d7030952.chunk.js" -> "56213\.[^.]+\.chunk\.js"
            var nameParts = fileName.Split('.');
            var prefix = nameParts.Length >= 3 ? nameParts[0] : fileName;
            var fileNamePattern = $"{prefix}\\.[^.]+\\.chunk\\.js";

            var chunkId = Guid.NewGuid();
            var payload = new JObject
            {
                ["id"] = chunkId.ToString(),
                ["fileNamePattern"] = fileNamePattern,
                ["callbackAssembly"] = GetType().Assembly.FullName,
                ["callbackClass"] = typeof(TransformationPatches).FullName,
                ["callbackMethod"] = nameof(TransformationPatches.PatchLoadSections)
            };

            _detector.RegisterTransformation(payload);
            _logger.LogInformation(
                "Registered loadSections chunk transformation for {FileName} (pattern: {Pattern}, id: {Id}).",
                fileName,
                fileNamePattern,
                chunkId);
        }

        // An unreadable chunk or a missing fragment leaves nothing known: the guard then never reloads.
        _patchStatus.SetLoadSections(fragmentTemplate is null || outcomes.Count == 0
            ? LoadSectionsPatchState.Unknown
            : PatchStatus.Combine(outcomes));
    }

    /// <summary>
    /// Runs a dry splice attempt against the detected chunk at startup so that a Jellyfin Web bundle
    /// drift (the minified module self-reference no longer resolving) is surfaced as an explicit log
    /// warning immediately, instead of silently falling back to native rendering and only being
    /// noticed later when the home page fails to show JellyUX sections.
    /// </summary>
    /// <param name="chunkPath">The chunk to check.</param>
    /// <param name="fileName">The chunk's file name, for the log.</param>
    /// <param name="fragmentTemplate">The inject fragment template, or null when it is missing.</param>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The splice outcome, or null when the chunk or the fragment could not be read.</returns>
    internal static LoadSectionsOutcome? WarnIfLoadSectionsHookHasDrifted(
        string chunkPath,
        string fileName,
        string? fragmentTemplate,
        IFileSystem fileSystem,
        ILogger logger)
    {
        if (fragmentTemplate is null)
        {
            return null;
        }

        string content;
        try
        {
            content = fileSystem.ReadAllText(chunkPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read chunk file for drift self-check: {File}", chunkPath);
            return null;
        }

        var outcome = TransformationPatches.TryPatchLoadSections(content, fragmentTemplate).Outcome;
        if (outcome == LoadSectionsOutcome.HookNotFound)
        {
            logger.LogWarning(
                "Chunk {FileName} contains ',loadSections:' but an expected minified token (module "
                + "self-reference, cardBuilder binding or its .default read) is missing. Jellyfin Web may "
                + "have drifted; the home page will fall back to native rendering. "
                + "See CONTRIBUTING.md 'Jellyfin Update Procedure'.",
                fileName);
        }

        return outcome;
    }
}
