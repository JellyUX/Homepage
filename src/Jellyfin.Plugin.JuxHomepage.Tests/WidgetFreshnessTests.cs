using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// REV-04.a (DR-032): every widget declares how fresh its section must be, and a direct section is one the home
/// screen never paints from a server page cache, because the client reads it natively or always asks the server.
/// </summary>
public sealed class WidgetFreshnessTests
{
    private static readonly string[] ExpectedDirect =
    [
        "jux.native.continue-watching",
        "jux.native.next-up",
        "jux.native.recently-added-movies",
        "jux.native.recently-added-shows",
        "jux.native.recently-added-episodes",
        "jux.native.watchlist",
    ];

    [Fact]
    public void EveryWidget_DeclaresAFreshnessLevel()
    {
        Assert.All(WidgetTypes.All, pair =>
            Assert.True(
                Enum.IsDefined(WidgetTypes.Instance(pair.Value).Freshness),
                $"{pair.Key} declares no freshness level"));
    }

    [Fact]
    public void DirectWidgets_AreThePlaybackRecentlyAddedAndWatchlistRows()
    {
        var direct = WidgetTypes.All
            .Where(pair => WidgetTypes.Instance(pair.Value).Freshness == FreshnessLevel.Direct)
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ExpectedDirect.Order(StringComparer.Ordinal), direct);
    }

    [Fact]
    public void DirectWidgets_AreReadNativelyOrAlwaysReadFromTheServerByTheClient()
    {
        var liveRows = ClientLiveRows();

        Assert.All(ExpectedDirect, type =>
            Assert.True(
                ReadsNatively(WidgetTypes.All[type]) || liveRows.Contains(type),
                $"{type} is direct but neither read natively nor in LIVE_ROWS"));
    }

    [Fact]
    public void ClientLiveRows_AreAllDirectWidgets()
    {
        var liveRows = ClientLiveRows();

        Assert.NotEmpty(liveRows);
        Assert.All(liveRows, type =>
            Assert.Equal(FreshnessLevel.Direct, WidgetTypes.Instance(WidgetTypes.All[type]).Freshness));
    }

    private static bool ReadsNatively(Type widget)
    {
        var property = widget.GetProperty("ReadNatively", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)property.GetValue(WidgetTypes.Instance(widget))!;
    }

    private static HashSet<string> ClientLiveRows()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web", "jux-homepage.js"));
        var list = Regex.Match(script, @"var LIVE_ROWS = \[([^\]]*)\];");
        Assert.True(list.Success, "LIVE_ROWS not found in jux-homepage.js");
        return Regex.Matches(list.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }
}
