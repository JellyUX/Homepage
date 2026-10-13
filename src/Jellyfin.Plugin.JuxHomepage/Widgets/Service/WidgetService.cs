using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Public API surface and session cache for the JellyUX home screen widget engine.
/// <para>
/// <see cref="GetWidgetsForUser"/> returns the cached widget layout for a user, rebuilding it via
/// <see cref="WidgetLayoutResolver"/> on a cache miss and storing the result in
/// <see cref="SessionCache"/>. Configuration resolution (merging global config with per-user
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

    private readonly IWidgetRegistry _registry;
    private readonly SessionCache _sessionCache;
    private readonly WidgetLayoutResolver _layoutResolver;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<WidgetService> _logger;
    private readonly IPersonalizationState? _personalizationState;

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
    /// Results are cached per-user; the cache is invalidated when the TTL expires.
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
        var ttlMinutes = _getConfiguration()?.Cache?.SessionTtlMinutes ?? 15;
        var ttl = TimeSpan.FromMinutes(ttlMinutes);

        if (_sessionCache.TryGet(userId, lang, ttl, out var cached) && cached is not null)
        {
            return Paginate(cached, page);
        }

        var descriptors = await _layoutResolver.BuildDescriptors(userId, lang, cancellationToken).ConfigureAwait(false);
        if (_personalizationState?.TakePendingServed(userId) != true)
        {
            _sessionCache.Set(userId, lang, descriptors);
        }

        return Paginate(descriptors, page);
    }

    /// <summary>
    /// Fetches items for a specific widget on demand.
    /// Called by <c>GET /JuxHomepage/Section/{widgetType}</c> when the front end loads a section.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="widgetType">The widget type identifier.</param>
    /// <param name="additionalData">Optional instance-specific data (e.g. library ID).</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="WidgetResult"/>, or null if the widget type is not registered.</returns>
    public async Task<WidgetResult?> GetWidgetItems(
        Guid userId,
        string widgetType,
        string? additionalData,
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
        var configRow = _layoutResolver.ResolveConfigRow(userId, widgetType, additionalData);
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
