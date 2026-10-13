using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
using Jellyfin.Plugin.JuxHomepage.Widgets.Native;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JuxHomepage;

/// <summary>
/// JellyUX Homepage plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration, IHasWebPages
{
    /// <summary>
    /// The current configuration schema version. See the "Configuration Schema Versioning" section
    /// in CLAUDE.md for the migration policy.
    /// </summary>
    private const int CurrentSchemaVersion = 9;

    /// <summary>
    /// The current per-user configuration schema version, tracked separately from
    /// <see cref="CurrentSchemaVersion"/>. Starts at 8, above the plugin constant (7) that earlier
    /// versions stamped on user files, so no existing file reads as newer than it is.
    /// </summary>
    private const int CurrentUserSchemaVersion = 9;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Configuration is lazy-loaded by BasePlugin<T>; accessing it here forces the load (or
        // default-instance creation) immediately, so migration and validation apply on load, not
        // only when an admin saves from the dashboard (which previously left a hand-edited XML
        // file's invalid values in effect until the next save).
        var priorSchemaVersion = Configuration.SchemaVersion;
        MigrateConfiguration(Configuration);
        ValidateConfiguration(Configuration);

        // Migration restructures data (not just clamps values) -- persist immediately rather than
        // waiting for the next admin save, so a hand-edited or pre-Phase-8 V1 config file on disk
        // gets corrected right away instead of silently staying stale until someone opens the
        // config page and saves.
        if (Configuration.SchemaVersion != priorSchemaVersion)
        {
            SaveConfiguration();
        }
    }

    /// <summary>
    /// Gets the singleton plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc/>
    public override string Name => "JellyUX Homepage";

    /// <summary>
    /// The plugin page name that serves the configuration page's controller module.
    /// </summary>
    internal const string ConfigControllerPageName = "juxhomepageconfigjs";

    /// <summary>
    /// The plugin page name that serves the configuration page's stylesheet. Plugin pages are sent
    /// without cache headers, so an update reaches the page at its next visit.
    /// </summary>
    internal const string ConfigStylesheetPageName = "juxhomepageconfigcss";

    /// <inheritdoc/>
    public override Guid Id => Guid.Parse("3adf1f1f-1541-4e47-b9e3-34d2d2968af6");

    /// <inheritdoc/>
    public override string Description =>
        "Replaces and enhances the Jellyfin home screen with configurable widgets.";

    /// <inheritdoc/>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            DisplayName = Name,
            EnableInMainMenu = true,
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.html"
        };

        // The configuration page's controller, loaded by jellyfin-web as an ES module from the page's
        // data-controller="__plugin/juxhomepageconfigjs" attribute.
        yield return new PluginPageInfo
        {
            Name = ConfigControllerPageName,
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.js"
        };

        yield return new PluginPageInfo
        {
            Name = ConfigStylesheetPageName,
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.css"
        };
    }

    /// <inheritdoc/>
    public override void UpdateConfiguration(MediaBrowser.Model.Plugins.BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration config)
        {
            ValidateConfiguration(config);
        }

        // Call base last so ConfigurationChanged fires with the clamped values.
        base.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// Applies version-gated migrations to a loaded or newly-created configuration, keyed off
    /// <see cref="PluginConfiguration.SchemaVersion"/>, then bumps it to
    /// <see cref="CurrentSchemaVersion"/> once applied. See the "Configuration Schema Versioning"
    /// section in CLAUDE.md.
    /// </summary>
    /// <param name="config">The configuration to migrate in place.</param>
    internal static void MigrateConfiguration(PluginConfiguration config)
    {
        // A hand-edited or partial XML file can deserialize the array as null.
        config.Widgets ??= [];

        // A fresh install (default instance, never saved) is seeded before the tiers run: the
        // append-if-missing tiers below would otherwise make the array non-empty and leave the
        // install with only the rows they add instead of the full default layout.
        if (config.SchemaVersion < 2 && config.Widgets.Length == 0)
        {
            config.Widgets = [.. NativeWidgetDefaults.Build(), .. SeasonalWidgetDefaults.Build()];
        }

        if (config.SchemaVersion < 2)
        {
            config.Widgets = ExplodePersonalizedFanOut(config.Widgets);
        }

        if (config.SchemaVersion < 3)
        {
            config.Widgets = AppendWatchlistWidgetIfMissing(config.Widgets);
        }

        // V3-to-V4 previously added MenuTabShortcuts (TODO_V3.md Phase 8.2: JellyUX tab shortcuts in
        // the navigation drawer). That feature was removed again in the same release cycle -- the
        // native tab-switching it depended on turned out to be unreliable in real usage (confirmed by
        // the user) -- so there is no V3-to-V4 tier here anymore. No V4-to-V5 tier is needed either:
        // removing a field is not a data transform an installation needs applied, it simply stops
        // being read/written. The version is still bumped per the "Configuration Schema Versioning"
        // policy in CLAUDE.md, since the on-disk schema shape did change.

        if (config.SchemaVersion < 6)
        {
            config.Widgets = AppendSeasonalPresetsIfMissing(config.Widgets);
        }

        if (config.SchemaVersion < 7)
        {
            config.Widgets = AppendRecentlyAddedEpisodesWidgetIfMissing(config.Widgets);
        }

        // A missing <Modules> element deserializes to the all-enabled default; an empty or partial
        // one keeps the defaults of its missing switches. Only an explicit nil needs handling.
        if (config.SchemaVersion < 8)
        {
            config.Modules ??= new ModulesConfig();
        }

        // Rows saved before 8.1 (F-53) have no sort: they keep the alphabetical order they always had.
        if (config.SchemaVersion < 9)
        {
            NormalizeSort(config.Widgets);
        }

        // Add future migrations here as additional `if (config.SchemaVersion < N) { ... }` blocks,
        // each gated on its own target version and never combined with `else if` -- an installation
        // resuming from several versions behind must apply every intermediate transformation in
        // order, not jump straight to the newest one. See CLAUDE.md "Configuration Schema Versioning".
        config.SchemaVersion = CurrentSchemaVersion;
    }

    /// <summary>
    /// Applies the same version-gated migrations as <see cref="MigrateConfiguration"/>, but to a
    /// per-user configuration's <see cref="UserConfiguration.WidgetOverrides"/> -- per the
    /// "Configuration Schema Versioning" policy in CLAUDE.md, any migration of widget configuration
    /// rows must apply to both models identically.
    /// </summary>
    /// <param name="config">The user configuration to migrate in place.</param>
    internal static void MigrateUserConfiguration(UserConfiguration config)
    {
        // "WidgetOverrides": null in a user's JSON file deserializes as null despite the initializer.
        config.WidgetOverrides ??= [];

        if (config.SchemaVersion < 2)
        {
            config.WidgetOverrides = ExplodePersonalizedFanOut(config.WidgetOverrides);
        }

        if (config.SchemaVersion < 9)
        {
            NormalizeSort(config.WidgetOverrides);
        }

        // See MigrateConfiguration for the chain-extension pattern to follow for future tiers; user
        // tiers are numbered against CurrentUserSchemaVersion, the next one being 10.
        config.SchemaVersion = CurrentUserSchemaVersion;
    }

    /// <summary>
    /// V1-to-V2 migration step (TODO_V2.md Phase 8.4): explodes any Personalized widget row that
    /// still has <c>MaxInstances &gt; 1</c> (the old fan-out setting) into that many independent
    /// rows, one per instance, rather than silently losing the setting now that Personalized widgets
    /// are single-instance ("1 row = 1 section", see <see cref="PersonalizedWidgetBase"/>).
    /// The first copy keeps the original row's <see cref="WidgetConfig.CustomDisplayName"/> and
    /// <see cref="WidgetConfig.ExtraParams"/>; subsequent copies start blank. Rows for other widget
    /// categories, or Personalized rows already at <c>MaxInstances &lt;= 1</c>, pass through unchanged.
    /// </summary>
    /// <param name="widgets">The widget configuration rows to migrate.</param>
    /// <returns>The migrated array of rows.</returns>
    private static WidgetConfig[] ExplodePersonalizedFanOut(WidgetConfig[] widgets)
    {
        var result = new List<WidgetConfig>(widgets.Length);

        foreach (var widget in widgets)
        {
            if (!PersonalizedWidgetTypes.All.Contains(widget.WidgetType) || widget.MaxInstances <= 1)
            {
                result.Add(widget);
                continue;
            }

            for (var i = 0; i < widget.MaxInstances; i++)
            {
                result.Add(new WidgetConfig
                {
                    WidgetType = widget.WidgetType,
                    CustomDisplayName = i == 0 ? widget.CustomDisplayName : null,
                    Enabled = widget.Enabled,
                    AllowUserOverride = widget.AllowUserOverride,
                    Order = widget.Order + (i * 10),
                    MinItems = widget.MinItems,
                    MaxItems = widget.MaxItems,
                    ViewMode = widget.ViewMode,
                    MinInstances = 1,
                    MaxInstances = 1,
                    ExtraParams = i == 0 ? widget.ExtraParams : []
                });
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// V2-to-V3 migration step (TODO_V3.md Phase 6.4): appends the new native Watchlist widget's
    /// default <see cref="WidgetConfig"/> row (see <see cref="Widgets.Native.NativeWidgetDefaults"/>)
    /// to an existing, already-populated <see cref="Configuration.PluginConfiguration.Widgets"/> array
    /// -- <see cref="Inject.StartupService"/> only seeds the full default list when the array is
    /// empty (brand-new installs), so an existing installation would otherwise never see the new
    /// widget. Idempotent: a no-op if a row for this widget type already exists (defensive, in case
    /// this migration ever runs twice against the same configuration).
    /// </summary>
    /// <param name="widgets">The widget configuration rows to migrate.</param>
    /// <returns>The migrated array of rows, with the Watchlist row appended if it was missing.</returns>
    private static WidgetConfig[] AppendWatchlistWidgetIfMissing(WidgetConfig[] widgets)
    {
        const string WatchlistWidgetType = "jux.native.watchlist";

        if (widgets.Any(w => w.WidgetType == WatchlistWidgetType))
        {
            return widgets;
        }

        var result = new List<WidgetConfig>(widgets)
        {
            new()
            {
                WidgetType = WatchlistWidgetType,
                Enabled = true,
                Order = 50,
                MinItems = 1,
                MaxItems = 20,
                ViewMode = WidgetViewMode.Portrait,
                MinInstances = 1,
                MaxInstances = 1,
                AllowUserOverride = true
            }
        };

        return result.ToArray();
    }

    /// <summary>
    /// V5-to-V6 migration step (TODO_V3.md Phase 9.1): appends any of the four seasonal preset rows
    /// (see <see cref="SeasonalWidgetDefaults"/>) that are missing from an existing installation's
    /// <see cref="Configuration.PluginConfiguration.Widgets"/> array -- <see cref="Inject.StartupService"/>
    /// only seeds the full default list when the array is empty (brand-new installs), so an existing
    /// installation would otherwise never see the new presets. Idempotent: checked per-preset via its
    /// fixed <c>ExtraParams["value"]</c> GUID, so a preset an admin already removed intentionally is
    /// not re-added, but only if the row was removed entirely -- this migration only ever fires once
    /// per installation (schema version only moves forward), so that distinction does not arise in
    /// practice.
    /// </summary>
    /// <param name="widgets">The widget configuration rows to migrate.</param>
    /// <returns>The migrated array of rows, with any missing preset rows appended.</returns>
    private static WidgetConfig[] AppendSeasonalPresetsIfMissing(WidgetConfig[] widgets)
    {
        var existingIds = widgets
            .Where(w => w.WidgetType == "jux.connected.seasonal")
            .Select(w => w.ExtraParams.FirstOrDefault(p => p.Key == "value")?.Value)
            .Where(v => v is not null)
            .ToHashSet();

        var missingPresets = SeasonalWidgetDefaults.Build()
            .Where(preset => !existingIds.Contains(preset.ExtraParams.First(p => p.Key == "value").Value));

        return [.. widgets, .. missingPresets];
    }

    /// <summary>
    /// V6-to-V7 migration step (TODO_V3.md Phase 10.2): appends the new native "Recently Added
    /// Episodes" widget's default <see cref="WidgetConfig"/> row (see
    /// <see cref="Widgets.Native.NativeWidgetDefaults"/>) to an existing, already-populated
    /// <see cref="Configuration.PluginConfiguration.Widgets"/> array -- <see cref="Inject.StartupService"/>
    /// only seeds the full default list when the array is empty (brand-new installs), so an existing
    /// installation would otherwise never see the new widget. Idempotent: a no-op if a row for this
    /// widget type already exists (same pattern as <see cref="AppendWatchlistWidgetIfMissing"/>).
    /// </summary>
    /// <param name="widgets">The widget configuration rows to migrate.</param>
    /// <returns>The migrated array of rows, with the Recently Added Episodes row appended if missing.</returns>
    private static WidgetConfig[] AppendRecentlyAddedEpisodesWidgetIfMissing(WidgetConfig[] widgets)
    {
        const string RecentlyAddedEpisodesWidgetType = "jux.native.recently-added-episodes";

        if (widgets.Any(w => w.WidgetType == RecentlyAddedEpisodesWidgetType))
        {
            return widgets;
        }

        var result = new List<WidgetConfig>(widgets)
        {
            new()
            {
                WidgetType = RecentlyAddedEpisodesWidgetType,
                Enabled = true,
                Order = 35,
                MinItems = 4,
                MaxItems = 20,
                ViewMode = WidgetViewMode.Landscape,
                MinInstances = 1,
                MaxInstances = 1,
                AllowUserOverride = true
            }
        };

        return result.ToArray();
    }

    /// <summary>
    /// V8-to-V9 migration step (TODO_V4.md 8.1, F-53): gives every row a known sort, alphabetical
    /// when it has none, so the order of existing sections does not change. Idempotent.
    /// </summary>
    /// <param name="widgets">The widget rows to migrate in place.</param>
    private static void NormalizeSort(WidgetConfig[] widgets)
    {
        foreach (var widget in widgets)
        {
            widget.SortBy = WidgetSort.NormalizeSortBy(widget.SortBy);
            widget.SortOrder = WidgetSort.NormalizeSortOrder(widget.SortOrder);
        }
    }

    /// <summary>
    /// Clamps one widget row to valid ranges in place: the rows of a saved configuration and a row
    /// previewed before it is saved (TODO_V4.md 6.5) go through the same rules.
    /// </summary>
    /// <param name="widget">The widget row to validate in place.</param>
    internal static void ValidateWidget(WidgetConfig widget)
    {
        // Clamp per-widget values and ensure MinItems does not exceed MaxItems.
        widget.MinItems = Math.Max(0, widget.MinItems);
        widget.MaxItems = Math.Clamp(widget.MaxItems, 1, WidgetConfig.MaxItemsCeiling);
        if (widget.MinItems > widget.MaxItems)
        {
            widget.MinItems = widget.MaxItems;
        }

        widget.SortBy = WidgetSort.NormalizeSortBy(widget.SortBy);
        widget.SortOrder = WidgetSort.NormalizeSortOrder(widget.SortOrder);

        widget.MinInstances = Math.Max(1, widget.MinInstances);
        widget.MaxInstances = Math.Max(1, widget.MaxInstances);
        if (widget.MinInstances > widget.MaxInstances)
        {
            widget.MinInstances = widget.MaxInstances;
        }

        // Seasonal windows recur every year as MM-dd: an unparseable date is dropped at load and save
        // (TODO_V4.md 5.5, F-23), so the stored configuration and the admin page never carry a window
        // the widget would silently treat as never active.
        if (widget.WidgetType == "jux.connected.seasonal")
        {
            widget.ExtraParams = widget.ExtraParams
                .Where(p => p.Key is not ("startDate" or "endDate") || SeasonalWidget.TryParseMonthDay(p.Value, out _))
                .ToArray();
        }
    }

    /// <summary>
    /// Clamps all configuration values to valid ranges in place. Called both when the configuration
    /// is loaded (so a hand-edited XML file cannot bypass validation until the next admin save) and
    /// when an admin saves from the dashboard.
    /// </summary>
    /// <param name="config">The configuration to validate in place.</param>
    internal static void ValidateConfiguration(PluginConfiguration config)
    {
        // A hand-edited XML file can carry a nil <Modules> on an already migrated configuration.
        config.Modules ??= new ModulesConfig();

        // Clamp cache values to valid ranges.
        if (config.Cache is not null)
        {
            config.Cache.SessionTtlMinutes = Math.Max(1, config.Cache.SessionTtlMinutes);
            config.Cache.TMDbRefreshIntervalHours = Math.Max(1, config.Cache.TMDbRefreshIntervalHours);
        }

        foreach (var widget in config.Widgets)
        {
            ValidateWidget(widget);
        }

        // Trim API keys; empty string becomes null.
        if (config.ApiKeys is not null)
        {
            config.ApiKeys.TMDb = string.IsNullOrWhiteSpace(config.ApiKeys.TMDb)
                ? null
                : config.ApiKeys.TMDb.Trim();
        }

        // Clamp TMDb list page counts to a sane range (1 page = up to 20 items; more pages
        // means more cross-referencing API calls per refresh).
        if (config.TMDbLists is not null)
        {
            config.TMDbLists.TrendingMoviesPages = Math.Clamp(config.TMDbLists.TrendingMoviesPages, 1, 5);
            config.TMDbLists.TrendingShowsPages = Math.Clamp(config.TMDbLists.TrendingShowsPages, 1, 5);
            config.TMDbLists.AiringTodayPages = Math.Clamp(config.TMDbLists.AiringTodayPages, 1, 5);
            config.TMDbLists.TopRatedMoviesPages = Math.Clamp(config.TMDbLists.TopRatedMoviesPages, 1, 5);
            config.TMDbLists.TopRatedShowsPages = Math.Clamp(config.TMDbLists.TopRatedShowsPages, 1, 5);
            config.TMDbLists.NowPlayingMoviesPages = Math.Clamp(config.TMDbLists.NowPlayingMoviesPages, 1, 5);
            config.TMDbLists.NowPlayingRegion = string.IsNullOrWhiteSpace(config.TMDbLists.NowPlayingRegion)
                ? null
                : config.TMDbLists.NowPlayingRegion.Trim().ToUpperInvariant();
            config.TMDbLists.TopRatedVoteCountMin = Math.Max(0, config.TMDbLists.TopRatedVoteCountMin);
        }
    }
}
