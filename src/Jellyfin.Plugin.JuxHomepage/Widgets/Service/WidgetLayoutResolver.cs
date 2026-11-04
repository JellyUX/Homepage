using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
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
    /// <summary>Maximum number of rows resolved and probed at the same time during a layout build, on any machine.</summary>
    private const int MaxConcurrentProbes = 4;

    private readonly IWidgetRegistry _registry;
    private readonly IUserConfigurationStore _userConfigStore;
    private readonly ILocalizationService _localizationService;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<WidgetLayoutResolver> _logger;
    private readonly BinaryMismatchReporter? _mismatchReporter;
    private readonly int _concurrentProbes;

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
    /// <param name="mismatchReporter">
    /// Tells the administrator when a widget fails the layout probe because the plugin build does not match
    /// the server, so the section left out of the layout is not silent (COMPAT-01.a). Null only logs it.
    /// </param>
    /// <param name="processorCount">The machine's processor count; <see cref="Environment.ProcessorCount"/> when null.</param>
    public WidgetLayoutResolver(
        IWidgetRegistry registry,
        IUserConfigurationStore userConfigStore,
        ILocalizationService localizationService,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<WidgetLayoutResolver> logger,
        BinaryMismatchReporter? mismatchReporter = null,
        int? processorCount = null)
    {
        _registry = registry;
        _userConfigStore = userConfigStore;
        _localizationService = localizationService;
        _getConfiguration = getConfiguration;
        _logger = logger;
        _mismatchReporter = mismatchReporter;

        // One row per processor on a small server, so a computation does not hold every pool thread the
        // requests need: on 1 vCPU, the p95 of the concurrent native requests went 89 -> 68 ms (PERF-07, DR-006).
        _concurrentProbes = Math.Clamp(processorCount ?? Environment.ProcessorCount, 1, MaxConcurrentProbes);
    }

    /// <summary>
    /// Builds the ordered, MinItems-filtered list of widget descriptors for a user's home screen,
    /// merging global plugin configuration with the user's own overrides.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="lang">The requested language code (e.g. "fr"), or null for the default (English).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordered list of descriptors, not yet paginated.</returns>
    public Task<IReadOnlyList<WidgetDescriptor>> BuildDescriptors(
        Guid userId,
        string? lang,
        CancellationToken cancellationToken) =>
        BuildDescriptors(userId, lang, onProbed: null, cancellationToken);

    /// <summary>
    /// Builds the layout like <see cref="BuildDescriptors(Guid, string?, CancellationToken)"/> and
    /// hands each kept section's probed page to <paramref name="onProbed"/>, with the page size the
    /// probe read: the first page the client requests from the Section endpoint (PERF-03.b).
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="lang">The requested language code (e.g. "fr"), or null for the default (English).</param>
    /// <param name="onProbed">Receives each kept section, its probe's page size and its page; null for none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordered list of descriptors, not yet paginated.</returns>
    public async Task<IReadOnlyList<WidgetDescriptor>> BuildDescriptors(
        Guid userId,
        string? lang,
        Action<WidgetDescriptor, int, WidgetResult>? onProbed,
        CancellationToken cancellationToken) =>
        (await BuildCore(userId, lang, onProbed, store: null, cancellationToken).ConfigureAwait(false)).Descriptors;

    /// <summary>
    /// Builds the layout with the deferred level kept on disk (DR-033). With a <paramref name="stored"/> layout, each
    /// deferred row it holds is read again from its stored order with live user data, without resolving its widget
    /// (no scoring) nor probing it; a deferred row it lacks and every direct row are read as by
    /// <see cref="BuildDescriptors(Guid, string?, Action{WidgetDescriptor, int, WidgetResult}?, CancellationToken)"/>.
    /// Without one, every row is computed, each deferred row probed with a margin of twice its MaxItems, and the
    /// returned <see cref="LayoutBuild.Computed"/> layout is the one to store.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="lang">The requested language code, or null for the default (English).</param>
    /// <param name="onProbed">Receives each kept section, the page size the client reads and its first page.</param>
    /// <param name="stored">The stored layout to read again, or null to compute one.</param>
    /// <param name="reader">Reads a page of a deferred section from its stored ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The layout, the layout to store when one was computed, and whether a stored row is due again.</returns>
    public Task<LayoutBuild> BuildAsync(
        Guid userId,
        string? lang,
        Action<WidgetDescriptor, int, WidgetResult>? onProbed,
        StoredLayout? stored,
        StoredSectionReader reader,
        CancellationToken cancellationToken) =>
        BuildCore(userId, lang, onProbed, new StoreAccess(stored, reader), cancellationToken);

    private async Task<LayoutBuild> BuildCore(
        Guid userId,
        string? lang,
        Action<WidgetDescriptor, int, WidgetResult>? onProbed,
        StoreAccess? store,
        CancellationToken cancellationToken)
    {
        var globalConfig = _getConfiguration()?.Widgets ?? [];
        var userConfig = _userConfigStore.GetUserConfiguration(userId);

        // A section the user hid (TODO_V4.md 8.3, F-36) is left out by the key of its global row. The
        // rows are the global ones: per-user overrides are frozen (QUAL-02, CONTRIBUTING.md).
        var hidden = new HashSet<string>(userConfig?.HiddenSections ?? [], StringComparer.Ordinal);
        var keys = SectionKeys(globalConfig);
        var effectiveConfigs = globalConfig
            .Where(row => !hidden.Contains(keys[row]))
            .Select(row => (Config: row, Key: keys[row]))
            .Where(c => c.Config.Enabled)
            .OrderBy(c => c.Config.Order)
            .ToList();

        // Assign a 1-indexed rank to each row within its WidgetType group, ordered by Order --
        // replaces the old MaxInstances fan-out count (TODO_V2.md Phase 8.2). Every row gets a rank,
        // not just Personalized ones: Resolve for every other category already ignores the "rank"
        // parameter entirely (returns this unconditionally), so this is a harmless no-op for them.
        var ranks = effectiveConfigs
            .Select(c => c.Config)
            .GroupBy(c => c.WidgetType)
            .SelectMany(g => g.OrderBy(c => c.Order).Select((c, i) => (Config: c, Rank: i + 1)))
            .ToDictionary(x => x.Config, x => x.Rank);

        // Resolve a widget instance for each enabled config entry. Resolve (which can wait for the
        // first scoring) and the probes read the library synchronously, so each row runs on the
        // thread pool, at most one per processor and MaxConcurrentProbes at a time (PERF-02, PERF-07): a slow
        // row overlaps the others instead of delaying them (BuildDescriptors_SlowSynchronousProbes_OverlapUpToTheBound,
        // BuildDescriptors_ResolveWaitingForScoring_OverlapsTheOtherProbes). Since DR-033 the probes and the
        // scoring run only when a layout is computed (first visit, nightly task, a stored layout due again); a
        // stored deferred row is read again without them.
        var results = new RowOutcome[effectiveConfigs.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, effectiveConfigs.Count),
            new ParallelOptions { MaxDegreeOfParallelism = _concurrentProbes, CancellationToken = cancellationToken },
            async (i, token) =>
            {
                var (config, key) = effectiveConfigs[i];
                results[i] = store?.Stored?.RowOf(key) is { } row && IsDeferred(config.WidgetType)
                    ? ReadStoredRow(userId, config, key, row, onProbed, store.Reader)
                    : await ResolveAndFetch(userId, config, key, ranks[config], lang, onProbed, store, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        // Each config row resolves to at most one descriptor (see ResolveAndFetch); drop the nulls.
        var descriptors = results
            .Select(r => r.Descriptor)
            .OfType<WidgetDescriptor>()
            .OrderBy(d => d.Order)
            .ToList()
            .AsReadOnly();
        var computed = store is { Stored: null }
            ? new StoredLayout { Rows = [.. results.Select(r => r.Row).OfType<StoredRow>()] }
            : null;
        return new LayoutBuild(descriptors, computed, results.Any(r => r.Due));
    }

    private bool IsDeferred(string widgetType) => _registry.GetByType(widgetType)?.Freshness == FreshnessLevel.Deferred;

    /// <summary>
    /// Reads a deferred row again from its stored order (DR-033): left out as stored, or its section with the page the
    /// client reads, the live filter applied. It is due again when watched or removed titles left fewer than MaxItems
    /// of the stored ones, and left out until then when they left fewer than MinItems.
    /// </summary>
    private RowOutcome ReadStoredRow(
        Guid userId,
        WidgetConfig config,
        string key,
        StoredRow row,
        Action<WidgetDescriptor, int, WidgetResult>? onProbed,
        StoredSectionReader reader)
    {
        var widget = _registry.GetByType(config.WidgetType);
        if (row.Section is null || widget is null)
        {
            return new RowOutcome(null, row, false);
        }

        var descriptor = row.Section.ToDescriptor(key);
        var payload = ClientPayload(userId, config, descriptor);
        var (page, visible) = reader.Read(widget, payload, row.Ids);
        var due = visible < Math.Min(descriptor.MaxItems, row.Ids.Count);
        if (visible < config.MinItems)
        {
            return new RowOutcome(null, row, true);
        }

        onProbed?.Invoke(descriptor, payload.Limit, page);
        return new RowOutcome(descriptor, row, due);
    }

    /// <summary>The first page the client requests from the Section endpoint for a section (PERF-03.b).</summary>
    private static WidgetPayload ClientPayload(Guid userId, WidgetConfig config, WidgetDescriptor descriptor)
    {
        var maxItems = Math.Clamp(config.MaxItems, 1, WidgetConfig.MaxItemsCeiling);
        return new WidgetPayload
        {
            UserId = userId,
            AdditionalData = descriptor.AdditionalData,
            ExtraParams = config.GetExtraParamsDictionary(),
            SortBy = config.SortBy,
            SortOrder = config.SortOrder,
            MaxItems = config.MaxItems,
            StartIndex = 0,
            Limit = Math.Max(Math.Max(1, config.MinItems), maxItems),
            Featured = descriptor.Featured,
            FeaturedMinRating = descriptor.Featured ? config.FeaturedMinRating : 0
        };
    }

    /// <summary>
    /// Identifies each global row for the user's hidden sections (TODO_V4.md 8.3, F-36): by instance id
    /// (a GUID in <c>ExtraParams["value"]</c>, as Discover, Rewards and Seasonal rows carry) when the
    /// row has one, otherwise by widget type and rank within the type, ordered by
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

    /// <summary>
    /// Returns the discriminant of the user's stored layouts: a saved configuration or a change of the user's own
    /// settings makes the layouts stored before it computed again (DR-033).
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The discriminant.</returns>
    public string DiscriminantOf(Guid userId) =>
        StoredHomeScreens.Discriminant(_getConfiguration(), _userConfigStore.GetUserConfiguration(userId));

    private static Guid? InstanceId(WidgetConfig row)
    {
        var value = row.ExtraParams.FirstOrDefault(p => p.Key == WidgetConfig.ValueParam)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves the config row for an on-demand section fetch. When multiple rows share the same
    /// <paramref name="widgetType"/> (e.g. several Discover instances with different filters), the
    /// row whose <c>ExtraParams["value"]</c> matches <paramref name="additionalData"/> is preferred
    /// so each instance's own parameters are forwarded rather than always the first configured row.
    /// Falls back to the first match by type, preserving prior behavior for single-instance widgets.
    /// The rows are the global ones, as for the layout, so a section reads the same settings (sort
    /// order included, TODO_V4.md 8.1) as the layout probe that showed it.
    /// </summary>
    /// <param name="widgetType">The widget type identifier.</param>
    /// <param name="additionalData">Optional instance-specific data (e.g. a Discover instance id).</param>
    /// <param name="sectionKey">The section's <see cref="WidgetDescriptor.SectionKey"/>, preferred when it names a row.</param>
    /// <returns>The matching config row, or null if none is configured for this widget type.</returns>
    public WidgetConfig? ResolveConfigRow(string widgetType, string? additionalData, string? sectionKey = null)
    {
        var globalRows = _getConfiguration()?.Widgets ?? [];
        var candidates = globalRows.OrderBy(g => g.Order).Where(c => c.WidgetType == widgetType).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        // The layout's key names the exact row: a Personalized section sends its scored value, not
        // the row's, so only the key tells its second row from its first (TODO_V4.md 9.3, F-27).
        if (sectionKey is not null)
        {
            var keys = SectionKeys(globalRows);
            var keyed = candidates.FirstOrDefault(row => keys[row] == sectionKey);
            if (keyed is not null)
            {
                return keyed;
            }
        }

        if (additionalData is not null)
        {
            var match = candidates.FirstOrDefault(c => c.ExtraParams.Any(p =>
                p.Key == WidgetConfig.ValueParam && p.Value == additionalData));
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
    private async Task<RowOutcome> ResolveAndFetch(
        Guid userId,
        WidgetConfig config,
        string sectionKey,
        int rank,
        string? lang,
        Action<WidgetDescriptor, int, WidgetResult>? onProbed,
        StoreAccess? store,
        CancellationToken cancellationToken)
    {
        var widget = _registry.GetByType(config.WidgetType);
        if (widget is null)
        {
            _logger.LogDebug(
                "Widget type '{WidgetType}' is configured but not registered - skipping (user {UserId}).",
                config.WidgetType,
                userId);
            return default;
        }

        // A computed layout probes its deferred rows with a margin and stores them; a stored layout that lacks a
        // deferred row (its probe failed when it was computed) reads it live and is due again.
        var deferred = widget.Freshness == FreshnessLevel.Deferred;
        var materialize = deferred && store is { Stored: null };
        var lacking = deferred && store is { Stored: not null };

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
            await ReportMismatchAsync(config.WidgetType, ex).ConfigureAwait(false);
            return new RowOutcome(null, null, lacking);
        }

        if (instance is null)
        {
            return new RowOutcome(null, materialize ? LeftOut(sectionKey, widget) : null, lacking);
        }

        var probed = await FetchInstanceDescriptor(
            userId,
            config,
            instanceConfig,
            instance,
            materialize,
            cancellationToken).ConfigureAwait(false);
        if (probed is not { } kept)
        {
            return new RowOutcome(null, null, lacking);
        }

        // Only a page read from library ids can be stored: a stored layout lacking any other row is not due for it.
        var due = lacking && kept.Page.ItemIds is not null;
        if (kept.Descriptor is not { } descriptor)
        {
            // Below MinItems: left out, and stored as left out when its page was read from library ids.
            return new RowOutcome(null, materialize && kept.Page.ItemIds is not null ? LeftOut(sectionKey, widget) : null, due);
        }

        descriptor.SectionKey = sectionKey;
        StoredRow? row = null;
        var (limit, page) = (kept.Limit, kept.Page);
        if (materialize && page.ItemIds is { } ids && store is not null)
        {
            // The order is stored with its margin; the page the client reads is its first MaxItems, filtered live.
            row = new StoredRow { Key = sectionKey, Sources = [.. widget.Sources], Section = StoredSection.From(descriptor), Ids = [.. ids] };
            var client = ClientPayload(userId, config, descriptor);
            page = page.Head?.Invoke(client.Limit) ?? store.Reader.Read(widget, client, ids).Page;
            limit = client.Limit;
        }

        if (!descriptor.ReadNatively)
        {
            onProbed?.Invoke(descriptor, limit, page);
        }

        return new RowOutcome(descriptor, row, due);
    }

    private static StoredRow LeftOut(string sectionKey, IWidget widget) =>
        new() { Key = sectionKey, Sources = [.. widget.Sources] };

    private async Task<(WidgetDescriptor? Descriptor, int Limit, WidgetResult Page)?> FetchInstanceDescriptor(
        Guid userId,
        WidgetConfig config,
        WidgetInstanceConfig instanceConfig,
        IWidget instance,
        bool withMargin,
        CancellationToken cancellationToken)
    {
        var instanceDescriptor = instance.GetDescriptor();

        // Instances produced by a fan-out (e.g. personalized widgets) self-identify via a
        // non-null AdditionalData on their own descriptor; single-instance widgets fall back
        // to the config row's resolved AdditionalData (ExtraParams["value"]).
        var selfIdentifies = instanceDescriptor.AdditionalData is not null;
        var additionalData = selfIdentifies ? instanceDescriptor.AdditionalData : instanceConfig.AdditionalData;
        var displayName = selfIdentifies ? instanceDescriptor.DisplayName : instanceConfig.DisplayName;

        // The probe reads the first page the client requests from the Section endpoint, MaxItems
        // items, so that section is served from it (PERF-03.b); a section the client reads from a
        // native route reads exactly MinItems items (F-05). Counting every match cost a second query
        // per section: widgets that skip the count report the page size as their total, so the
        // decision takes whichever of the two is larger.
        var maxItems = Math.Clamp(config.MaxItems, 1, WidgetConfig.MaxItemsCeiling);
        var minItems = Math.Max(1, config.MinItems);
        var payload = new WidgetPayload
        {
            UserId = userId,
            AdditionalData = additionalData,
            ExtraParams = instanceConfig.ExtraParams,
            SortBy = config.SortBy,
            SortOrder = config.SortOrder,
            MaxItems = config.MaxItems,
            StartIndex = 0,
            // A computed deferred row reads twice its MaxItems: the margin its stored order keeps for the titles watched
            // or removed before the next computation (DR-033).
            Limit = instanceDescriptor.ReadNatively ? minItems : Math.Max(minItems, withMargin ? 2 * maxItems : maxItems)
        }.WithFeatured(config, instanceDescriptor);

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
            await ReportMismatchAsync(config.WidgetType, ex).ConfigureAwait(false);
            return null;
        }

        var found = Math.Max(result.TotalRecordCount, result.ItemCount);
        if (found < config.MinItems)
        {
            _logger.LogDebug(
                "Widget '{WidgetType}' has {Count} items (MinItems={Min}) for user {UserId} - excluded from layout.",
                config.WidgetType,
                found,
                config.MinItems,
                userId);
            return (null, payload.Limit, result);
        }

        // Generic theme population: any widget instance may carry an ExtraParams["theme"] value
        // (e.g. SeasonalWidget's halloween/christmas/valentines/newyear presets); the resolver does
        // not need to know which widget type set it. Widgets that never set this key simply produce
        // a null Theme, which the front end ignores.
        string? theme = instanceConfig.ExtraParams is not null
            && instanceConfig.ExtraParams.TryGetValue(SeasonalWidget.ThemeParam, out var themeValue)
            && !string.IsNullOrEmpty(themeValue)
                ? themeValue
                : null;

        var descriptor = new WidgetDescriptor
        {
            WidgetType = instanceDescriptor.WidgetType,
            DisplayName = displayName,
            Category = instanceDescriptor.Category,
            ViewMode = config.ViewMode,
            Featured = payload.Featured,
            Route = instanceDescriptor.Route,
            AdditionalData = additionalData,
            Order = config.Order,
            MinItems = config.MinItems,
            MaxItems = maxItems,
            LimitFactor = instanceDescriptor.LimitFactor,
            MaxPerSeries = instanceDescriptor.MaxPerSeries,
            ReadNatively = instanceDescriptor.ReadNatively,
            Theme = theme
        };
        return (descriptor, payload.Limit, result);
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
        }.WithFeatured(config, instanceDescriptor);

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
        extra.TryGetValue(WidgetConfig.ValueParam, out var additionalData);
        return new WidgetInstanceConfig
        {
            // A preset names itself by a translated key (TODO_V4.md 9.4, F-64); a name the admin typed wins.
            DisplayName = config.CustomDisplayName
                ?? localizationService.Translate(extra.TryGetValue(WidgetConfig.NameKeyParam, out var nameKey) ? nameKey : widget.WidgetType, lang),
            MinItems = config.MinItems,
            MaxItems = config.MaxItems,
            ViewMode = config.ViewMode,
            Order = config.Order,
            ExtraParams = extra,
            AdditionalData = additionalData,
            Lang = lang
        };
    }

    private Task ReportMismatchAsync(string widgetType, Exception exception) =>
        _mismatchReporter?.ReportAsync(widgetType, exception) ?? Task.CompletedTask;

    /// <summary>The outcome of one configuration row: its section or none, its stored row, and whether it is due again.</summary>
    private readonly record struct RowOutcome(WidgetDescriptor? Descriptor, StoredRow? Row, bool Due);

    /// <summary>The stored layout a build reads again (null: one is computed) and the reader of its sections.</summary>
    private sealed record StoreAccess(StoredLayout? Stored, StoredSectionReader Reader);
}

/// <summary>A layout built with the deferred level kept on disk (DR-033).</summary>
/// <param name="Descriptors">The ordered sections, not yet paginated.</param>
/// <param name="Computed">The deferred rows this build computed, to store; null when a stored layout was read again.</param>
/// <param name="Due">Whether a stored row was read short of its MaxItems or was missing, so the layout is computed again.</param>
public sealed record LayoutBuild(IReadOnlyList<WidgetDescriptor> Descriptors, StoredLayout? Computed, bool Due);
