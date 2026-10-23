namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Standard view mode identifiers for widget card rendering.
/// Use these constants instead of magic strings when setting <see cref="WidgetConfig.ViewMode"/> or
/// <see cref="WidgetDescriptor.ViewMode"/>.
/// </summary>
public static class WidgetViewMode
{
    /// <summary>Tall portrait cards (e.g. movie and series posters).</summary>
    public const string Portrait = "Portrait";

    /// <summary>Wide landscape cards (e.g. episode backdrops).</summary>
    public const string Landscape = "Landscape";

    /// <summary>Square cards (e.g. music albums, people).</summary>
    public const string Square = "Square";

    /// <summary>
    /// Returns the known view mode <paramref name="viewMode"/> names, ignoring case, otherwise
    /// <see cref="Landscape"/>, the <see cref="WidgetConfig.ViewMode"/> default.
    /// </summary>
    /// <param name="viewMode">The stored value.</param>
    /// <returns>A known view mode.</returns>
    public static string Normalize(string? viewMode) =>
        string.Equals(viewMode, Portrait, StringComparison.OrdinalIgnoreCase) ? Portrait
        : string.Equals(viewMode, Square, StringComparison.OrdinalIgnoreCase) ? Square
        : Landscape;
}
