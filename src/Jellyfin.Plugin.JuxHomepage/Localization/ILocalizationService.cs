namespace Jellyfin.Plugin.JuxHomepage.Localization;

/// <summary>
/// Resolves translation keys (widget type identifiers, admin UI and client strings) to text, loaded
/// from the JSON language files embedded in the plugin (<c>Localization/&lt;lang&gt;.json</c>).
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Resolves <paramref name="key"/> for <paramref name="lang"/>.
    /// <para>
    /// <paramref name="lang"/> is normalized (e.g. "fr-FR" -&gt; "fr"); null, empty, or unrecognized
    /// values fall back to English. Lookup order: requested language -&gt; English -&gt; the raw
    /// <paramref name="key"/> itself (never throws, never returns null).
    /// </para>
    /// <para>
    /// When <paramref name="value"/> is provided, the literal token <c>{value}</c> in the resolved
    /// string is replaced with it (used for dynamic names, e.g. "Because you watched {value}").
    /// </para>
    /// </summary>
    /// <param name="key">The translation key (typically a widget's <c>WidgetType</c> or an admin UI key).</param>
    /// <param name="lang">The requested language code, or null to use the default (English).</param>
    /// <param name="value">Optional value substituted for the <c>{value}</c> placeholder.</param>
    /// <returns>The resolved, translated string.</returns>
    string Translate(string key, string? lang, string? value = null);

    /// <summary>
    /// Normalizes a raw language tag (e.g. "fr-FR", "FR", null) to the short code of a loaded
    /// dictionary (e.g. "fr"), or English for null, empty, or unknown values, so a request can never
    /// create a cache entry per arbitrary tag (TODO_V4.md 5.5, F-21).
    /// </summary>
    /// <param name="lang">The raw language tag.</param>
    /// <returns>The short code of a loaded language.</returns>
    string NormalizeLanguage(string? lang);

    /// <summary>
    /// Returns the full flat key-to-string dictionary for a language, for the admin config page to
    /// translate its own UI client-side. English is used as a base, with keys of the requested
    /// language overlaid on top when present.
    /// </summary>
    /// <param name="lang">The requested language code, or null to use the default (English).</param>
    /// <returns>The merged dictionary for the requested language.</returns>
    IReadOnlyDictionary<string, string> GetDictionary(string? lang);

    /// <summary>
    /// Returns the strings of the client scripts (the <c>client.*</c> keys) of every loaded language,
    /// served to the browser as <c>jux-i18n.js</c> (TODO_V4.md 9.4, F-64). Each dictionary holds only
    /// the keys its file defines: the client falls back to English key by key.
    /// </summary>
    /// <returns>The client keys of each language, by short language code.</returns>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetClientDictionaries();

    /// <summary>
    /// Returns <c>jux-i18n.js</c>: the <see cref="GetClientDictionaries"/> of every language as
    /// <c>window.JuxI18n</c>, read synchronously by <c>JuxCore.t</c> and <c>JuxCore.labels</c>.
    /// </summary>
    /// <returns>The script source, built once per service (Annexe.a).</returns>
    string GetClientScript();
}
