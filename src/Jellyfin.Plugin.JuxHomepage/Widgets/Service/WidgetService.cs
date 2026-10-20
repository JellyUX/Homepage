using System.Collections.Concurrent;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Public API surface and session cache for the JellyUX home screen widget engine.
/// <para>
/// <see cref="GetWidgetsForUser"/> returns the cached widget layout for a user, building it via
/// <see cref="WidgetLayoutResolver"/> on a cache miss (one build per user and language at a time) and
/// storing the result in <see cref="SessionCache"/>; an expired layout is served while it is rebuilt
/// in the background (PERF-01). Configuration resolution (merging global config with per-user
/// overrides, sorting, fan-out rank, MinItems filtering) lives entirely in
/// <see cref="WidgetLayoutResolver"/> -- this class owns only the public API and the cache.
/// </para>
/// <para>
/// <see cref="GetWidgetItems"/> fetches items for a specific widget on demand (called per section
/// by the front end).
/// </para>
/// </summary>
public sealed class WidgetService
{
    private const int PageSize = 20;

    /// <summary>Maximum number of expired layouts waiting for their background rebuild.</summary>
    private const int MaxQueuedRebuilds = 8;

    private readonly ConcurrentDictionary<(Guid UserId, string Lang), (long Version, Lazy<Task<IReadOnlyList<WidgetDescriptor>>> Build)> _builds = new();
    private readonly ConcurrentDictionary<(Guid UserId, string Lang), byte> _queuedRebuilds = new();
    private readonly ConcurrentDictionary<Task, byte> _backgroundRebuilds = new();
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);
    private readonly IWidgetRegistry _registry;
    private readonly SessionCache _sessionCache;
    private readonly WidgetLayoutResolver _layoutResolver;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<WidgetService> _logger;
    private readonly IPersonalizationState? _personalizationState;
    private int _queuedRebuildCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetService"/> class.
    /// </summary>
    /// <param name="registry">The widget registry, used for the direct lookup in <see cref="GetWidgetItems"/>.</param>
    /// <param name="sessionCache">The session layout cache.</param>
    /// <param name="layoutResolver">Resolves the effective widget layout configuration for a user.</param>
    /// <param name="getConfiguration">
    /// Factory that returns the current plugin configuration, used here only for the session cache TTL.
    /// Defaults to <c>Plugin.Instance?.Configuration</c> in production.
    /// </param>
    /// <param name="logger">Logger.</param>
    /// <param name="personalizationState">
    /// Reports a layout built before the user's first scoring finished, which is then not cached so
    /// the personalized sections appear on the next load. Null disables the check.
    /// </param>
    public WidgetService(
        IWidgetRegistry registry,
        SessionCache sessionCache,
        WidgetLayoutResolver layoutResolver,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<WidgetService> logger,
        IPersonalizationState? personalizationState = null)
    {
        _registry = registry;
        _sessionCache = sessionCache;
        _layoutResolver = layoutResolver;
        _getConfiguration = getConfiguration;
        _logger = logger;
        _personalizationState = personalizationState;
    }

    /// <summary>
    /// Returns the ordered list of widget descriptors for the user's home screen.
    /// Widgets that return fewer items than their MinItems threshold are excluded.
    /// Results are cached per user and language; past the TTL the cached layout is still served and a
    /// rebuild is queued in the background.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="page">Zero-based page index (each page contains up to <c>20</c> descriptors).</param>
    /// <param name="lang">
    /// The requested language code (e.g. "fr"), or null for the default (English). Determines the
    /// language of translated widget display names and is part of the session cache key, so each
    /// language gets its own cached layout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of the home screen layout, and whether a later page exists.</returns>
    public async Task<SectionsPage> GetWidgetsForUser(
        Guid userId,
        int page,
        string? lang,
        CancellationToken cancellationToken)
    {
        if (_sessionCache.TryGet(userId, lang, SessionTtl, out var cached, out var expired) && cached is not null)
        {
            if (expired)
            {
                QueueBackgroundRebuild(userId, lang);
            }

            return Paginate(cached, page);
        }

        var descriptors = await BuildOnce(userId, lang).WaitAsync(cancellationToken).ConfigureAwait(false);
        return Paginate(descriptors, page);
    }

    /// <summary>
    /// Test-only seam: completes once every queued background rebuild has finished.
    /// </summary>
    /// <returns>A task that completes when no background rebuild is left.</returns>
    internal Task WhenIdleForTesting() => Task.WhenAll(_backgroundRebuilds.Keys);

    private TimeSpan SessionTtl => TimeSpan.FromMinutes(_getConfiguration()?.Cache?.SessionTtlMinutes ?? 15);

    /// <summary>
    /// Builds the user's layout, or joins the build already running for the same user and language,
    /// so simultaneous misses cost one build (<c>Sections_SimultaneousMisses_BuildOnce</c>). A build
    /// started before the cache was cleared or invalidated is not joined: it read the old
    /// configuration (<c>Sections_MissAfterClear_DoesNotJoinTheBuildStartedBefore</c>). The shared
    /// build ignores any one caller's cancellation; each caller stops waiting on its own.
    /// </summary>
    private Task<IReadOnlyList<WidgetDescriptor>> BuildOnce(Guid userId, string? lang)
    {
        var key = (userId, SessionCache.NormalizeKey(lang));
        var version = _sessionCache.VersionOf(userId);
        return _builds.AddOrUpdate(
            key,
            _ => NewBuild(key, lang, version),
            (_, running) => running.Version == version ? running : NewBuild(key, lang, version)).Build.Value;
    }

    private (long Version, Lazy<Task<IReadOnlyList<WidgetDescriptor>>> Build) NewBuild((Guid UserId, string Lang) key, string? lang, long version) =>
        (version, new Lazy<Task<IReadOnlyList<WidgetDescriptor>>>(() => BuildAndStore(key, lang, version)));

    private async Task<IReadOnlyList<WidgetDescriptor>> BuildAndStore((Guid UserId, string Lang) key, string? lang, long version)
    {
        try
        {
            var descriptors = await _layoutResolver.BuildDescriptors(key.UserId, lang, CancellationToken.None).ConfigureAwait(false);

            // A layout built before the user's first scoring finished is served, never cached, so an
            // expired one is never served in its place either (Sections_PendingScoring_IsNotCachedNorServedStale).
            if (_personalizationState?.TakePendingServed(key.UserId) != true)
            {
                _sessionCache.Set(key.UserId, lang, descriptors, version);
            }

            return descriptors;
        }
        finally
        {
            if (_builds.TryGetValue(key, out var current) && current.Version == version)
            {
                _builds.TryRemove(KeyValuePair.Create(key, current));
            }
        }
    }

    /// <summary>
    /// Rebuilds an expired layout in the background (PERF-01), one rebuild at a time server-wide and at
    /// most <see cref="MaxQueuedRebuilds"/> queued, like the scoring queue: when the queue is full the
    /// expired layout is served as is and a later request queues the rebuild again
    /// (<c>Sections_AfterTtl_ServesPreviousLayoutAndRebuildsOnce</c>, <c>Sections_RebuildQueueFull_ServesExpiredWithoutQueueing</c>).
    /// </summary>
    private void QueueBackgroundRebuild(Guid userId, string? lang)
    {
        var key = (userId, SessionCache.NormalizeKey(lang));
        if (_builds.ContainsKey(key) || !_queuedRebuilds.TryAdd(key, 0))
        {
            return;
        }

        if (Interlocked.Increment(ref _queuedRebuildCount) > MaxQueuedRebuilds)
        {
            Interlocked.Decrement(ref _queuedRebuildCount);
            _queuedRebuilds.TryRemove(key, out _);
            _logger.LogDebug("Layout rebuild queue full; serving the expired layout of user {UserId} as is.", userId);
            return;
        }

        var rebuild = Task.Run(async () =>
        {
            try
            {
                await _rebuildGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_sessionCache.TryGet(userId, lang, SessionTtl, out _, out var stillExpired) && stillExpired)
                    {
                        await BuildOnce(userId, lang).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _rebuildGate.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild the home layout of user {UserId} in the background.", userId);
            }
            finally
            {
                _queuedRebuilds.TryRemove(key, out _);
                Interlocked.Decrement(ref _queuedRebuildCount);
            }
        });
        _backgroundRebuilds.TryAdd(rebuild, 0);
        _ = rebuild.ContinueWith(t => _backgroundRebuilds.TryRemove(t, out _), TaskScheduler.Default);
    }

    /// <summary>
    /// Fetches items for a specific widget on demand.
    /// Called by <c>GET /JuxHomepage/Section/{widgetType}</c> when the front end loads a section.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="widgetType">The widget type identifier.</param>
    /// <param name="additionalData">Optional instance-specific data (e.g. library ID).</param>
    /// <param name="sectionKey">The section's key from the layout, naming its configuration row (F-27).</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="WidgetResult"/>, or null if the widget type is not registered.</returns>
    public async Task<WidgetResult?> GetWidgetItems(
        Guid userId,
        string widgetType,
        string? additionalData,
        string? sectionKey,
        int startIndex,
        int limit,
        CancellationToken cancellationToken)
    {
        var widget = _registry.GetByType(widgetType);
        if (widget is null)
        {
            return null;
        }

        // Forward the configured ExtraParams (e.g. "excludeWatched" for personalized widgets, or a
        // Discover instance's filter parameters) so on-demand section fetches honor the same
        // settings as the layout probe.
        var configRow = _layoutResolver.ResolveConfigRow(userId, widgetType, additionalData, sectionKey);
        var extra = configRow?.GetExtraParamsDictionary();

        // A section never returns more than its configured MaxItems (TODO_V4.md 5.5, F-21).
        var maxItems = Math.Clamp(configRow?.MaxItems ?? WidgetConfig.MaxItemsCeiling, 1, WidgetConfig.MaxItemsCeiling);

        var payload = new WidgetPayload
        {
            UserId = userId,
            AdditionalData = additionalData,
            StartIndex = Math.Max(0, startIndex),
            Limit = Math.Clamp(limit, 1, maxItems),
            ExtraParams = extra,
            SortBy = configRow?.SortBy ?? WidgetSort.SortName,
            SortOrder = configRow?.SortOrder ?? WidgetSort.Ascending,
            MaxItems = maxItems
        };

        try
        {
            return await widget.GetItemsAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Widget '{WidgetType}' threw an exception during GetWidgetItems for user {UserId}.", widgetType, userId);
            return WidgetResult.Empty;
        }
    }

    /// <summary>
    /// Returns whether JellyUX runs for the user: enabled globally and not turned off by the user
    /// (TODO_V4.md 8.3, F-36).
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>True when JellyUX runs for this user.</returns>
    public bool IsEnabledFor(Guid userId) => _layoutResolver.IsEnabledFor(userId);

    /// <summary>
    /// Drops the cached layout of a user whose own settings changed, so the next home screen shows
    /// them at once.
    /// </summary>
    /// <param name="userId">The user.</param>
    public void InvalidateUser(Guid userId) => _sessionCache.Invalidate(userId);

    internal static SectionsPage Paginate(IReadOnlyList<WidgetDescriptor> all, int page)
    {
        var skip = Math.Max(0, page) * PageSize;
        return new SectionsPage(all.Skip(skip).Take(PageSize).ToList().AsReadOnly(), skip + PageSize < all.Count);
    }
}
