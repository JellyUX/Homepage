using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Localization;

public sealed class LocalizationServiceTests
{
    // The section title keys that are not a widget type: the formats of the personalized sections.
    private static readonly string[] FormatKeys =
    [
        "jux.personalized.favorite-genre.format",
        "jux.personalized.favorite-actor.format",
        "jux.personalized.favorite-director.format",
        "jux.personalized.because-you-watched.format"
    ];

    public static TheoryData<string> LanguageFiles() =>
        new(Directory.GetFiles(Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Localization"), "*.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)!);

    // Each language file as written, not merged over English: a key missing from fr.json would
    // otherwise pass through its English fallback. The widget types come from the widget classes, so
    // a new widget needs its name in every language file and in no list of this test (QUAL-06.c).
    [Theory]
    [MemberData(nameof(LanguageFiles))]
    public void RealLanguageFiles_NameEveryWidgetType(string file)
    {
        var path = Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Localization", file);
        var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

        var missing = WidgetTypes.All.Keys.Concat(FormatKeys)
            .Where(key => string.IsNullOrWhiteSpace(dictionary.GetValueOrDefault(key)))
            .Order(StringComparer.Ordinal);

        Assert.Empty(missing);
    }

    // DefaultDisplayName is the en.json name, the one source of the English names (QUAL-06.c).
    [Fact]
    public void DefaultDisplayName_IsTheEnglishNameOfEveryWidgetType()
    {
        var english = new LocalizationService(NullLogger<LocalizationService>.Instance).GetDictionary("en");

        var names = WidgetTypes.All.Values.Select(WidgetTypes.Instance)
            .ToDictionary(w => w.WidgetType, w => w.DefaultDisplayName);

        Assert.Equal(WidgetTypes.All.Keys.ToDictionary(type => type, type => english[type]), names);
    }

    private static LocalizationService BuildService() => new(
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["fr"] = new Dictionary<string, string>
            {
                ["jux.native.my-media"] = "Mes médias",
                ["jux.personalized.favorite-genre.format"] = "Encore du {value}"
            },
            ["en"] = new Dictionary<string, string>
            {
                ["jux.native.my-media"] = "My Media",
                ["jux.native.next-up"] = "Next Up",
                ["jux.personalized.favorite-genre.format"] = "More {value}"
            }
        });

    [Fact]
    public void RealEmbeddedLanguageFiles_AreAllLoaded_AndGiveTheClientTheSameKeys()
    {
        // TODO_V4.md 9.4 (F-64): every embedded Localization/<lang>.json is a language, no list to edit.
        var client = new LocalizationService(NullLogger<LocalizationService>.Instance).GetClientDictionaries();

        Assert.Equal(["en", "fr"], client.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(client["en"].Keys.Order(StringComparer.Ordinal), client["fr"].Keys.Order(StringComparer.Ordinal));
        Assert.True(client["en"].Count > 80);
    }

    [Fact]
    public void GetClientScript_ReadTwice_IsBuiltOnce()
    {
        // Annexe.a: jux-i18n.js used to be serialized again on every request.
        var service = new LocalizationService(NullLogger<LocalizationService>.Instance);

        Assert.Same(service.GetClientScript(), service.GetClientScript());
    }

    [Fact]
    public void Translate_KeyPresentInRequestedLanguage_ReturnsTranslatedValue()
    {
        var service = BuildService();

        Assert.Equal("Mes médias", service.Translate("jux.native.my-media", "fr"));
    }

    [Fact]
    public void Translate_KeyMissingInRequestedLanguage_FallsBackToEnglish()
    {
        var service = BuildService();

        Assert.Equal("Next Up", service.Translate("jux.native.next-up", "fr"));
    }

    [Fact]
    public void Translate_KeyMissingEverywhere_FallsBackToRawKey()
    {
        var service = BuildService();

        Assert.Equal("jux.unknown.widget", service.Translate("jux.unknown.widget", "fr"));
    }

    [Fact]
    public void Translate_ValueProvided_SubstitutesPlaceholder()
    {
        var service = BuildService();

        Assert.Equal("Encore du Action", service.Translate("jux.personalized.favorite-genre.format", "fr", "Action"));
        Assert.Equal("More Drama", service.Translate("jux.personalized.favorite-genre.format", "en", "Drama"));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("FR")]
    [InlineData("fr")]
    public void Translate_LanguageTagVariants_NormalizeToSameResult(string lang)
    {
        var service = BuildService();

        Assert.Equal("Mes médias", service.Translate("jux.native.my-media", lang));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("es-ES")]
    public void Translate_NullOrUnrecognizedLanguage_FallsBackToEnglish(string? lang)
    {
        var service = BuildService();

        Assert.Equal("My Media", service.Translate("jux.native.my-media", lang));
    }

    [Fact]
    public void GetDictionary_RequestedLanguage_MergesOverEnglishBase()
    {
        var service = BuildService();

        var dict = service.GetDictionary("fr");

        Assert.Equal("Mes médias", dict["jux.native.my-media"]);
        Assert.Equal("Next Up", dict["jux.native.next-up"]);
    }

    [Fact]
    public void GetDictionary_EnglishOrUnrecognized_ReturnsEnglishBase()
    {
        var service = BuildService();

        var dict = service.GetDictionary("en");

        Assert.Equal("My Media", dict["jux.native.my-media"]);
    }

    [Theory]
    [InlineData("zz", "en")]
    [InlineData("ZZ-yy", "en")]
    [InlineData(null, "en")]
    [InlineData("fr-FR", "fr")]
    [InlineData("FR", "fr")]
    public void NormalizeLanguage_ReturnsALoadedLanguage(string? lang, string expected)
    {
        var service = new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>(),
            ["fr"] = new Dictionary<string, string>()
        });

        Assert.Equal(expected, service.NormalizeLanguage(lang));
    }
}
