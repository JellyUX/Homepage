using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.Models;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

public sealed class ClientAssetVersionTests
{
    [Fact]
    public void Current_IsAShortStableHexKey()
    {
        var key = ClientAssetVersion.Current;

        Assert.Matches("^[0-9a-f]{12}$", key);
        Assert.Equal(key, ClientAssetVersion.Compute(typeof(Plugin).Assembly));
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
