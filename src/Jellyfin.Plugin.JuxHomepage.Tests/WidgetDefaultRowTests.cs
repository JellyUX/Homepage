using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

// QUAL-03.c: the row the admin page adds for a widget type comes from the server (Admin/Catalog
// DefaultRow), so the page no longer copies the server's defaults.
public sealed class WidgetDefaultRowTests
{
    public static TheoryData<string, string> Defaults() => new()
    {
        { "jux.admin.genre", string.Empty },
        { "jux.native.next-up", string.Empty },
        { "jux.connected.trending-movies", string.Empty },
        { "jux.connected.discover-movies", "value=;sortBy=popularity.desc;minVotes=50;pages=1" },
        { "jux.connected.rewards", "value=" },
        { "jux.connected.seasonal", "value=" },
        { "jux.local.top-rated", "itemTypes=Movie,Series" },
        { "jux.personalized.favorite-genre", "excludeWatched=true" },
        { "jux.personalized.because-you-watched", "excludeWatched=true" },
    };

    [Theory]
    [MemberData(nameof(Defaults))]
    public void DefaultExtraParams_AreTheParametersOfANewRow(string widgetType, string expected)
    {
        var widget = WidgetTypes.Instance(WidgetTypes.All[widgetType]);

        Assert.Equal(expected, string.Join(';', widget.DefaultExtraParams.Select(p => $"{p.Key}={p.Value}")));
    }

    [Fact]
    public void DefaultExtraParams_UseOnlyTheKeysOfTheContract()
    {
        var path = Path.Combine(RepoPaths.Root(), "test", "fixtures", "extraparams-contract.json");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var contract = json.RootElement.GetProperty("widgets");

        var strays = WidgetTypes.All
            .SelectMany(pair => WidgetTypes.Instance(pair.Value).DefaultExtraParams
                .Where(p => !contract.GetProperty(pair.Key).EnumerateArray().Any(k => k.GetString() == p.Key))
                .Select(p => $"{pair.Key}: {p.Key}"))
            .ToList();

        Assert.Empty(strays);
    }
}

// R11 review of phase 09: a missing or unreadable en.json leaves the widget types as their names
// (as LocalizationService degrades), instead of a Lazy that throws on every later call.
public sealed class WidgetNamesTests
{
    [Fact]
    public void Parse_NoStream_GivesNoNames() => Assert.Empty(WidgetNames.Parse(null));

    [Fact]
    public void Parse_UnreadableJson_GivesNoNames() =>
        Assert.Empty(WidgetNames.Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{ not json"))));
}
