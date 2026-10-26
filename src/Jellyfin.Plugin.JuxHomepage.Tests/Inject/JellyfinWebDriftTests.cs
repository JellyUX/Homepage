using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

// CI guard (TODO_V4.md 3.3, F-11): detects a Jellyfin Web bundle drift against everything JellyUX patches
// or depends on, before it silently breaks for users. Reuses the production code paths
// (TransformationPatches.FindLoadSectionsChunks/TryPatchLoadSections/CheckHomeHtmlChunks) instead of
// re-implementing the detection, and checks every native token of Web/jux-native-selectors.json.
//
// Needs the web client extracted from the target image (jellyfin/jellyfin:10.11.11, the version the
// hook constant is validated for): tagged so the normal `dotnet test` run skips it; the "drift" job of
// ci.yml extracts the bundle, sets JELLYFIN_WEB_EXTRACT_PATH and runs it as part of the release gate.
[Trait("Category", "RequiresDocker")]
public sealed class JellyfinWebDriftTests
{
    private static string WebPath()
    {
        var webPath = Environment.GetEnvironmentVariable("JELLYFIN_WEB_EXTRACT_PATH");
        if (string.IsNullOrEmpty(webPath))
        {
            throw new InvalidOperationException(
                "JELLYFIN_WEB_EXTRACT_PATH must be set to the extracted jellyfin-web directory. "
                + "See the drift job of .github/workflows/ci.yml, which sets it before running this test.");
        }

        return webPath;
    }

    [Fact]
    public void LoadSectionsSplice_StillResolvesAgainstTheTargetBundle()
    {
        var fileSystem = new FileSystem();
        var chunks = TransformationPatches.FindLoadSectionsChunks(WebPath(), fileSystem, NullLogger.Instance);

        Assert.NotEmpty(chunks);

        var template = TransformationPatches.LoadInjectFragmentTemplate();
        Assert.NotNull(template);

        foreach (var chunkPath in chunks)
        {
            var content = fileSystem.ReadAllText(chunkPath);
            var (_, outcome) = TransformationPatches.TryPatchLoadSections(content, template!);

            // HookNotFound means a token the splice needs (module self-reference, cardBuilder binding
            // or its .default read) is gone from this build. See CONTRIBUTING.md "Jellyfin Update Procedure".
            Assert.Equal(LoadSectionsOutcome.Patched, outcome);
        }
    }

    [Fact]
    public void HomeHtmlAnchor_IsPresentInTheTargetBundle()
    {
        Assert.True(TransformationPatches.CheckHomeHtmlChunks(WebPath(), new FileSystem(), NullLogger.Instance));
    }

    private static string Bundle() => string.Concat(Directory
        .EnumerateFiles(WebPath(), "*", SearchOption.AllDirectories)
        .Where(f => f.EndsWith(".js", StringComparison.Ordinal)
            || f.EndsWith(".css", StringComparison.Ordinal)
            || f.EndsWith(".html", StringComparison.Ordinal))
        .Select(File.ReadAllText));

    private static JsonDocument Inventory() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web", "jux-native-selectors.json")));

    [Fact]
    public void EveryInventoriedNativeToken_AppearsInTheTargetBundle()
    {
        var bundle = Bundle();
        using var inventory = Inventory();

        // A name must stand on its own: "actionSheetScrollerX" does not keep "actionSheetScroller" alive.
        bool IsPresent(string key, string token) => key == "anchors"
            ? bundle.Contains(token, StringComparison.Ordinal)
            : Regex.IsMatch(bundle, $@"(?<![\w-]){Regex.Escape(token)}(?![\w-])");

        var missing = new[] { "classes", "ids", "attributes", "elements", "anchors" }
            .SelectMany(key => inventory.RootElement.GetProperty(key).EnumerateArray().Select(t => (Key: key, Token: t.GetString()!)))
            .Where(entry => !IsPresent(entry.Key, entry.Token))
            .Select(entry => $"{entry.Key}: {entry.Token}")
            .ToList();

        Assert.Empty(missing);
    }

    // An attribute value a selector matches on ([data-id="resume"]) is set by the bundle: each one names
    // the bundle fragment that sets it (action sheet entries take data-id from their id).
    [Fact]
    public void EveryInventoriedAttributeValue_IsSetInTheTargetBundle()
    {
        var bundle = Bundle();
        using var inventory = Inventory();

        var missing = inventory.RootElement.GetProperty("attributeValues").EnumerateArray()
            .Select(v => (Attribute: v.GetProperty("attribute").GetString()!, Value: v.GetProperty("value").GetString()!, Evidence: v.GetProperty("evidence").GetString()!))
            .Where(v => !v.Evidence.Contains('"' + v.Value + '"', StringComparison.Ordinal)
                || !bundle.Contains(v.Evidence, StringComparison.Ordinal))
            .Select(v => $"{v.Attribute}={v.Value}: {v.Evidence}")
            .ToList();

        Assert.Empty(missing);
    }

    // FIAB-05: a document-level class is read on the element Jellyfin sets it on ("layout-tv" on <html>,
    // not <body>). Each carrier names that element and the bundle fragment that sets the class there.
    [Fact]
    public void EveryInventoriedCarrier_IsSetOnItsElementInTheTargetBundle()
    {
        var bundle = Bundle();
        using var inventory = Inventory();

        var wrong = inventory.RootElement.GetProperty("carriers").EnumerateArray()
            .Select(c => (Class: c.GetProperty("class").GetString()!, Element: c.GetProperty("element").GetString()!, Evidence: c.GetProperty("evidence").GetString()!))
            .Where(c => !c.Evidence.Contains(c.Element + ".classList", StringComparison.Ordinal)
                || !bundle.Contains(c.Evidence, StringComparison.Ordinal))
            .Select(c => $"{c.Class} on {c.Element}: {c.Evidence}")
            .ToList();

        Assert.Empty(wrong);
    }
}
