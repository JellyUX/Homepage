using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Resolves the effective widget layout configuration for a user, independently of runtime
/// orchestration (cache, item fetching). Extracted from <see cref="WidgetService"/> (TODO_V2.md
/// Phase 7.1) so configuration resolution -- merging global config with per-user overrides, sorting,
/// fan-out rank -- has a single well-scoped owner, separate from <see cref="WidgetService"/>'s public
/// API and session cache.
/// </summary>
public sealed class WidgetLayoutResolver
{
    private readonly IWidgetRegistry _registry;
    private readonly IUserConfigurationStore _userConfigStore;
    private readonly ILocalizationService _localizationService;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<WidgetLayoutResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetLayoutResolver"/> class.
    /// </summary>
    /// <param name="registry">The widget registry.</param>
    /// <param name="userConfigStore">The per-user configuration store.</param>
    /// <param name="localizationService">Widget display-name translation service.</param>
    /// <param name="getConfiguration">
    /// Factory that returns the current plugin configuration.
    /// Defaults to <c>Plugin.Instance?.Configuration</c> in production.
    /// </param>
    /// <param name="logger">Logger.</param>
    public WidgetLayoutResolver(
        IWidgetRegistry registry,
        IUserConfigurationStore userConfigStore,
        ILocalizationService localizationService,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<WidgetLayoutResolver> logger)
    {
        _registry = registry;
        _userConfigStore = userConfigStore;
        _localizationService = localizationService;
        _getConfiguration = getConfiguration;
        _logger = logger;
    }

    /// <summary>
    /// Builds the ordered, MinItems-filtered list of widget descriptors for a user's home screen,
    /// merging global plugin configuration with the user's own overrides.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="lang">The requested language code (e.g. "fr"), or null for the default (English).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordered list of descriptors, not yet paginated.</returns>
    public async Task<IReadOnlyList<WidgetDescriptor>> BuildDescriptors(
        Guid userId,
        string? lang,
        CancellationToken cancellationToken)
    {
        var globalConfig = _getConfiguration()?.Widgets ?? [];
        var userConfig = _userConfigStore.GetUserConfiguration(userId);
        var overrides = userConfig?.WidgetOverrides ?? [];

        // A section the user hid (TODO_V4.md 8.3, F-36) is left out by the key of its global row.
        // Hiding changes none of the row's settings, so it does not depend on AllowUserOverride.
        var hidden = new HashSet<string>(userConfig?.HiddenSections ?? [], StringComparer.Ordinal);
        var keys = SectionKeys(globalConfig);
        var effectiveConfigs = globalConfig
            .OrderBy(g => g.Order)
            .Zip(MergeOverrides(globalConfig, overrides))
            .Where(pair => !hidden.Contains(keys[pair.First]))
            .Select(pair => pair.Second)
            .Where(c => c.Enabled)
            .OrderBy(c => c.Order)
            .ToList();

        // Assign a 1-indexed rank to each row within its WidgetType group, ordered by Order --
        // replaces the old MaxInstances fan-out count (TODO_V2.md Phase 8.2). Every row gets a rank,
        // not just Personalized ones: Resolve for every other category already ignores the "rank"
        // parameter entirely (returns this unconditionally), so this is a harmless no-op for them.
        var ranks = effectiveConfigs
            .GroupBy(c => c.WidgetType)
            .SelectMany(g => g.OrderBy(c => c.Order).Select((c, i) => (Config: c, Rank: i + 1)))
            .ToDictionary(x => x.Config, x => x.Rank);

        // Resolve a widget instance for each enabled config entry
        var tasks = effectiveConfigs
            .Select(config => ResolveAndFetch(userId, config, ranks[config], lang, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Each config row resolves to at most one descriptor (see ResolveAndFetch); drop the nulls.
        return results
            .OfType<WidgetDescriptor>()
            .OrderBy(d => d.Order)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Replaces each global row that allows it with the user's override for that same row. Several
    /// rows can share a widget type, so an override is paired with one row only: by instance id
    /// (a GUID in <c>ExtraParams["value"]</c>, as Discover, Rewards and Seasonal rows carry) when the
    /// row has one, otherwise by rank within the type (k-th row by <see cref="WidgetConfig.Order"/>
    /// takes the k-th override of that type). Each override is used at most once.
    /// </summary>
    /// <param name="globalRows">The global widget rows.</param>
    /// <param name="overrides">The user's override rows.</param>
    /// <returns>The effective rows, one per global row.</returns>
    private static IEnumerable<WidgetConfig> MergeOverrides(WidgetConfig[] globalRows, WidgetConfig[] overrides)
    {
        var unused = overrides.ToList();
        foreach (var row in globalRows.OrderBy(g => g.Order))
        {
            if (!row.AllowUserOverride)
            {
                yield return row;
                continue;
            }

            var instanceId = InstanceId(row);
            var match = unused.FirstOrDefault(o => o.WidgetType == row.WidgetType && InstanceId(o) == instanceId);
            if (match is null)
            {
                yield return row;
                continue;
            }

            unused.Remove(match);
            yield return match;
        }
    }

    /// <summary>
    /// Identifies each global row for the user's hidden sections (TODO_V4.md 8.3, F-36), the way
    /// <see cref="MergeOverrides"/> pairs rows: by instance id (a GUID in <c>ExtraParams["value"]</c>)
    /// when the row has one, otherwise by widget type and rank within the type, ordered by
    /// <see cref="WidgetConfig.Order"/> (e.g. <c>jux.personalized.favorite-genre#2</c>).
    /// </summary>
    /// <param name="globalRows">The global widget rows.</param>
    /// <returns>The key of each row.</returns>
    public static IReadOnlyDictionary<WidgetConfig, string> SectionKeys(WidgetConfig[] globalRows) =>
        globalRows
            .GroupBy(r => r.WidgetType)
            .SelectMany(g => g.OrderBy(r => r.Order).Select((r, i) => (Row: r, Key: InstanceId(r)?.ToString() ?? $"{g.Key}#{i + 1}")))
            .ToDictionary(x => x.Row, x => x.Key);

    /// <summary>
    /// Returns whether JellyUX runs for the user: enabled globally and not turned off by the user
    /// for themselves (TODO_V4.md 8.3, F-36).
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>True when the home screen, tabs and card add-ons are JellyUX's for this user.</returns>
    public bool IsEnabledFor(Guid userId) =>
        (_getConfiguration()?.Enabled ?? true) && _userConfigStore.GetUserConfiguration(userId)?.Enabled != false;

    private static Guid? InstanceId(WidgetConfig row)
    {
        var value = row.ExtraParams.FirstOrDefault(p => p.Key == "value")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves the config row for an on-demand section fetch. When multiple rows share the same
    /// <paramref name="widgetType"/> (e.g. several Discover instances with different filters), the
    /// row whose <c>ExtraParams["value"]</c> matches <paramref name="additionalData"/> is preferred
    /// so each instance's own parameters are forwarded rather than always the first configured row.
    /// Falls back to the first match by type, preserving prior behavior for single-instance widgets.
    /// The rows are the user's effective ones, overrides merged as for the layout, so a section reads
    /// the same settings (sort order included, TODO_V4.md 8.1) as the layout probe that showed it.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="widgetType">The widget type identifier.</param>
    /// <param name="additionalData">Optional instance-specific data (e.g. a Discover instance id).</param>
    /// <returns>The matching config row, or null if none is configured for this widget type.</returns>
    public WidgetConfig? ResolveConfigRow(Guid userId, string widgetType, string? additionalData)
    {
        var overrides = _userConfigStore.GetUserConfiguration(userId)?.WidgetOverrides ?? [];
        var candidates = MergeOverrides(_getConfiguration()?.Widgets ?? [], overrides)
            .Where(c => c.WidgetType == widgetType)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        if (additionalData is not null)
        {
            var match = candidates.FirstOrDefault(c => c.ExtraParams.Any(p =>
                p.Key == "value" && p.Value == additionalData));
            if (match is not null)
            {
                return match;
            }
        }

        return candidates[0];
    }

    /// <summary>
    /// Resolves a single <see cref="WidgetConfig"/> row into zero or one descriptor.
    /// Most widgets are single-instance and always produce a descriptor. Personalized widgets may
    /// resolve to null (the requested <paramref name="rank"/> has no scored value for this user, see
    /// <see cref="Personalized.PersonalizedWidgetBase.Resolve"/>).
    /// </summary>
    private async Task<WidgetDescriptor?> ResolveAndFetch(
        Guid userId,
        WidgetConfig config,
        int rank,
        string? lang,
        CancellationToken cancellationToken)
    {
        var widget = _registry.GetByType(config.WidgetType);
        if (widget is null)
        {
            _logger.LogDebug(
                "Widget type '{WidgetType}' is configured but not registered - skipping (user {UserId}).",
                config.WidgetType,
                userId);
            return null;
        }

        var instanceConfig = BuildInstanceConfig(config, widget, lang, _localizationService);

        IWidget? instance;
        try
        {
            instance = widget.Resolve(userId, instanceConfig, rank);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Widget '{WidgetType}' threw an exception during layout build for user {UserId} - skipping.",
                config.WidgetType,
                userId);
            return null;
        }

        if (instance is null)
        {
            return null;
        }

        return await FetchInstanceDescriptor(
            userId,
            config,
            instanceConfig,
            instance,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WidgetDescriptor?> FetchInstanceDescriptor(
        Guid userId,
        WidgetConfig config,
        WidgetInstanceConfig instanceConfig,
        IWidget instance,
        CancellationToken cancellationToken)
    {
        var instanceDescriptor = instance.GetDescriptor();

        // Instances produced by a fan-out (e.g. personalized widgets) self-identify via a
        // non-null AdditionalData on their own descriptor; single-instance widgets fall back
        // to the config row's resolved AdditionalData (ExtraParams["value"]).
        var selfIdentifies = instanceDescriptor.AdditionalData is not null;
        var additionalData = selfIdentifies ? instanceDescriptor.AdditionalData : instanceConfig.AdditionalData;
        var displayName = selfIdentifies ? instanceDescriptor.DisplayName : instanceConfig.DisplayName;

        // Probe exactly MinItems items for MinItems filtering (F-05): counting every match cost a
        // second query per section. Widgets that skip the count report the page size as their total,
        // so the decision takes whichever of the two is larger.
        var payload = new WidgetPayload
        {
            UserId = userId,
            AdditionalData = additionalData,
            ExtraParams = instanceConfig.ExtraParams,
            SortBy = config.SortBy,
            SortOrder = config.SortOrder,
            MaxItems = config.MaxItems,
            StartIndex = 0,
            Limit = Math.Max(1, config.MinItems)
        };

        WidgetResult result;
        try
        {
            result = await instance.GetItemsAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Widget '{WidgetType}' threw an exception during layout build for user {UserId} - skipping.",
                config.WidgetType,
                userId);
            return null;
        }

        var found = Math.Max(result.TotalRecordCount, result.Items.Count);
        if (found < config.MinItems)
        {
            _logger.LogDebug(
                "Widget '{WidgetType}' has {Count} items (MinItems={Min}) for user {UserId} - excluded from layout.",
                config.WidgetType,
                found,
                config.MinItems,
                userId);
            return null;
        }

        // Generic theme population: any widget instance may carry an ExtraParams["theme"] value
        // (e.g. SeasonalWidget's halloween/christmas/valentines/newyear presets); the resolver does
        // not need to know which widget type set it. Widgets that never set this key simply produce
        // a null Theme, which the front end ignores.
        string? theme = instanceConfig.ExtraParams is not null
            && instanceConfig.ExtraParams.TryGetValue("theme", out var themeValue)
            && !string.IsNullOrEmpty(themeValue)
                ? themeValue
                : null;

        return new WidgetDescriptor
        {
            WidgetType = instanceDescriptor.WidgetType,
            DisplayName = displayName,
            Category = instanceDescriptor.Category,
            ViewMode = config.ViewMode,
            Route = instanceDescriptor.Route,
            AdditionalData = additionalData,
            Order = config.Order,
            MinItems = config.MinItems,
            MaxItems = Math.Clamp(config.MaxItems, 1, WidgetConfig.MaxItemsCeiling),
            Theme = theme
        };
    }

    /// <summary>
    /// Renders one widget row that may not be saved yet the way the home screen would for
    /// <paramref name="userId"/> (TODO_V4.md 6.5, F-62): the row is validated like a saved one, then
    /// resolved and read through the same path as a layout section, up to its MaxItems.
    /// </summary>
    /// <param name="userId">The user whose library and history the row reads.</param>
    /// <param name="config">The row as edited on the admin page; validated in place.</param>
    /// <param name="lang">The normalized language of the section title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preview, or null when the row's widget type is not registered.</returns>
    public async Task<WidgetPreview?> PreviewAsync(
        Guid userId,
        WidgetConfig config,
        string? lang,
        CancellationToken cancellationToken)
    {
        var widget = _registry.GetByType(config.WidgetType);
        if (widget is null)
        {
            return null;
        }

        Plugin.ValidateWidget(config);
        var instanceConfig = BuildInstanceConfig(config, widget, lang, _localizationService);
        var instance = widget.Resolve(userId, instanceConfig, 0);
        if (instance is null)
        {
            return new WidgetPreview(instanceConfig.DisplayName, [], 0, config.MinItems, WidgetPreview.HiddenNoValue);
        }

        var instanceDescriptor = instance.GetDescriptor();
        var selfIdentifies = instanceDescriptor.AdditionalData is not null;
        var payload = new WidgetPayload
        {
            UserId = userId,
            AdditionalData = selfIdentifies ? instanceDescriptor.AdditionalData : instanceConfig.AdditionalData,
            ExtraParams = instanceConfig.ExtraParams,
            SortBy = config.SortBy,
            SortOrder = config.SortOrder,
            MaxItems = config.MaxItems,
            StartIndex = 0,
            Limit = config.MaxItems
        };

        var result = await instance.GetItemsAsync(payload, cancellationToken).ConfigureAwait(false);
        var found = Math.Max(result.TotalRecordCount, result.Items.Count);
        return new WidgetPreview(
            selfIdentifies ? instanceDescriptor.DisplayName : instanceConfig.DisplayName,
            result.Items,
            found,
            config.MinItems,
            found < config.MinItems ? WidgetPreview.HiddenBelowMinItems : null);
    }

    private static WidgetInstanceConfig BuildInstanceConfig(
        WidgetConfig config,
        IWidget widget,
        string? lang,
        ILocalizationService localizationService)
    {
        var extra = config.GetExtraParamsDictionary();
        extra.TryGetValue("value", out var additionalData);
        return new WidgetInstanceConfig
        {
            DisplayName = config.CustomDisplayName ?? localizationService.Translate(widget.WidgetType, lang),
            MinItems = config.MinItems,
            MaxItems = config.MaxItems,
            ViewMode = config.ViewMode,
            Order = config.Order,
            ExtraParams = extra,
            AdditionalData = additionalData,
            Lang = lang
        };
    }
}
