namespace Jellyfin.Plugin.JuxHomepage.Caching;

/// <summary>
/// On-disk envelope for a single <see cref="DiskJsonCache{T}"/> file, wrapping the cached items with the timestamp of
/// the refresh that produced them.
/// </summary>
/// <typeparam name="T">The cached item type.</typeparam>
public sealed class CacheEntry<T>
{
    /// <summary>Gets or sets the UTC timestamp of the refresh that produced this cache file.</summary>
    public DateTime RefreshedAtUtc { get; set; }

    /// <summary>Gets or sets the cached items.</summary>
    public IReadOnlyList<T> Items { get; set; } = [];
}
