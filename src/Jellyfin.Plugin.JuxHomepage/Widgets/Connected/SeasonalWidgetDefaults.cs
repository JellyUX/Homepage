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
        BuildRow(HalloweenId, "Halloween", "10-01", "10-31", "halloween", ("tag", "halloween"), order: 200),
        BuildRow(HalloweenHorrorId, "Halloween Horror", "10-01", "10-31", "halloween", ("genre", "Horror|Horreur"), order: 201),
        BuildRow(HalloweenThrillerId, "Halloween Thrillers", "10-01", "10-31", "halloween", ("genre", "Thriller"), order: 202),
        BuildRow(ChristmasId, "Christmas", "12-01", "12-25", "christmas", ("tag", "christmas"), order: 210),
        BuildRow(ValentinesId, "Valentine's Day", "02-07", "02-14", "valentines", ("tag", "valentine's day"), order: 220),
        BuildRow(ValentinesRomanceId, "Valentine's Romance", "02-07", "02-14", "valentines", ("genre", "Romance"), order: 221),
        BuildRow(NewYearId, "New Year", "12-26", "01-06", "newyear", ("tag", "new year's eve"), order: 230)
    ];

    private static WidgetConfig BuildRow(
        Guid id,
        string displayName,
        string startDate,
        string endDate,
        string theme,
        (string Key, string Value) filter,
        int order) => new()
    {
        WidgetType = "jux.connected.seasonal",
        CustomDisplayName = displayName,
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
            new WidgetExtraParam { Key = "value", Value = id.ToString() },
            new WidgetExtraParam { Key = "startDate", Value = startDate },
            new WidgetExtraParam { Key = "endDate", Value = endDate },
            new WidgetExtraParam { Key = "theme", Value = theme },
            new WidgetExtraParam { Key = filter.Key, Value = filter.Value }
        ]
    };
}
