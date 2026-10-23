using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.Localization;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// The English name of each widget type, read from the embedded <c>Localization/en.json</c> where the
/// widget type is the key (QUAL-06.c): the name lives once, next to its translations.
/// </summary>
internal static class WidgetNames
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> _english = new(() =>
    {
        var assembly = typeof(LocalizationService).Assembly;
        using var stream = assembly.GetManifestResourceStream(typeof(LocalizationService).Namespace + ".en.json")
            ?? throw new InvalidOperationException("Localization/en.json is not embedded in the plugin.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    });

    /// <summary>Returns the English name of <paramref name="widgetType"/>, the type itself when en.json has none.</summary>
    /// <param name="widgetType">The widget type, also its key in en.json.</param>
    /// <returns>The English name.</returns>
    internal static string English(string widgetType) =>
        _english.Value.TryGetValue(widgetType, out var name) ? name : widgetType;
}
