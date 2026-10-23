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
        using var stream = typeof(LocalizationService).Assembly
            .GetManifestResourceStream(typeof(LocalizationService).Namespace + ".en.json");
        return Parse(stream);
    });

    /// <summary>Returns the English name of <paramref name="widgetType"/>, the type itself when en.json has none.</summary>
    /// <param name="widgetType">The widget type, also its key in en.json.</param>
    /// <returns>The English name.</returns>
    internal static string English(string widgetType) =>
        _english.Value.TryGetValue(widgetType, out var name) ? name : widgetType;

    /// <summary>
    /// Reads en.json; a missing or unreadable file gives no names, so every widget falls back to its
    /// type, as <see cref="LocalizationService"/> falls back to the raw key
    /// (tests Parse_NoStream_GivesNoNames, Parse_UnreadableJson_GivesNoNames).
    /// </summary>
    /// <param name="stream">The en.json stream, or null when it is not embedded.</param>
    /// <returns>The names by widget type.</returns>
    internal static IReadOnlyDictionary<string, string> Parse(Stream? stream)
    {
        if (stream is null)
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
