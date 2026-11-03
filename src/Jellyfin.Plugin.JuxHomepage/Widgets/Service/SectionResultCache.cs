namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Short cache of the section pages a user's home screen reads (PERF-03.b, PERF-06.a). The layout probe
/// stores the first page of each section it keeps, so the section request that follows reads no
/// widget; a page read by a section request is kept the same way. One page per section and user: a
/// request for another page replaces it, so a user holds at most <see cref="WidgetConfig.MaxItems"/>
/// items per section.
/// <para>
/// A page is served until the layout TTL it was stored with ends, and never after the user's layout
/// was invalidated or the configuration changed (<see cref="SessionCache.VersionOf"/>), nor after
/// <see cref="Invalidate"/> (the user's data changed: a title watched, resumed or liked) or
/// <see cref="Clear"/> (an item was added to or removed from the library). A page computed before one
/// of these is never stored (<c>Section_InvalidatedWhileRead_IsNotStored</c>). The versions and the cleanup are
/// those of <see cref="VersionedUserCache{TKey, TEntry}"/>.
/// </para>
/// </summary>
public sealed class SectionResultCache : IDisposable
{
    private const int CleanupIntervalMinutes = 5;

    private readonly VersionedUserCache<SectionId, Entry> _entries;
    private readonly SessionCache _sessionCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionResultCache"/> class.
    /// </summary>
    /// <param name="sessionCache">The layout cache, whose version a stored page must still match.</param>
    /// <param name="timeProvider">Time source for page expiry. Defaults to <see cref="TimeProvider.System"/>.</param>
    public SectionResultCache(SessionCache sessionCache, TimeProvider? timeProvider = null)
    {
        _sessionCache = sessionCache;
        _entries = new(
            id => id.UserId,
            (id, entry, now) => now >= entry.ExpiresAt || entry.Stamp != StampOf(id.UserId),
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            timeProvider);
    }

    /// <summary>
    /// The user's current stamp. Read it before computing a page and pass it to <see cref="Set"/>.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The stamp.</returns>
    public Stamp StampOf(Guid userId) =>
        new(_sessionCache.VersionOf(userId), _entries.VersionOf(userId));

    /// <summary>
    /// Returns the stored page of a section, when it is the requested one and still valid.
    /// </summary>
    /// <param name="id">The section.</param>
    /// <param name="startIndex">The requested start index.</param>
    /// <param name="limit">The requested page size.</param>
    /// <param name="result">The page, or null on a miss.</param>
    /// <returns>True on a hit.</returns>
    public bool TryGet(SectionId id, int startIndex, int limit, out WidgetResult? result)
    {
        if (_entries.TryGet(id, out var entry)
            && entry.StartIndex == startIndex
            && entry.Limit == limit
            && entry.Stamp == StampOf(id.UserId)
            && _entries.UtcNow < entry.ExpiresAt)
        {
            result = entry.Result;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Stores a page of a section in place of the one it held, unless the user's stamp changed since
    /// <paramref name="stamp"/> was read: the page reflects a state that no longer holds.
    /// </summary>
    /// <param name="id">The section.</param>
    /// <param name="startIndex">The page's start index.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="result">The page.</param>
    /// <param name="stamp">The <see cref="StampOf"/> read before the page was computed.</param>
    /// <param name="ttl">How long the page is served.</param>
    /// <returns>True when the page was stored.</returns>
    public bool Set(SectionId id, int startIndex, int limit, WidgetResult result, Stamp stamp, TimeSpan ttl)
    {
        var entry = new Entry(startIndex, limit, result, stamp, _entries.UtcNow + ttl);
        return _entries.Set(id, entry, () => stamp == StampOf(id.UserId));
    }

    /// <summary>
    /// Drops the stored page of a section, after its projection failed.
    /// </summary>
    /// <param name="id">The section.</param>
    /// <param name="result">The page that failed; a page stored since is kept.</param>
    public void Remove(SectionId id, WidgetResult result)
    {
        if (_entries.TryGet(id, out var entry) && ReferenceEquals(entry.Result, result))
        {
            _entries.Remove(id, entry);
        }
    }

    /// <summary>Drops every stored page of the user.</summary>
    /// <param name="userId">The user.</param>
    public void Invalidate(Guid userId) => _entries.Invalidate(userId);

    /// <summary>Drops every stored page.</summary>
    public void Clear() => _entries.Clear();

    /// <inheritdoc/>
    public void Dispose() => _entries.Dispose();

    /// <summary>
    /// Test-only seam: the number of stored pages.
    /// </summary>
    /// <returns>The number of entries.</returns>
    internal int CountForTesting() => _entries.Count;

    /// <summary>
    /// Test-only seam: runs the cleanup the background timer runs every
    /// <see cref="CleanupIntervalMinutes"/> minutes.
    /// </summary>
    internal void RunCleanupForTesting() => _entries.Cleanup();

    /// <summary>A user's versions: the layout cache's and this cache's own.</summary>
    /// <param name="Layout">The layout cache version (<see cref="SessionCache.VersionOf"/>).</param>
    /// <param name="Sections">This cache's version for the user.</param>
    public readonly record struct Stamp(long Layout, long Sections);

    private sealed record Entry(int StartIndex, int Limit, WidgetResult Result, Stamp Stamp, DateTime ExpiresAt);
}

/// <summary>
/// Identifies a section of a user's home screen as the client requests it.
/// </summary>
/// <param name="UserId">The user.</param>
/// <param name="WidgetType">The section's widget type.</param>
/// <param name="AdditionalData">The section's instance data; empty is the same as none.</param>
/// <param name="SectionKey">The section's configuration row key; empty is the same as none.</param>
public readonly record struct SectionId(Guid UserId, string WidgetType, string? AdditionalData, string? SectionKey)
{
    /// <summary>Gets the instance data, null when empty.</summary>
    public string? AdditionalData { get; } = string.IsNullOrEmpty(AdditionalData) ? null : AdditionalData;

    /// <summary>Gets the configuration row key, null when empty.</summary>
    public string? SectionKey { get; } = string.IsNullOrEmpty(SectionKey) ? null : SectionKey;
}
