using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.Models;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

// Covers TransformationPatches.TryPatchLoadSections, extracted in Phase 9 of TODO_V2.md from the
// reflective PatchLoadSections entry point specifically so this splicing logic -- the most fragile
// mechanism in the project (see CLAUDE.md "Jellyfin Update Procedure") -- could be unit tested without
// reflection or embedded-resource I/O. Fixtures below are short representative stand-ins for the real
// minified chunk; update them if a future Jellyfin Web bump changes the shape of the surrounding code
// around ",loadSections:".
public sealed class TransformationPatchesTests
{
    private const string FragmentTemplate = "/*{{cardbuilder}}{{this_hook}}*/";

    [Fact]
    public void TryPatchLoadSections_ValidChunkWithHook_SplicesFragmentAndReplacesTokens()
    {
        const string raw = "var u={default:1};var n={A:1};,loadSections:function(){},other:2";

        var (content, outcome) = TransformationPatches.TryPatchLoadSections(raw, FragmentTemplate);

        Assert.Equal(LoadSectionsOutcome.Patched, outcome);
        Assert.Contains(",originalLoadSections:", content, StringComparison.Ordinal);
        // The last "var X=" before ",loadSections:" is "n" -- confirms {{this_hook}} resolution.
        Assert.Contains("/*", content, StringComparison.Ordinal);
        Assert.DoesNotContain("{{this_hook}}", content, StringComparison.Ordinal);
        Assert.DoesNotContain("{{cardbuilder}}", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TryPatchLoadSections_NoLoadSectionsMarker_ReturnsContentUnchanged()
    {
        const string raw = "var u={default:1};function unrelated(){return 1;}";

        var (content, outcome) = TransformationPatches.TryPatchLoadSections(raw, FragmentTemplate);

        Assert.Equal(LoadSectionsOutcome.NoMarker, outcome);
        Assert.Equal(raw, content);
    }

    [Fact]
    public void TryPatchLoadSections_MarkerPresentButNoVarDeclaration_ReturnsHookNotFoundAndContentUnchanged()
    {
        // Simulates a Jellyfin Web drift where the module self-reference is no longer declared via
        // "var X=" immediately before the marker (e.g. a minifier switch to a different pattern).
        // Regression test for the Phase 9 fix: this outcome must be reported (StartupService logs a
        // warning), never silently swallowed, and the content must remain untouched (safe fallback).
        const string raw = "someExpression.without.a.var.decl,loadSections:function(){},other:2";

        var (content, outcome) = TransformationPatches.TryPatchLoadSections(raw, FragmentTemplate);

        Assert.Equal(LoadSectionsOutcome.HookNotFound, outcome);
        Assert.Equal(raw, content);
    }

    // -------------------------------------------------------------------------
    // TryPatchHomeHtmlChunk (TODO_V3.md Phase 4.1)
    // -------------------------------------------------------------------------

    // Short representative stand-in for the real minified chunk -- the anchor text itself is the
    // exact literal string confirmed against the real Jellyfin Web bundle deployed on jellyux-test
    // (home-html.*.chunk.js) and independently against jellyfin-plugin-custom-tabs' own source.
    private const string HomeHtmlAnchor = "id=\"favoritesTab\" data-index=\"1\"> <div class=\"sections\"></div> </div>";

    [Fact]
    public void TryPatchHomeHtmlChunk_AnchorPresent_SplicesFourTabPanesInOrderRightAfterAnchor()
    {
        var raw = $"<div class=\"tabContent pageTabContent\" id=\"homeTab\" data-index=\"0\"> <div class=\"sections\"></div> </div> <div class=\"tabContent pageTabContent\" {HomeHtmlAnchor} </div>";

        var (content, outcome) = TransformationPatches.TryPatchHomeHtmlChunk(raw);

        Assert.Equal(HomeHtmlOutcome.Patched, outcome);

        // The 4 panes must appear in this exact order (DOM position drives the native tab-switch
        // visibility toggle, see TransformationPatches.cs comments), each with the matching
        // data-index (2..5) immediately following the native anchor.
        var watchlistIndex = content.IndexOf("id=\"jux-tab-watchlist\" data-index=\"2\"", StringComparison.Ordinal);
        var progressIndex = content.IndexOf("id=\"jux-tab-progress\" data-index=\"3\"", StringComparison.Ordinal);
        var historyIndex = content.IndexOf("id=\"jux-tab-history\" data-index=\"4\"", StringComparison.Ordinal);
        var statisticsIndex = content.IndexOf("id=\"jux-tab-statistics\" data-index=\"5\"", StringComparison.Ordinal);

        Assert.True(watchlistIndex >= 0);
        Assert.True(watchlistIndex < progressIndex);
        Assert.True(progressIndex < historyIndex);
        Assert.True(historyIndex < statisticsIndex);
    }

    [Fact]
    public void TryPatchHomeHtmlChunk_AnchorMissing_ReturnsContentUnchanged()
    {
        const string raw = "<div class=\"tabContent pageTabContent\" id=\"homeTab\" data-index=\"0\"> <div class=\"sections\"></div> </div>";

        var (content, outcome) = TransformationPatches.TryPatchHomeHtmlChunk(raw);

        Assert.Equal(HomeHtmlOutcome.NoMarker, outcome);
        Assert.Equal(raw, content);
    }

    [Fact]
    public void IndexHtml_InjectedAssetUrls_AreRelativeSoTheyFollowBaseUrl()
    {
        var html = TransformationPatches.IndexHtml(new PatchRequestPayload { Contents = "<html><head></head><body></body></html>" });

        // index.html is served under <BaseUrl>/web/, so "../JuxHomepage/" resolves to
        // <BaseUrl>/JuxHomepage/; an absolute "/JuxHomepage/" ignores BaseUrl and receives the web
        // client's HTML fallback instead of the asset.
        Assert.DoesNotContain("\"/JuxHomepage/", html, StringComparison.Ordinal);
        Assert.Equal(13, html.Split("\"../JuxHomepage/").Length - 1);
    }

    [Fact]
    public void ConfigPage_AssetUrls_AreRelativeSoTheyFollowBaseUrl()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.JuxHomepage.Web.config.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var page = reader.ReadToEnd();

        Assert.DoesNotContain("\"/JuxHomepage/", page, StringComparison.Ordinal);
    }

    private const string EmptyIndex = "<html><head></head><body></body></html>";

    private static readonly string[] AllAssetsInOrder =
    [
        "jux-homepage.css", "jux-ui.css", "jux-homepage.js", "jux-ui.js", "jux-tab-injector.js",
        "jux-watchlist.js", "jux-card-hooks.js", "jux-progress.js", "jux-history.js", "jux-statistics.js",
        "jux-series-flatten.js", "jux-collections.js", "jux-breadcrumb.js",
    ];

    // Each module switch, and the assets that consume window.JuxUI.
    public static TheoryData<string> ModuleSwitches => new() { "Tabs", "CardEnhancements", "SeriesFlatten", "Collections", "Breadcrumb" };

    private static readonly string[] JuxUiConsumers = ["jux-watchlist.js", "jux-progress.js", "jux-history.js", "jux-statistics.js", "jux-collections.js"];

    private static PluginConfiguration ConfigWithout(params string[] switches)
    {
        var config = new PluginConfiguration();
        foreach (var name in switches)
        {
            typeof(ModulesConfig).GetProperty(name)!.SetValue(config.Modules, false);
        }

        return config;
    }

    private static List<string> InjectedAssets(string html) =>
        html.Split("../JuxHomepage/").Skip(1).Select(part => part.Split('?')[0]).ToList();

    [Fact]
    public void InjectClientAssets_AllModulesOn_InjectsEveryAssetInDocumentOrder()
    {
        var html = TransformationPatches.InjectClientAssets(EmptyIndex, new PluginConfiguration(), "k");

        Assert.Equal(AllAssetsInOrder, InjectedAssets(html));
    }

    [Fact]
    public void InjectClientAssets_PluginDisabled_LeavesIndexUnchanged()
    {
        var config = new PluginConfiguration { Enabled = false };

        Assert.Equal(EmptyIndex, TransformationPatches.InjectClientAssets(EmptyIndex, config, "k"));
    }

    [Theory]
    [MemberData(nameof(ModuleSwitches))]
    public void InjectClientAssets_OneModuleOff_KeepsTheOrderOfTheOthersAndJuxUiForItsConsumers(string module)
    {
        var assets = InjectedAssets(TransformationPatches.InjectClientAssets(EmptyIndex, ConfigWithout(module), "k"));

        var expectedOrder = AllAssetsInOrder.Where(assets.Contains).ToList();
        Assert.Equal(expectedOrder, assets);
        Assert.True(assets.Count < AllAssetsInOrder.Length);
        if (assets.Any(JuxUiConsumers.Contains))
        {
            Assert.Contains("jux-ui.js", assets);
            Assert.True(assets.IndexOf("jux-ui.js") < assets.FindIndex(JuxUiConsumers.Contains));
        }
    }

    [Fact]
    public void InjectClientAssets_TabsAndCollectionsOff_DropsJuxUi()
    {
        var assets = InjectedAssets(TransformationPatches.InjectClientAssets(EmptyIndex, ConfigWithout("Tabs", "Collections"), "k"));

        Assert.DoesNotContain("jux-ui.js", assets);
        Assert.DoesNotContain("jux-ui.css", assets);
        Assert.Equal(["jux-homepage.css", "jux-homepage.js", "jux-card-hooks.js", "jux-series-flatten.js", "jux-breadcrumb.js"], assets);
    }

    [Fact]
    public void TabsActive_PluginOrTabsModuleOff_SkipsTheHomeTabPanes()
    {
        Assert.True(TransformationPatches.TabsActive(new PluginConfiguration()));
        Assert.False(TransformationPatches.TabsActive(new PluginConfiguration { Enabled = false }));
        Assert.False(TransformationPatches.TabsActive(ConfigWithout("Tabs")));
    }
}
