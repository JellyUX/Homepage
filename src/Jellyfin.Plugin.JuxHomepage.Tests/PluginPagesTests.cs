using System.Runtime.CompilerServices;
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
}
