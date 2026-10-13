using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Localization;

/// <summary>
/// Reads the JSON language files embedded in this plugin's assembly (<c>Localization/&lt;lang&gt;.json</c>,
/// every file a language: adding <c>de.json</c> adds German) and resolves translation keys against them.
/// <para>
/// Degrades gracefully like <see cref="TMDb.TMDbApiClient"/>: a missing or malformed language file
/// never throws, it just yields an empty dictionary for that language (logged as a warning), so a
/// single bad file cannot break widget loading or the admin page.
/// </para>
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string DefaultLanguage = "en";

    private const string ClientKeyPrefix = "client.";

    private static readonly string ResourcePrefix = $"{typeof(LocalizationService).Namespace}.";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _dictionaries;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationService"/> class, loading every
    /// <c>Localization/&lt;lang&gt;.json</c> embedded in this assembly.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public LocalizationService(ILogger<LocalizationService> logger)
    {
        var assembly = typeof(LocalizationService).Assembly;
        _dictionaries = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .ToDictionary(
                n => n[ResourcePrefix.Length..^".json".Length].ToLowerInvariant(),
                n => LoadFromEmbeddedResource(assembly, n, logger));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationService"/> class with pre-parsed
    /// dictionaries, for unit tests that exercise <see cref="Translate"/>'s lookup/fallback logic
    /// without depending on the real embedded JSON files.
    /// </summary>
    /// <param name="dictionaries">Per-language key-to-string dictionaries.</param>
    internal LocalizationService(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> dictionaries)
    {
        _dictionaries = dictionaries;
    }

    /// <inheritdoc/>
    public string Translate(string key, string? lang, string? value = null)
    {
        var normalized = NormalizeLanguage(lang);

        var resolved = LookUp(normalized, key)
            ?? LookUp(DefaultLanguage, key)
            ?? key;

        return value is null ? resolved : resolved.Replace("{value}", value, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetDictionary(string? lang)
    {
        var normalized = NormalizeLanguage(lang);

        var merged = new Dictionary<string, string>();
        if (_dictionaries.TryGetValue(DefaultLanguage, out var en))
        {
            foreach (var (k, v) in en)
            {
                merged[k] = v;
            }
        }

        if (normalized != DefaultLanguage && _dictionaries.TryGetValue(normalized, out var requested))
        {
            foreach (var (k, v) in requested)
            {
                merged[k] = v;
            }
        }

        return merged;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetClientDictionaries() =>
        _dictionaries.ToDictionary(
            d => d.Key,
            d => (IReadOnlyDictionary<string, string>)d.Value
                .Where(e => e.Key.StartsWith(ClientKeyPrefix, StringComparison.Ordinal))
                .ToDictionary(e => e.Key, e => e.Value));

    /// <inheritdoc />
    public string NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return DefaultLanguage;
        }

        var shortCode = lang.Split('-', 2)[0].ToLowerInvariant();
        return _dictionaries.ContainsKey(shortCode) ? shortCode : DefaultLanguage;
    }

    private string? LookUp(string lang, string key) =>
        _dictionaries.TryGetValue(lang, out var dictionary) && dictionary.TryGetValue(key, out var value)
            ? value
            : null;

    private static IReadOnlyDictionary<string, string> LoadFromEmbeddedResource(
        Assembly assembly,
        string name,
        ILogger logger)
    {
        var suffix = name[ResourcePrefix.Length..];
        try
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                logger.LogWarning("Localization file '{Suffix}' could not be opened as a stream.", suffix);
                return new Dictionary<string, string>();
            }

            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(stream, SerializerOptions);
            return result ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load or parse localization file '{Suffix}'.", suffix);
            return new Dictionary<string, string>();
        }
    }
}
