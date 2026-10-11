using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Outcome of attempting to splice the loadSections override fragment into a chunk.
/// </summary>
internal enum LoadSectionsOutcome
{
    /// <summary>The chunk does not contain the ",loadSections:" marker at all.</summary>
    NoMarker,

    /// <summary>The fragment was spliced in successfully.</summary>
    Patched,

    /// <summary>
    /// The marker is present but the minified module self-reference ("var X=") could not be resolved -
    /// indicates a Jellyfin Web bundle drift. The content is returned unchanged (safe native fallback).
    /// </summary>
    HookNotFound
}

/// <summary>
/// Outcome of attempting to splice the JellyUX tab content panes into the home-html chunk.
/// </summary>
internal enum HomeHtmlOutcome
{
    /// <summary>The chunk does not contain the "favoritesTab" anchor at all.</summary>
    NoMarker,

    /// <summary>The tab panes were spliced in successfully.</summary>
    Patched
}

/// <summary>
/// Static transformation callbacks invoked by the FileTransformation plugin via reflection.
/// Each method receives a <see cref="PatchRequestPayload"/> and returns the transformed content.
/// </summary>
public static class TransformationPatches
{
    // Minified variable names in the Jellyfin 10.11.10 home chunk (56213.*.chunk.js).
    // Verified by extracting the web bundle from jellyfin/jellyfin:10.11.10 and inspecting
    // the module-level var declarations around ",loadSections:".
    // Update these constants when targeting a new Jellyfin web release.
    private const string HookCardBuilder = "u";    // u.default  = cardBuilder
    private const string HookLayoutManager = "n";  // n.A        = layoutManager
    private const string HookShapes = "y";         // y.UI / y.xK / y.zP = backdrop / portrait / square
    private const string HookAppRouter = "p";      // p.appRouter = appRouter
    private const string HookGlobalize = "s";      // s.Ay       = globalize

    private static readonly Regex LastVarRegex =
        new Regex(@"var\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\s*=", RegexOptions.Compiled);

    private const string InjectResourceSuffix = "Web.jux-loadsections-inject.js";

    // The exact literal anchor confirmed by extracting the real Jellyfin Web bundle (home-html
    // chunk) deployed on jellyux-test, and independently confirmed against the published
    // jellyfin-plugin-custom-tabs source (Helpers/TransformationPatches.cs), which targets this
    // exact same string. Marks the end of the native tab content container (index 1, "Favorites").
    private const string HomeHtmlAnchor = "id=\"favoritesTab\" data-index=\"1\"> <div class=\"sections\"></div> </div>";

    // The 4 JellyUX tab panes, in DOM order. Order matters: the native tab-switch mechanism toggles
    // visibility by DOM position within the full .tabContent list (not by data-index attribute
    // matching alone -- confirmed by reading the compiled home.js tab-switch handler), so these must
    // stay in the same order as the buttons created by jux-tab-injector.js (data-index 2..5).
    private static readonly string[] HomeTabIds = ["jux-tab-watchlist", "jux-tab-progress", "jux-tab-history", "jux-tab-statistics"];

    /// <summary>
    /// Injects the JellyUX CSS link and JS script tags into Jellyfin's index.html.
    /// Called by FileTransformation via reflection - must remain public and static.
    /// </summary>
    /// <param name="content">Payload containing the raw index.html contents.</param>
    /// <returns>Transformed HTML with JUX resources injected.</returns>
    public static string IndexHtml(PatchRequestPayload content)
    {
        var version = Plugin.Instance?.Version?.ToString() ?? "0";
        var cacheParam = $"?v={version}";

        // Relative to index.html (served under <BaseUrl>/web/), so the assets follow a configured
        // BaseUrl; an absolute "/JuxHomepage/" would bypass it and receive the web client's HTML.

        var linkTag = $"<link rel=\"stylesheet\" href=\"../JuxHomepage/jux-homepage.css{cacheParam}\" />";
        var uiLinkTag = $"<link rel=\"stylesheet\" href=\"../JuxHomepage/jux-ui.css{cacheParam}\" />";
        var scriptTag = $"<script src=\"../JuxHomepage/jux-homepage.js{cacheParam}\" defer></script>";
        var uiScriptTag = $"<script src=\"../JuxHomepage/jux-ui.js{cacheParam}\" defer></script>";
        var tabInjectorScriptTag = $"<script src=\"../JuxHomepage/jux-tab-injector.js{cacheParam}\" defer></script>";
        var watchlistScriptTag = $"<script src=\"../JuxHomepage/jux-watchlist.js{cacheParam}\" defer></script>";
        var cardHooksScriptTag = $"<script src=\"../JuxHomepage/jux-card-hooks.js{cacheParam}\" defer></script>";
        var progressScriptTag = $"<script src=\"../JuxHomepage/jux-progress.js{cacheParam}\" defer></script>";
        var historyScriptTag = $"<script src=\"../JuxHomepage/jux-history.js{cacheParam}\" defer></script>";
        var statisticsScriptTag = $"<script src=\"../JuxHomepage/jux-statistics.js{cacheParam}\" defer></script>";
        var seriesFlattenScriptTag = $"<script src=\"../JuxHomepage/jux-series-flatten.js{cacheParam}\" defer></script>";
        var collectionsScriptTag = $"<script src=\"../JuxHomepage/jux-collections.js{cacheParam}\" defer></script>";
        var breadcrumbScriptTag = $"<script src=\"../JuxHomepage/jux-breadcrumb.js{cacheParam}\" defer></script>";

        // jux-ui.js must appear (in source order) before the tab-rendering scripts and jux-collections.js
        // that consume window.JuxUI -- deferred scripts execute in document order, so this ordering is
        // what guarantees JuxUI exists by the time those scripts run their own top-level init().
        return (content.Contents ?? string.Empty)
            .Replace("</head>", $"{linkTag}\n{uiLinkTag}\n</head>", StringComparison.OrdinalIgnoreCase)
            .Replace(
                "</body>",
                $"{scriptTag}\n{uiScriptTag}\n{tabInjectorScriptTag}\n{watchlistScriptTag}\n{cardHooksScriptTag}\n{progressScriptTag}\n{historyScriptTag}\n{statisticsScriptTag}\n{seriesFlattenScriptTag}\n{collectionsScriptTag}\n{breadcrumbScriptTag}\n</body>",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Patches the Jellyfin home-html chunk.js to splice in 4 empty JellyUX tab content panes
    /// (Watchlist, populated in Phase 5; Progress/History/Statistics, populated in Phase 6), right
    /// after the native "Favorites" tab pane. Called by FileTransformation via reflection - must
    /// remain public and static.
    /// </summary>
    /// <param name="content">Payload containing the raw home-html chunk.js contents.</param>
    /// <returns>Transformed JS with the JellyUX tab panes spliced in.</returns>
    public static string HomeHtmlChunk(PatchRequestPayload content)
    {
        var raw = content.Contents ?? string.Empty;
        return TryPatchHomeHtmlChunk(raw).Content;
    }

    /// <summary>
    /// Pure splicing logic, decoupled from reflection: attempts to insert the JellyUX tab content
    /// panes right after the native "Favorites" tab pane.
    /// </summary>
    /// <param name="raw">The raw home-html chunk.js contents.</param>
    /// <returns>The (possibly transformed) content, and which outcome produced it.</returns>
    internal static (string Content, HomeHtmlOutcome Outcome) TryPatchHomeHtmlChunk(string raw)
    {
        if (!raw.Contains(HomeHtmlAnchor, StringComparison.Ordinal))
        {
            return (raw, HomeHtmlOutcome.NoMarker);
        }

        // data-index starts at 2: 0 and 1 are already taken by the native "Home"/"Favorites" tabs.
        var panes = string.Concat(HomeTabIds.Select((id, i) =>
            $" <div class=\"tabContent pageTabContent\" id=\"{id}\" data-index=\"{i + 2}\"> <div class=\"sections\"></div> </div>"));

        var patched = raw.Replace(HomeHtmlAnchor, HomeHtmlAnchor + panes, StringComparison.Ordinal);
        return (patched, HomeHtmlOutcome.Patched);
    }

    /// <summary>
    /// Patches the Jellyfin home chunk.js to override loadSections with JUX rendering.
    /// Splices the embedded inject fragment at the ",loadSections:" site, capturing the
    /// minified in-scope modules into window.JellyfinAPI and delegating to window.JUXHomepage.
    /// Called by FileTransformation via reflection - must remain public and static.
    /// </summary>
    /// <param name="content">Payload containing the raw chunk.js contents.</param>
    /// <returns>Transformed JS with loadSections overridden.</returns>
    public static string PatchLoadSections(PatchRequestPayload content)
    {
        var raw = content.Contents ?? string.Empty;

        var template = LoadInjectFragmentTemplate();
        if (template is null)
        {
            return raw;
        }

        return TryPatchLoadSections(raw, template).Content;
    }

    /// <summary>
    /// Reads the embedded loadSections inject fragment template (with unresolved <c>{{...}}</c>
    /// tokens), or null if the embedded resource could not be found.
    /// </summary>
    /// <returns>The raw fragment template text, or null if the resource is missing.</returns>
    internal static string? LoadInjectFragmentTemplate()
    {
        var fragmentStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(
                Assembly.GetExecutingAssembly().GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(InjectResourceSuffix, StringComparison.OrdinalIgnoreCase))
                ?? string.Empty);

        if (fragmentStream is null)
        {
            return null;
        }

        using var reader = new StreamReader(fragmentStream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Pure splicing logic, decoupled from reflection and embedded-resource I/O: attempts to splice
    /// the inject fragment into a chunk's ",loadSections:" site.
    /// </summary>
    /// <param name="raw">The raw chunk.js contents.</param>
    /// <param name="fragmentTemplate">The unresolved fragment template, as returned by <see cref="LoadInjectFragmentTemplate"/>.</param>
    /// <returns>The (possibly transformed) content, and which outcome produced it.</returns>
    internal static (string Content, LoadSectionsOutcome Outcome) TryPatchLoadSections(string raw, string fragmentTemplate)
    {
        if (!raw.Contains(",loadSections:", StringComparison.Ordinal))
        {
            return (raw, LoadSectionsOutcome.NoMarker);
        }

        var parts = raw.Split(",loadSections:", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return (raw, LoadSectionsOutcome.NoMarker);
        }

        // The module self-reference (this) resolves to the last var X= before ,loadSections:
        var matches = LastVarRegex.Matches(parts[0]);
        if (matches.Count == 0)
        {
            return (raw, LoadSectionsOutcome.HookNotFound);
        }

        var thisHook = matches[^1].Groups[1].Value.Trim();

        var fragment = fragmentTemplate
            .Replace("{{cardbuilder}}", HookCardBuilder, StringComparison.Ordinal)
            .Replace("{{layoutmanager}}", HookLayoutManager, StringComparison.Ordinal)
            .Replace("{{shapes}}", HookShapes, StringComparison.Ordinal)
            .Replace("{{approuter}}", HookAppRouter, StringComparison.Ordinal)
            .Replace("{{globalize}}", HookGlobalize, StringComparison.Ordinal)
            .Replace("{{this_hook}}", thisHook, StringComparison.Ordinal);

        var patched = raw.Replace(",loadSections:", $",loadSections:{fragment},originalLoadSections:", StringComparison.Ordinal);
        return (patched, LoadSectionsOutcome.Patched);
    }

    /// <summary>
    /// Scans the web path for the minified chunk.js that defines Jellyfin's loadSections.
    /// </summary>
    /// <param name="webPath">Path to the Jellyfin web directory.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="logger">Logger for reporting results.</param>
    /// <returns>List of matching chunk file paths.</returns>
    public static IReadOnlyList<string> FindLoadSectionsChunks(string webPath, IFileSystem fileSystem, ILogger logger)
    {
        if (!fileSystem.DirectoryExists(webPath))
        {
            logger.LogWarning("Web path does not exist: {WebPath}", webPath);
            return Array.Empty<string>();
        }

        var matches = fileSystem
            .GetFiles(webPath, "*.chunk.js", SearchOption.AllDirectories)
            .Where(f =>
            {
                try
                {
                    return fileSystem.ReadAllText(f).Contains(",loadSections:", StringComparison.Ordinal);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Could not read chunk file: {File}", f);
                    return false;
                }
            })
            .ToList();

        if (matches.Count == 0)
        {
            logger.LogWarning(
                "No chunk.js containing ,loadSections: found in {WebPath}. "
                + "This is expected if Jellyfin Web is not installed.",
                webPath);
        }
        else
        {
            foreach (var match in matches)
            {
                logger.LogInformation(
                    "Found loadSections chunk: {FileName}.",
                    Path.GetFileName(match));
            }
        }

        return matches;
    }
}
