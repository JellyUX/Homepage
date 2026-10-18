using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.JuxHomepage.Inject;

/// <summary>
/// Cache key of the client assets: a short hash of their embedded contents. Used as the <c>?v=</c>
/// parameter of every asset URL in index.html, so the URL changes whenever any asset changes, which
/// is what makes serving them as <c>immutable</c> safe, including for local builds that keep the
/// same assembly version. The language files count too: <c>jux-i18n.js</c> is built from them.
/// </summary>
internal static class ClientAssetVersion
{
    private const string AssetMarker = ".Web.jux-";

    private const string LanguageMarker = ".Localization.";

    private const int KeyLength = 12;

    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    private const string DefaultCacheControl = "public, max-age=3600";

    private static readonly Lazy<string> CurrentKey = new(() => Compute(typeof(ClientAssetVersion).Assembly));

    /// <summary>
    /// Gets the cache key of the client assets embedded in the plugin assembly.
    /// </summary>
    public static string Current => CurrentKey.Value;

    /// <summary>
    /// Hashes the names and contents of the client assets embedded in an assembly.
    /// </summary>
    /// <param name="assembly">The assembly holding the embedded assets.</param>
    /// <returns>The first 12 hexadecimal characters of the SHA-256 hash.</returns>
    internal static string Compute(Assembly assembly) =>
        Compute(assembly.GetManifestResourceNames()
            .Where(IsClientAsset)
            .ToDictionary(n => n, n => ReadResource(assembly, n), StringComparer.Ordinal));

    /// <summary>
    /// Hashes the names and contents of the client assets among named resources, in ordinal name
    /// order; other resources are ignored.
    /// </summary>
    /// <param name="resources">Resource contents by manifest resource name.</param>
    /// <returns>The first 12 hexadecimal characters of the SHA-256 hash.</returns>
    internal static string Compute(IReadOnlyDictionary<string, byte[]> resources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in resources.Keys.Where(IsClientAsset).OrderBy(n => n, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            hash.AppendData(resources[name]);
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..KeyLength].ToLowerInvariant();
    }

    /// <summary>
    /// Chooses the Cache-Control header of an asset response: immutable only when the request
    /// carries the current cache key, so an outdated or missing key never pins a response.
    /// </summary>
    /// <param name="requestedKey">The <c>v</c> query parameter of the request, if any.</param>
    /// <param name="currentKey">The current cache key.</param>
    /// <returns>The Cache-Control header value.</returns>
    internal static string CacheControlFor(string? requestedKey, string currentKey) =>
        string.Equals(requestedKey, currentKey, StringComparison.Ordinal) ? ImmutableCacheControl : DefaultCacheControl;

    private static bool IsClientAsset(string name) =>
        (name.Contains(AssetMarker, StringComparison.Ordinal)
            && (name.EndsWith(".js", StringComparison.Ordinal) || name.EndsWith(".css", StringComparison.Ordinal)))
        || (name.Contains(LanguageMarker, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal));

    private static byte[] ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
