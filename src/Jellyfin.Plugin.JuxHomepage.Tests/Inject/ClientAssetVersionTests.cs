using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.Models;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

public sealed class ClientAssetVersionTests
{
    private const string Script = "Jellyfin.Plugin.JuxHomepage.Web.jux-homepage.js";
    private const string Stylesheet = "Jellyfin.Plugin.JuxHomepage.Web.jux-homepage.css";
    private const string Language = "Jellyfin.Plugin.JuxHomepage.Localization.en.json";

    private static Dictionary<string, byte[]> Resources() => new(StringComparer.Ordinal)
    {
        [Script] = "window.a = 1;"u8.ToArray(),
        [Stylesheet] = ".a { margin: 0; }"u8.ToArray(),
        [Language] = "{}"u8.ToArray(),
        ["Jellyfin.Plugin.JuxHomepage.Web.configPage.html"] = "<div></div>"u8.ToArray()
    };

    [Fact]
    public void Current_IsAShortHexKey()
    {
        Assert.Matches("^[0-9a-f]{12}$", ClientAssetVersion.Current);
    }

    [Theory]
    [InlineData(Script)]
    [InlineData(Stylesheet)]
    [InlineData(Language)]
    public void Compute_OneByteOfAClientAssetChanged_ChangesTheKey(string name)
    {
        var resources = Resources();
        var before = ClientAssetVersion.Compute(resources);

        resources[name] = [.. resources[name], (byte)' '];

        Assert.NotEqual(before, ClientAssetVersion.Compute(resources));
    }

    [Fact]
    public void Compute_ClientAssetRenamed_ChangesTheKey()
    {
        var resources = Resources();
        var before = ClientAssetVersion.Compute(resources);

        resources["Jellyfin.Plugin.JuxHomepage.Web.jux-renamed.js"] = resources[Script];
        resources.Remove(Script);

        Assert.NotEqual(before, ClientAssetVersion.Compute(resources));
    }

    [Fact]
    public void Compute_OtherResourcesChangedOrAdded_KeepsTheKey()
    {
        var resources = Resources();
        var before = ClientAssetVersion.Compute(resources);

        resources["Jellyfin.Plugin.JuxHomepage.Web.configPage.html"] = "<p></p>"u8.ToArray();
        resources["Jellyfin.Plugin.JuxHomepage.Web.config.js"] = "x"u8.ToArray();
        resources["Jellyfin.Plugin.JuxHomepage.Web.jux-notes.md"] = "x"u8.ToArray();

        Assert.Equal(before, ClientAssetVersion.Compute(resources));
    }

    [Fact]
    public void Compute_DoesNotDependOnTheResourceOrder()
    {
        var resources = Resources();
        var reversed = new Dictionary<string, byte[]>(resources.Reverse(), StringComparer.Ordinal);

        Assert.Equal(ClientAssetVersion.Compute(resources), ClientAssetVersion.Compute(reversed));
    }

    [Fact]
    public void Compute_AssemblyWithoutClientAssets_DiffersFromThePluginKey()
    {
        Assert.NotEqual(ClientAssetVersion.Current, ClientAssetVersion.Compute(typeof(ClientAssetVersionTests).Assembly));
    }

    [Fact]
    public void CacheControlFor_CurrentKey_IsImmutable()
    {
        Assert.Equal("public, max-age=31536000, immutable", ClientAssetVersion.CacheControlFor("abc", "abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2.9.1.0")]
    public void CacheControlFor_MissingOrOutdatedKey_IsRevalidatedHourly(string? requestedKey)
    {
        Assert.Equal("public, max-age=3600", ClientAssetVersion.CacheControlFor(requestedKey, "abc"));
    }

    [Fact]
    public void IndexHtml_ReferencesAssetsWithTheContentKey()
    {
        var html = TransformationPatches.IndexHtml(new PatchRequestPayload { Contents = "<html><head></head><body></body></html>" });

        Assert.Contains($"jux-homepage.js?v={ClientAssetVersion.Current}\"", html, StringComparison.Ordinal);
    }
}
