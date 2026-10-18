using System.Text.RegularExpressions;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

// Covers TransformationPatches.TryPatchLoadSections, extracted in Phase 9 of TODO_V2.md from the
// reflective PatchLoadSections entry point specifically so this splicing logic -- the most fragile
// mechanism in the project (see CONTRIBUTING.md "Jellyfin Update Procedure") -- could be unit tested without
// reflection or embedded-resource I/O. Fixtures below are short representative stand-ins for the real
// minified chunk; update them if a future Jellyfin Web bump changes the shape of the surrounding code
// around ",loadSections:".
public sealed class TransformationPatchesTests
{
    private const string FragmentTemplate = "/*{{cardbuilder}}{{this_hook}}*/";

    [Fact]
    public void TryPatchLoadSections_ValidChunkWithHook_SplicesFragmentAndReplacesTokens()
    {
        const string raw = "var u=r(1);var n={A:function(){return u.default}};,loadSections:function(){},other:2";

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

    [Theory]
    // cardBuilder never bound: the fragment would read an unrelated or undefined "u".
    [InlineData("var x=r(1);var n={A:function(){return x.default}};,loadSections:function(){},o:2")]
    // Bound but never read through .default: the export shape changed.
    [InlineData("var u=r(1);var n={A:function(){return u.Ay}};,loadSections:function(){},o:2")]
    // Bound and read only in another module of the chunk, not in the one defining loadSections.
    [InlineData("1:function(e,t,r){var u=r(1);u.default.x()},2:function(e,t,r){var n=r(3);var x={a:1,loadSections:function(){}}}")]
    public void TryPatchLoadSections_CardBuilderTokenMissingFromModule_ReturnsHookNotFoundAndContentUnchanged(string raw)
    {
        var (content, outcome) = TransformationPatches.TryPatchLoadSections(raw, FragmentTemplate);

        Assert.Equal(LoadSectionsOutcome.HookNotFound, outcome);
        Assert.Equal(raw, content);
    }

    [Fact]
    public void TryPatchLoadSections_CardBuilderBoundInTheLoadSectionsModule_Patches()
    {
        const string raw = "1:function(e,t,r){var x=r(9)},56213:function(e,t,r){var n=r(4),u=r(24468);function h(){return u.default.getCardsHtml()}var x={a:1,loadSections:function(){}}}";

        var (_, outcome) = TransformationPatches.TryPatchLoadSections(raw, FragmentTemplate);

        Assert.Equal(LoadSectionsOutcome.Patched, outcome);
    }

    [Fact]
    public void CheckHomeHtmlChunks_AnchorPresent_ReturnsTrue()
    {
        var fileSystem = HomeHtmlFileSystem("x" + HomeHtmlAnchor + "y");

        Assert.True(TransformationPatches.CheckHomeHtmlChunks("/web", fileSystem, NullLogger.Instance));
    }

    [Fact]
    public void CheckHomeHtmlChunks_AnchorMissing_ReturnsFalse()
    {
        var fileSystem = HomeHtmlFileSystem("id=\"favoritesTab\" data-index=\"1\"><div class=\"sections\"></div></div>");

        Assert.False(TransformationPatches.CheckHomeHtmlChunks("/web", fileSystem, NullLogger.Instance));
    }

    [Fact]
    public void CheckHomeHtmlChunks_NoHomeHtmlChunk_ReturnsFalse()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.DirectoryExists("/web")).Returns(true);
        fileSystem.Setup(f => f.GetFiles("/web", "home-html.*.chunk.js", SearchOption.AllDirectories)).Returns([]);

        Assert.False(TransformationPatches.CheckHomeHtmlChunks("/web", fileSystem.Object, NullLogger.Instance));
    }

    private static IFileSystem HomeHtmlFileSystem(string content)
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.DirectoryExists("/web")).Returns(true);
        fileSystem.Setup(f => f.GetFiles("/web", "home-html.*.chunk.js", SearchOption.AllDirectories))
            .Returns(["/web/home-html.abc.chunk.js"]);
        fileSystem.Setup(f => f.ReadAllText("/web/home-html.abc.chunk.js")).Returns(content);
        return fileSystem.Object;
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
        Assert.Equal(16, html.Split("\"../JuxHomepage/").Length - 1);
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
        "jux-homepage.css", "jux-ui.css", "jux-i18n.js", "jux-core.js", "jux-homepage.js", "jux-user-settings.js", "jux-ui.js", "jux-tab-injector.js",
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
    public void InjectClientAssets_CardEnhancementsOff_DropsOnlyTheCardHooks() =>
        AssertOnlyDropped("CardEnhancements", "jux-card-hooks.js");

    [Fact]
    public void InjectClientAssets_SeriesFlattenOff_DropsOnlyTheSeriesFlattener() =>
        AssertOnlyDropped("SeriesFlatten", "jux-series-flatten.js");

    [Fact]
    public void InjectClientAssets_BreadcrumbOff_DropsOnlyTheBreadcrumb() =>
        AssertOnlyDropped("Breadcrumb", "jux-breadcrumb.js");

    private static void AssertOnlyDropped(string module, string asset) =>
        Assert.Equal(
            AllAssetsInOrder.Where(a => a != asset),
            InjectedAssets(TransformationPatches.InjectClientAssets(EmptyIndex, ConfigWithout(module), "k")));

    [Fact]
    public void InjectClientAssets_TabsAndCollectionsOff_DropsJuxUi()
    {
        var assets = InjectedAssets(TransformationPatches.InjectClientAssets(EmptyIndex, ConfigWithout("Tabs", "Collections"), "k"));

        Assert.DoesNotContain("jux-ui.js", assets);
        Assert.DoesNotContain("jux-ui.css", assets);
        Assert.Equal(["jux-homepage.css", "jux-i18n.js", "jux-core.js", "jux-homepage.js", "jux-user-settings.js", "jux-card-hooks.js", "jux-series-flatten.js", "jux-breadcrumb.js"], assets);
    }

    [Fact]
    public void TabsActive_PluginOrTabsModuleOff_SkipsTheHomeTabPanes()
    {
        Assert.True(TransformationPatches.TabsActive(new PluginConfiguration()));
        Assert.False(TransformationPatches.TabsActive(new PluginConfiguration { Enabled = false }));
        Assert.False(TransformationPatches.TabsActive(ConfigWithout("Tabs")));
    }

    // -------------------------------------------------------------------------
    // Reflective entry points (Plugin.Instance is never set in this test assembly, so they run on
    // the default configuration: plugin enabled, every module on)
    // -------------------------------------------------------------------------

    private const string LoadSectionsChunk = "56213:function(e,t,r){var n=r(4),u=r(24468);function h(){return u.default.getCardsHtml()}var x={a:1,loadSections:function(){}}}";

    [Fact]
    public void LoadInjectFragmentTemplate_HoldsExactlyTheTwoResolvedTokens()
    {
        var template = TransformationPatches.LoadInjectFragmentTemplate();

        Assert.NotNull(template);
        var tokens = Regex.Matches(template!, @"\{\{[^{}]*\}\}").Select(m => m.Value).Distinct().Order();
        Assert.Equal(["{{cardbuilder}}", "{{this_hook}}"], tokens);
    }

    [Fact]
    public void PatchLoadSections_RealFragment_ResolvesEveryTokenAgainstTheChunk()
    {
        var patched = TransformationPatches.PatchLoadSections(new PatchRequestPayload { Contents = LoadSectionsChunk });

        var marker = LoadSectionsChunk.IndexOf(",loadSections:", StringComparison.Ordinal) + ",loadSections:".Length;
        Assert.StartsWith(LoadSectionsChunk[..marker], patched, StringComparison.Ordinal);
        Assert.EndsWith(",originalLoadSections:function(){}}}", patched, StringComparison.Ordinal);
        Assert.Contains("function(e, t, r, o, page)", patched, StringComparison.Ordinal);
        Assert.Contains("cardBuilder: u.default", patched, StringComparison.Ordinal);
        // "x" is the last "var X=" before the marker: the module object holding loadSections.
        Assert.Contains("x.originalLoadSections.call(this, e, t, r, o)", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", patched, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var u=r(1);function f(){return u.default}")]
    [InlineData("")]
    [InlineData(null)]
    public void PatchLoadSections_NoMarker_ReturnsTheInputUnchanged(string? raw)
    {
        var patched = TransformationPatches.PatchLoadSections(new PatchRequestPayload { Contents = raw });

        Assert.Equal(raw ?? string.Empty, patched);
    }

    [Fact]
    public void IndexHtml_DefaultConfiguration_InjectsEveryAssetAtTheEndOfHeadAndBody()
    {
        var html = TransformationPatches.IndexHtml(new PatchRequestPayload { Contents = EmptyIndex });

        Assert.Equal(AllAssetsInOrder, InjectedAssets(html));
        var key = ClientAssetVersion.Current;
        Assert.Contains($"<link rel=\"stylesheet\" href=\"../JuxHomepage/jux-ui.css?v={key}\" />\n</head>", html, StringComparison.Ordinal);
        Assert.Contains($"<script src=\"../JuxHomepage/jux-breadcrumb.js?v={key}\" defer></script>\n</body>", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("</head>", StringComparison.Ordinal) < html.IndexOf("<script", StringComparison.Ordinal));
    }

    [Fact]
    public void IndexHtml_NullContents_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TransformationPatches.IndexHtml(new PatchRequestPayload { Contents = null }));
    }

    [Fact]
    public void HomeHtmlChunk_DefaultConfiguration_SplicesTheTabPanesAfterTheAnchor()
    {
        var patched = TransformationPatches.HomeHtmlChunk(new PatchRequestPayload { Contents = "a" + HomeHtmlAnchor + "b" });

        Assert.StartsWith("a" + HomeHtmlAnchor + " <div class=\"tabContent pageTabContent\" id=\"jux-tab-watchlist\" data-index=\"2\">", patched, StringComparison.Ordinal);
        Assert.EndsWith("id=\"jux-tab-statistics\" data-index=\"5\"> <div class=\"sections\"></div> </div>b", patched, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id=\"homeTab\" data-index=\"0\">")]
    [InlineData(null)]
    public void HomeHtmlChunk_AnchorMissing_ReturnsTheInputUnchanged(string? raw)
    {
        Assert.Equal(raw ?? string.Empty, TransformationPatches.HomeHtmlChunk(new PatchRequestPayload { Contents = raw }));
    }

    // -------------------------------------------------------------------------
    // FindLoadSectionsChunks
    // -------------------------------------------------------------------------

    private static Mock<IFileSystem> ChunkFileSystem(params (string Path, string? Content)[] chunks)
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.DirectoryExists("/web")).Returns(true);
        fileSystem.Setup(f => f.GetFiles("/web", "*.chunk.js", SearchOption.AllDirectories))
            .Returns(chunks.Select(c => c.Path).ToList());
        foreach (var (path, content) in chunks)
        {
            if (content is null)
            {
                fileSystem.Setup(f => f.ReadAllText(path)).Throws(new IOException("locked"));
            }
            else
            {
                fileSystem.Setup(f => f.ReadAllText(path)).Returns(content);
            }
        }

        return fileSystem;
    }

    [Fact]
    public void FindLoadSectionsChunks_MissingWebDirectory_ReturnsEmptyWithoutListingFiles()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.DirectoryExists("/web")).Returns(false);

        Assert.Empty(TransformationPatches.FindLoadSectionsChunks("/web", fileSystem.Object, NullLogger.Instance));
        fileSystem.Verify(f => f.GetFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()), Times.Never);
    }

    [Fact]
    public void FindLoadSectionsChunks_ReturnsOnlyTheChunksDefiningLoadSections()
    {
        var fileSystem = ChunkFileSystem(
            ("/web/1.chunk.js", "var a={loadSections:1}"),
            ("/web/56213.chunk.js", LoadSectionsChunk),
            ("/web/sub/2.chunk.js", "var b=2"));

        var chunks = TransformationPatches.FindLoadSectionsChunks("/web", fileSystem.Object, NullLogger.Instance);

        Assert.Equal(["/web/56213.chunk.js"], chunks);
    }

    [Fact]
    public void FindLoadSectionsChunks_UnreadableChunk_IsSkippedAndTheOthersStillScanned()
    {
        var fileSystem = ChunkFileSystem(
            ("/web/locked.chunk.js", null),
            ("/web/56213.chunk.js", LoadSectionsChunk),
            ("/web/other.chunk.js", "x,loadSections:y"));

        var chunks = TransformationPatches.FindLoadSectionsChunks("/web", fileSystem.Object, NullLogger.Instance);

        Assert.Equal(["/web/56213.chunk.js", "/web/other.chunk.js"], chunks);
    }

    [Fact]
    public void FindLoadSectionsChunks_NoMatchingChunk_ReturnsEmpty()
    {
        var fileSystem = ChunkFileSystem(("/web/1.chunk.js", "var a=1"), ("/web/2.chunk.js", "loadSections"));

        Assert.Empty(TransformationPatches.FindLoadSectionsChunks("/web", fileSystem.Object, NullLogger.Instance));
    }
}
