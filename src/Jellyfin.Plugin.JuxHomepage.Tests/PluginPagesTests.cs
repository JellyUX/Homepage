using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class PluginPagesTests
{
    [Fact]
    public void GetPages_ConfigurationPage_IsListedInTheDashboardMenuUnderThePluginName()
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));

        var page = Assert.Single(plugin.GetPages(), p => p.EmbeddedResourcePath.EndsWith(".Web.config.html", System.StringComparison.Ordinal));

        Assert.Equal("JellyUX Homepage", page.Name);
        Assert.True(page.EnableInMainMenu);
        Assert.Equal("JellyUX Homepage", page.DisplayName);
    }

    [Fact]
    public void GetPages_ServesTheControllerModuleNamedByTheConfigurationPage()
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        var html = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web", "config.html"));

        var page = Assert.Single(plugin.GetPages(), p => p.EmbeddedResourcePath.EndsWith(".Web.config.js", System.StringComparison.Ordinal));

        Assert.Contains($"data-controller=\"__plugin/{page.Name}\"", html, System.StringComparison.Ordinal);
        Assert.False(page.EnableInMainMenu);
        Assert.NotNull(typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath));
    }

    [Fact]
    public void GetPages_ServesTheStylesheetLinkedByTheConfigurationPage()
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        var html = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web", "config.html"));

        var page = Assert.Single(plugin.GetPages(), p => p.EmbeddedResourcePath.EndsWith(".Web.config.css", System.StringComparison.Ordinal));

        Assert.Contains($"href=\"configurationpage?name={page.Name}\"", html, System.StringComparison.Ordinal);
        Assert.False(page.EnableInMainMenu);
    }

    [Fact]
    public void GetPages_ServesEveryModuleTheConfigurationPageImports()
    {
        // config.js and its modules import each other as ./configurationpage?name=<page> (DR-019): every such
        // import names a page, and every config-*.js module is one.
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        var pages = plugin.GetPages().ToDictionary(p => p.Name, p => p.EmbeddedResourcePath, StringComparer.Ordinal);
        var web = Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web");
        var scripts = Directory.GetFiles(web, "config*.js").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        var imported = scripts
            .SelectMany(f => Regex.Matches(File.ReadAllText(Path.Combine(web, f!)), @"from '\./configurationpage\?name=([a-z]+)'").Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(imported);
        Assert.All(imported, name => Assert.True(pages.ContainsKey(name), $"{name} is imported but not a plugin page"));
        Assert.All(scripts, file => Assert.Contains(pages.Values, path => path.EndsWith($".Web.{file}", StringComparison.Ordinal)));
        Assert.All(pages.Values, path => Assert.NotNull(typeof(Plugin).Assembly.GetManifestResourceStream(path)));
    }
}
