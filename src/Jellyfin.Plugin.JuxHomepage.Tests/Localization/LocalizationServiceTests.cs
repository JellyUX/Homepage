using Jellyfin.Plugin.JuxHomepage.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Localization;

public sealed class LocalizationServiceTests
{
    // Every WidgetType currently registered in the plugin (kept in sync manually -- deliberately not
    // generated from WidgetRegistry.GetAll(), since populating a real registry requires a full DI
    // container with mocks for every widget's constructor dependencies; see TODO_V3.md Phase 3.6 for
    // the trade-off). A new widget must add its translation key to fr.json/en.json AND to this list,
    // so a forgotten key fails this test rather than silently showing a raw key in production --
    // exactly the gap that let "jux.connected.rewards" go missing from both this list and the
    // language files until this phase caught it.
    private static readonly string[] KnownWidgetTypes =
    [
        "jux.native.continue-watching",
        "jux.native.next-up",
        "jux.native.recently-added-movies",
        "jux.native.recently-added-shows",
        "jux.native.my-media",
        "jux.connected.trending-movies",
        "jux.connected.trending-shows",
        "jux.connected.top-rated-movies",
        "jux.connected.top-rated-shows",
        "jux.connected.airing-today",
        "jux.connected.now-playing-movies",
        "jux.connected.discover-movies",
        "jux.connected.rewards",
        "jux.admin.genre",
        "jux.admin.actor",
        "jux.admin.director",
        "jux.admin.studio",
        "jux.admin.collection",
        "jux.admin.tag",
        "jux.admin.year",
        "jux.personalized.favorite-genre",
        "jux.personalized.favorite-genre.format",
        "jux.personalized.favorite-actor",
        "jux.personalized.favorite-actor.format",
        "jux.personalized.favorite-director",
        "jux.personalized.favorite-director.format",
        "jux.personalized.because-you-watched",
        "jux.personalized.because-you-watched.format"
    ];

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void RealEmbeddedLanguageFiles_ContainEveryKnownWidgetTypeKey(string lang)
    {
        var service = new LocalizationService(NullLogger<LocalizationService>.Instance);
        var dictionary = service.GetDictionary(lang);

        foreach (var widgetType in KnownWidgetTypes)
        {
            Assert.True(
                dictionary.ContainsKey(widgetType),
                $"Missing '{lang}' translation for widget type '{widgetType}'.");
        }
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
