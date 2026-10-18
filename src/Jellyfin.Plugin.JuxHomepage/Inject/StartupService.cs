using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
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
    private static readonly TMDbCacheType[] TMDbCacheTypes =
    [
        TMDbCacheType.TrendingMovies,
        TMDbCacheType.TrendingShows,
        TMDbCacheType.AiringToday
    ];

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<StartupService> _logger;
    private readonly FileTransformationDetector _detector;
    private readonly ITMDbCacheService _tmdbCacheService;
    private readonly IFileSystem _fileSystem;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenRegistration _startedRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths, including WebPath.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="detector">FileTransformation reflection bridge.</param>
    /// <param name="tmdbCacheService">TMDb cache service, used to refresh a missing/stale cache immediately.</param>
    /// <param name="fileSystem">File system abstraction, used to locate and read the loadSections chunk.</param>
    /// <param name="lifetime">Host lifetime, used to defer the TMDb refresh until the server has started.</param>
    public StartupService(
        IApplicationPaths applicationPaths,
        ILogger<StartupService> logger,
        FileTransformationDetector detector,
        ITMDbCacheService tmdbCacheService,
        IFileSystem fileSystem,
        IHostApplicationLifetime lifetime)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
        _detector = detector;
        _tmdbCacheService = tmdbCacheService;
        _fileSystem = fileSystem;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterFileTransformationsIfAvailable();

        // Best-effort, not awaited, and only once the server has started (F-07): run during the boot,
        // its ~80 HTTP calls per list competed with Jellyfin's own startup. FileTransformation
        // registration above must never wait for it. The shutdown token stops it with the server.
        // RefreshStaleTMDbCacheAsync wraps its own body in try/catch and logs failures, so
        // discarding the task is safe.
        _startedRegistration = _lifetime.ApplicationStarted.Register(
            () => _ = RefreshStaleTMDbCacheAsync(_stopping.Token));

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _startedRegistration.Dispose();
        _stopping.Dispose();
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
    /// Immediately refreshes any TMDb cache type that is missing or older than the configured
    /// refresh interval, so a fresh install (or one that restarted after the daily 3 AM window was
    /// missed) does not have to wait for the next scheduled run. Best-effort: a failure here must
    /// never prevent the FileTransformation registration that follows.
    /// </summary>
    private async Task RefreshStaleTMDbCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var refreshed = 0;
            foreach (var type in TMDbCacheTypes)
            {
                if (!_tmdbCacheService.IsStale(type))
                {
                    continue;
                }

                await RefreshTMDbCacheType(type, cancellationToken).ConfigureAwait(false);
                refreshed++;
            }

            if (refreshed > 0)
            {
                _logger.LogInformation("Refreshed {Count} stale TMDb cache type(s) at startup.", refreshed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh TMDb cache at startup.");
        }
    }

    private Task RefreshTMDbCacheType(TMDbCacheType type, CancellationToken cancellationToken) => type switch
    {
        TMDbCacheType.TrendingMovies => _tmdbCacheService.RefreshTrendingMoviesAsync(cancellationToken),
        TMDbCacheType.TrendingShows => _tmdbCacheService.RefreshTrendingShowsAsync(cancellationToken),
        TMDbCacheType.AiringToday => _tmdbCacheService.RefreshAiringTodayAsync(cancellationToken),
        _ => Task.CompletedTask
    };

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
        TransformationPatches.CheckHomeHtmlChunks(_applicationPaths.WebPath, _fileSystem, _logger);

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
            return;
        }

        var fragmentTemplate = TransformationPatches.LoadInjectFragmentTemplate();

        foreach (var chunkPath in chunks)
        {
            var fileName = Path.GetFileName(chunkPath);

            WarnIfLoadSectionsHookHasDrifted(chunkPath, fileName, fragmentTemplate);

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
    }

    /// <summary>
    /// Runs a dry splice attempt against the detected chunk at startup so that a Jellyfin Web bundle
    /// drift (the minified module self-reference no longer resolving) is surfaced as an explicit log
    /// warning immediately, instead of silently falling back to native rendering and only being
    /// noticed later when the home page fails to show JellyUX sections.
    /// </summary>
    private void WarnIfLoadSectionsHookHasDrifted(string chunkPath, string fileName, string? fragmentTemplate)
    {
        if (fragmentTemplate is null)
        {
            return;
        }

        string content;
        try
        {
            content = _fileSystem.ReadAllText(chunkPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read chunk file for drift self-check: {File}", chunkPath);
            return;
        }

        var outcome = TransformationPatches.TryPatchLoadSections(content, fragmentTemplate).Outcome;
        if (outcome == LoadSectionsOutcome.HookNotFound)
        {
            _logger.LogWarning(
                "Chunk {FileName} contains ',loadSections:' but an expected minified token (module "
                + "self-reference, cardBuilder binding or its .default read) is missing. Jellyfin Web may "
                + "have drifted; the home page will fall back to native rendering. "
                + "See CONTRIBUTING.md 'Jellyfin Update Procedure'.",
                fileName);
        }
    }
}
