using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JuxHomepage.Configuration;
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
    /// The marker is present but an expected minified token is missing from the module that defines
    /// loadSections: the module self-reference ("var X="), the cardBuilder binding or its ".default"
    /// read. Indicates a Jellyfin Web bundle drift. The content is returned unchanged (native fallback).
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
/// One client asset served under /JuxHomepage/ and referenced from index.html.
/// </summary>
/// <param name="FileName">The asset file name.</param>
/// <param name="IsStylesheet">True for a CSS link in the head, false for a deferred script.</param>
/// <param name="Module">The module switch that enables the asset, or null for a core asset.</param>
/// <param name="IsJuxUi">True for the shared jux-ui assets, loaded only when a consumer is.</param>
/// <param name="UsesJuxUi">True when the asset consumes <c>window.JuxUI</c>.</param>
/// <param name="Needed">A further condition on the configuration, or null when the module decides alone.</param>
internal sealed record ClientAsset(
    string FileName,
    bool IsStylesheet,
    Func<ModulesConfig, bool>? Module,
    bool IsJuxUi = false,
    bool UsesJuxUi = false,
    Func<PluginConfiguration, bool>? Needed = null);

/// <summary>
/// Static transformation callbacks invoked by the FileTransformation plugin via reflection.
/// Each method receives a <see cref="PatchRequestPayload"/> and returns the transformed content.
/// </summary>
public static class TransformationPatches
{
    private static readonly Regex LastVarRegex =
        new Regex(@"var\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\s*=", RegexOptions.Compiled);

    // A webpack module definition inside a chunk: "56213:function(e,t,r){" or "56213:(e,t,r)=>{".
    private static readonly Regex ModuleStartRegex =
        new Regex(@"(?<![\w$])\d+:(?:function\(\w+,\w+,\w+\)\{|\(\w+,\w+,\w+\)=>\{)", RegexOptions.Compiled);

    // The access the loadSections module reads getCardsHtml through: "u.default" on Jellyfin 10.11,
    // "p.Ay" on 12.3 (the cardBuilder module gained named exports, so webpack mangles the default one).
    private static readonly Regex CardBuilderAccessRegex =
        new Regex(@"(?<![\w$.])([A-Za-z_$][\w$]*)\.([A-Za-z_$][\w$]*)\.getCardsHtml\b", RegexOptions.Compiled);

    private const string InjectResourceSuffix = "Web.jux-loadsections-inject.js";

    // The exact literal anchor confirmed by extracting the real Jellyfin Web bundle (home-html
    // chunk) deployed on jellyux-test, and independently confirmed against the published
    // jellyfin-plugin-custom-tabs source (Helpers/TransformationPatches.cs), which targets this
    // exact same string. Marks the end of the native tab content container (index 1, "Favorites").
    private const string HomeHtmlAnchor = "id=\"favoritesTab\" data-index=\"1\"> <div class=\"sections\"></div> </div>";

    // The 4 JellyUX tab panes, in DOM order. Order matters: the native tab-switch mechanism toggles
    // visibility by DOM position within the full .tabContent list (not by data-index attribute
    // matching alone -- confirmed by reading the compiled home.js tab-switch handler), so
    // jux-tab-injector.js numbers each button after the real position of its pane in this list.
    private static readonly string[] HomeTabIds = ["jux-tab-watchlist", "jux-tab-history", "jux-tab-statistics"];

    // Every client asset index.html can reference, in document order: deferred scripts execute in
    // this order, so jux-i18n.js and jux-core.js come first (every script reads window.JuxCore, whose
    // labels come from window.JuxI18n) and jux-ui.js must
    // stay before the scripts that consume window.JuxUI. Module null marks the core assets, loaded
    // whenever the plugin is enabled; jux-ui is loaded only when an active asset declares UsesJuxUi.
    private static readonly ClientAsset[] ClientAssets =
    [
        new("jux-homepage.css", IsStylesheet: true, Module: null),
        new("jux-ui.css", IsStylesheet: true, Module: null, IsJuxUi: true),
        new(ClientI18nAsset, IsStylesheet: false, Module: null),
        new("jux-core.js", IsStylesheet: false, Module: null),
        new("jux-homepage.js", IsStylesheet: false, Module: null),
        // Only with an enabled featured section (PERF-10.a); jux-homepage.js loads it on demand when an
        // index.html cached before a section was featured lacks it.
        new("jux-featured.js", IsStylesheet: false, Module: null, Needed: HasFeaturedSection),
        new("jux-user-settings.js", IsStylesheet: false, Module: null),
        new("jux-ui.js", IsStylesheet: false, Module: null, IsJuxUi: true),
        new("jux-tab-injector.js", IsStylesheet: false, Module: m => m.Tabs),
        new("jux-tab-modern.js", IsStylesheet: false, Module: m => m.Tabs),
        new("jux-watchlist.js", IsStylesheet: false, Module: m => m.Tabs, UsesJuxUi: true),
        new("jux-card-hooks.js", IsStylesheet: false, Module: m => m.CardEnhancements),
        new("jux-progress.js", IsStylesheet: false, Module: m => m.Tabs, UsesJuxUi: true),
        new("jux-history.js", IsStylesheet: false, Module: m => m.Tabs, UsesJuxUi: true),
        new("jux-statistics.js", IsStylesheet: false, Module: m => m.Tabs, UsesJuxUi: true),
        new("jux-series-flatten.js", IsStylesheet: false, Module: m => m.SeriesFlatten),
        new("jux-collections.js", IsStylesheet: false, Module: m => m.Collections, UsesJuxUi: true),
        new("jux-breadcrumb.js", IsStylesheet: false, Module: m => m.Breadcrumb),
    ];

    private static readonly string[] ServedAssets = [.. ClientAssets.Select(a => a.FileName)];

    /// <summary>
    /// The client labels script (TODO_V4.md 9.4, F-64): not an embedded file but generated from the
    /// embedded language files by the asset endpoint.
    /// </summary>
    internal const string ClientI18nAsset = "jux-i18n.js";

    /// <summary>
    /// Resolves a requested file name against the served client assets, the only embedded files the
    /// asset endpoint exposes.
    /// </summary>
    /// <param name="fileName">The requested file name, matched case-insensitively.</param>
    /// <returns>The asset's file name as embedded, or null when it is not served.</returns>
    internal static string? ServedAssetName(string fileName) =>
        ServedAssets.FirstOrDefault(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Injects the CSS link and JS script tags of the enabled JellyUX modules into Jellyfin's
    /// index.html, or nothing when the plugin is disabled.
    /// Called by FileTransformation via reflection - must remain public and static.
    /// </summary>
    /// <param name="content">Payload containing the raw index.html contents.</param>
    /// <returns>Transformed HTML with JUX resources injected.</returns>
    public static string IndexHtml(PatchRequestPayload content)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return InjectClientAssets(content.Contents ?? string.Empty, config, ClientAssetVersion.Current);
    }

    /// <summary>
    /// Pure injection logic behind <see cref="IndexHtml"/>.
    /// </summary>
    /// <param name="html">The raw index.html contents.</param>
    /// <param name="config">The plugin configuration that decides which modules are injected.</param>
    /// <param name="cacheKey">The value of the <c>?v=</c> cache-busting parameter.</param>
    /// <returns>The HTML with the active assets injected, unchanged when the plugin is disabled.</returns>
    internal static string InjectClientAssets(string html, PluginConfiguration config, string cacheKey)
    {
        var assets = ActiveClientAssets(config);
        if (assets.Count == 0)
        {
            return html;
        }

        // Relative to index.html (served under <BaseUrl>/web/), so the assets follow a configured
        // BaseUrl; an absolute "/JuxHomepage/" would bypass it and receive the web client's HTML.
        var cacheParam = $"?v={cacheKey}";
        var links = assets
            .Where(a => a.IsStylesheet)
            .Select(a => $"<link rel=\"stylesheet\" href=\"../JuxHomepage/{a.FileName}{cacheParam}\" />\n");
        var scripts = assets
            .Where(a => !a.IsStylesheet)
            .Select(a => $"<script src=\"../JuxHomepage/{a.FileName}{cacheParam}\" defer></script>\n");

        return html
            .Replace("</head>", $"{string.Concat(links)}</head>", StringComparison.OrdinalIgnoreCase)
            .Replace("</body>", $"{string.Concat(scripts)}</body>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFeaturedSection(PluginConfiguration config) =>
        config.Widgets?.Any(w => w is { Enabled: true, Featured: true }) == true;

    /// <summary>
    /// Resolves which client assets the configuration activates, in document order.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The active assets; empty when the plugin is disabled.</returns>
    internal static IReadOnlyList<ClientAsset> ActiveClientAssets(PluginConfiguration config)
    {
        if (!config.Enabled)
        {
            return [];
        }

        var modules = config.Modules ?? new ModulesConfig();
        var active = ClientAssets
            .Where(a => !a.IsJuxUi && (a.Module is null || a.Module(modules)) && (a.Needed is null || a.Needed(config)))
            .ToList();
        var needsJuxUi = active.Any(a => a.UsesJuxUi);
        return ClientAssets.Where(a => active.Contains(a) || (a.IsJuxUi && needsJuxUi)).ToList();
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
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return TabsActive(config) ? TryPatchHomeHtmlChunk(raw).Content : raw;
    }

    /// <summary>
    /// Whether the home tab panes are spliced in: only when the plugin and its tabs module are on,
    /// so the home page holds no empty <c>#jux-tab-*</c> pane without the script that fills it.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>True when the home-html chunk must be patched.</returns>
    internal static bool TabsActive(PluginConfiguration config) =>
        config.Enabled && (config.Modules ?? new ModulesConfig()).Tabs;

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

        var cardBuilder = CardBuilderAccess(LoadSectionsModule(raw));
        if (cardBuilder is null)
        {
            return (raw, LoadSectionsOutcome.HookNotFound);
        }

        var fragment = fragmentTemplate
            .Replace("{{cardbuilder}}", cardBuilder, StringComparison.Ordinal)
            .Replace("{{this_hook}}", thisHook, StringComparison.Ordinal);

        var patched = raw.Replace(",loadSections:", $",loadSections:{fragment},originalLoadSections:", StringComparison.Ordinal);
        return (patched, LoadSectionsOutcome.Patched);
    }

    /// <summary>
    /// Returns how the loadSections module reads the cardBuilder default export (DR-024): the single
    /// X.Y access it reads getCardsHtml through, X being bound in that module to a webpack require
    /// (",X=r(24468)"). Anything else, no access, two different ones or an X that is a parameter or a
    /// comparison, proves nothing: the patch then reports HookNotFound and the home page stays native.
    /// </summary>
    /// <param name="module">The source of the module that defines ",loadSections:".</param>
    /// <returns>The access, such as "u.default" or "p.Ay", or null when none can be trusted.</returns>
    private static string? CardBuilderAccess(string module)
    {
        var accesses = CardBuilderAccessRegex.Matches(module)
            .Select(m => (Name: m.Groups[1].Value, Access: m.Groups[1].Value + "." + m.Groups[2].Value))
            .Distinct()
            .ToList();
        if (accesses.Count != 1)
        {
            return null;
        }

        var (name, access) = accesses[0];
        return Regex.IsMatch(module, $@"(?:\bvar\s+|,){Regex.Escape(name)}\s*=\s*[\w$]+\(\d+\)") ? access : null;
    }

    /// <summary>
    /// Returns the webpack module that contains the first ",loadSections:" marker, or the whole chunk
    /// when module boundaries cannot be found.
    /// </summary>
    /// <param name="raw">The chunk.js contents, known to contain the marker.</param>
    /// <returns>The module source text.</returns>
    private static string LoadSectionsModule(string raw)
    {
        var marker = raw.IndexOf(",loadSections:", StringComparison.Ordinal);
        var starts = ModuleStartRegex.Matches(raw);
        var start = starts.LastOrDefault(m => m.Index < marker)?.Index ?? 0;
        var end = starts.FirstOrDefault(m => m.Index > marker)?.Index ?? raw.Length;
        return raw[start..end];
    }

    /// <summary>
    /// Checks the home-html chunk(s) for the anchor the tab panes are spliced after, and logs the
    /// outcome: without it the tabs module falls back to native rendering (no JellyUX tab panes).
    /// </summary>
    /// <param name="webPath">Path to the Jellyfin web directory.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="logger">Logger for reporting results.</param>
    /// <returns>True when at least one home-html chunk holds the anchor.</returns>
    public static bool CheckHomeHtmlChunks(string webPath, IFileSystem fileSystem, ILogger logger)
    {
        if (!fileSystem.DirectoryExists(webPath))
        {
            return false;
        }

        var chunks = fileSystem.GetFiles(webPath, "home-html.*.chunk.js", SearchOption.AllDirectories);
        if (chunks.Count == 0)
        {
            logger.LogWarning(
                "No home-html chunk found in {WebPath}: the JellyUX tab panes cannot be spliced in. "
                + "See CONTRIBUTING.md 'Jellyfin Update Procedure'.",
                webPath);
            return false;
        }

        var found = false;
        foreach (var chunk in chunks)
        {
            string content;
            try
            {
                content = fileSystem.ReadAllText(chunk);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not read chunk file: {File}", chunk);
                continue;
            }

            if (TryPatchHomeHtmlChunk(content).Outcome == HomeHtmlOutcome.Patched)
            {
                found = true;
                logger.LogInformation("Found home-html tab anchor in {FileName}.", Path.GetFileName(chunk));
            }
            else
            {
                logger.LogWarning(
                    "Chunk {FileName} does not contain the home-html tab anchor: the JellyUX tab panes "
                    + "fall back to native rendering. See CONTRIBUTING.md 'Jellyfin Update Procedure'.",
                    Path.GetFileName(chunk));
            }
        }

        return found;
    }

    /// <summary>
    /// Returns whether <paramref name="stream"/> contains <paramref name="marker"/>, reading it through
    /// <paramref name="buffer"/> and keeping the last <c>marker.Length - 1</c> bytes between two reads so a
    /// marker split across them is still found. A minified chunk is one line: no line reader applies.
    /// </summary>
    /// <param name="stream">The stream to scan, read to its end at most.</param>
    /// <param name="marker">The bytes to look for.</param>
    /// <param name="buffer">The read buffer, longer than <paramref name="marker"/>; reused across calls.</param>
    /// <returns>True when the marker occurs in the stream.</returns>
    internal static bool StreamContains(Stream stream, ReadOnlySpan<byte> marker, byte[] buffer)
    {
        var kept = 0;
        int read;
        while ((read = stream.Read(buffer, kept, buffer.Length - kept)) > 0)
        {
            var filled = kept + read;
            if (buffer.AsSpan(0, filled).IndexOf(marker) >= 0)
            {
                return true;
            }

            kept = Math.Min(marker.Length - 1, filled);
            buffer.AsSpan(filled - kept, kept).CopyTo(buffer);
        }

        return false;
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

        // Each chunk is streamed through one buffer: reading ~17 MB of chunks as UTF-16 strings allocated
        // 68.5 MiB at every server start (PERF-06, MB-08).
        var marker = Encoding.UTF8.GetBytes(",loadSections:");
        var buffer = new byte[64 * 1024];
        var matches = fileSystem
            .GetFiles(webPath, "*.chunk.js", SearchOption.AllDirectories)
            .Where(f =>
            {
                try
                {
                    using var stream = fileSystem.OpenRead(f);
                    return StreamContains(stream, marker, buffer);
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
