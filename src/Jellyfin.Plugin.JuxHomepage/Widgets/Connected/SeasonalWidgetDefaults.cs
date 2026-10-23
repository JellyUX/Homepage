namespace Jellyfin.Plugin.JuxHomepage.Widgets.Connected;

/// <summary>
/// Provides the seasonal preset <see cref="WidgetConfig"/> rows shipped with the plugin. Mirrors
/// <see cref="Native.NativeWidgetDefaults"/>, but every row is disabled by default -- an admin opts in
/// to whichever seasons they want.
/// <para>
/// As in KefinTweaks (observed on 8097, TODO_V4.md 9.2, F-60), a season can hold several sections, each
/// with a single source: a tag (TMDb keyword names, the tags Jellyfin's metadata writes) or a genre.
/// Genre names follow the metadata language, so a genre preset lists the English and the French name.
/// </para>
/// <para>
/// Each row's <c>ExtraParams["value"]</c> uses a fixed, hardcoded GUID rather than a freshly generated
/// one, so a later migration (<see cref="Plugin.MigrateConfiguration"/>, schema v5-to-v6) can detect
/// whether a given preset has already been appended to an existing installation's configuration and
/// stays idempotent if it runs more than once.
/// </para>
/// </summary>
public static class SeasonalWidgetDefaults
{
    /// <summary>The fixed instance id for the Halloween preset row.</summary>
    public static readonly Guid HalloweenId = Guid.Parse("8f3a1b2c-0001-4b1e-9c3a-1f2e3d4c5b6a");

    /// <summary>The fixed instance id for the Christmas preset row.</summary>
    public static readonly Guid ChristmasId = Guid.Parse("8f3a1b2c-0002-4b1e-9c3a-1f2e3d4c5b6a");

    /// <summary>The fixed instance id for the Valentine's Day preset row.</summary>
    public static readonly Guid ValentinesId = Guid.Parse("8f3a1b2c-0003-4b1e-9c3a-1f2e3d4c5b6a");

    /// <summary>The fixed instance id for the New Year preset row.</summary>
    public static readonly Guid NewYearId = Guid.Parse("8f3a1b2c-0004-4b1e-9c3a-1f2e3d4c5b6a");

    /// <summary>The fixed instance id for the Halloween horror genre preset row.</summary>
    public static readonly Guid HalloweenHorrorId = Guid.Parse("8f3a1b2c-0005-4b1e-9c3a-1f2e3d4c5b6a");

    /// <summary>The fixed instance id for the Halloween thriller genre preset row.</summary>
    public static readonly Guid HalloweenThrillerId = Guid.Parse("8f3a1b2c-0006-4b1e-9c3a-1f2e3d4c5b6a");

    /// <summary>The fixed instance id for the Valentine's Day romance genre preset row.</summary>
    public static readonly Guid ValentinesRomanceId = Guid.Parse("8f3a1b2c-0007-4b1e-9c3a-1f2e3d4c5b6a");

    /// <summary>
    /// The English names the first four presets were saved with before their names became translated
    /// keys (TODO_V4.md 9.4, F-64): schema tier 11 replaces a name still equal to it by the preset's key.
    /// </summary>
    public static readonly IReadOnlyDictionary<Guid, string> ShippedNames = new Dictionary<Guid, string>
    {
        [HalloweenId] = "Halloween",
        [ChristmasId] = "Christmas",
        [ValentinesId] = "Valentine's Day",
        [NewYearId] = "New Year"
    };

    /// <summary>
    /// The presets added by schema tier 11 (TODO_V4.md 9.2), the only ones that tier appends to an
    /// existing configuration.
    /// </summary>
    public static readonly IReadOnlyCollection<Guid> AddedInSchema11 = [HalloweenHorrorId, HalloweenThrillerId, ValentinesRomanceId];

    /// <summary>
    /// Builds the default seasonal preset configuration rows, all disabled. A row whose filter matches
    /// nothing in a library simply stays hidden.
    /// </summary>
    /// <returns>A fixed-order array of seven <see cref="WidgetConfig"/> entries.</returns>
    public static WidgetConfig[] Build() =>
    [
        BuildRow(HalloweenId, "jux.seasonal.halloween", "10-01", "10-31", "halloween", (SeasonalWidget.TagParam, "halloween"), order: 200),
        BuildRow(HalloweenHorrorId, "jux.seasonal.halloween-horror", "10-01", "10-31", "halloween", (SeasonalWidget.GenreParam, "Horror|Horreur"), order: 201),
        BuildRow(HalloweenThrillerId, "jux.seasonal.halloween-thrillers", "10-01", "10-31", "halloween", (SeasonalWidget.GenreParam, "Thriller"), order: 202),
        BuildRow(ChristmasId, "jux.seasonal.christmas", "12-01", "12-25", "christmas", (SeasonalWidget.TagParam, "christmas"), order: 210),
        BuildRow(ValentinesId, "jux.seasonal.valentines", "02-07", "02-14", "valentines", (SeasonalWidget.TagParam, "valentine's day"), order: 220),
        BuildRow(ValentinesRomanceId, "jux.seasonal.valentines-romance", "02-07", "02-14", "valentines", (SeasonalWidget.GenreParam, "Romance"), order: 221),
        BuildRow(NewYearId, "jux.seasonal.newyear", "12-26", "01-06", "newyear", (SeasonalWidget.TagParam, "new year's eve"), order: 230)
    ];

    private static WidgetConfig BuildRow(
        Guid id,
        string nameKey,
        string startDate,
        string endDate,
        string theme,
        (string Key, string Value) filter,
        int order) => new()
    {
        WidgetType = "jux.connected.seasonal",
        Enabled = false,
        Order = order,
        MinItems = 4,
        MaxItems = 20,
        ViewMode = WidgetViewMode.Portrait,
        MinInstances = 1,
        MaxInstances = 1,
        AllowUserOverride = false,
        ExtraParams =
        [
            new WidgetExtraParam { Key = SeasonalWidget.ValueParam, Value = id.ToString() },
            new WidgetExtraParam { Key = SeasonalWidget.NameKeyParam, Value = nameKey },
            new WidgetExtraParam { Key = SeasonalWidget.StartDateParam, Value = startDate },
            new WidgetExtraParam { Key = SeasonalWidget.EndDateParam, Value = endDate },
            new WidgetExtraParam { Key = SeasonalWidget.ThemeParam, Value = theme },
            new WidgetExtraParam { Key = filter.Key, Value = filter.Value }
        ]
    };
}
