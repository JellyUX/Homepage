namespace Jellyfin.Plugin.JuxHomepage.Widgets.Local;

/// <summary>
/// The preset rows of the local library sections (TODO_V4.md 9.1, F-61), disabled like the seasonal
/// presets: an update changes nothing on the home screen until an admin turns one on.
/// </summary>
public static class LocalWidgetDefaults
{
    /// <summary>
    /// Builds the preset rows: Recently Released (movies) and Top Rated in Library (movies and series).
    /// </summary>
    /// <returns>A fixed-order array of two <see cref="WidgetConfig"/> entries.</returns>
    public static WidgetConfig[] Build() =>
    [
        BuildRow("jux.local.recently-released", "Movie", order: 100),
        BuildRow("jux.local.top-rated", "Movie,Series", order: 110)
    ];

    private static WidgetConfig BuildRow(string widgetType, string itemTypes, int order) => new()
    {
        WidgetType = widgetType,
        Enabled = false,
        Order = order,
        MinItems = 4,
        MaxItems = 20,
        ViewMode = WidgetViewMode.Portrait,
        MinInstances = 1,
        MaxInstances = 5,
        AllowUserOverride = false,
        ExtraParams = [new WidgetExtraParam { Key = LocalLibraryWidgetBase.ItemTypesParam, Value = itemTypes }]
    };
}
