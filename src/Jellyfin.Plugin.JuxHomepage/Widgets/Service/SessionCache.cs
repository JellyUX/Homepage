using Jellyfin.Plugin.JuxHomepage.Configuration;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Thread-safe per-user session cache for the home screen widget layout.
/// Caches the list of <see cref="WidgetDescriptor"/> that passed MinItems filtering so that
/// <see cref="WidgetService.GetWidgetsForUser"/> avoids re-querying every widget on each page load.
/// Entries expire a fixed TTL after they were built (see <see cref="CacheConfig.SessionTtlMinutes"/>):
/// reading an entry does not extend it (F-02). An expired entry is still returned, marked expired, so
/// the caller can serve it while it rebuilds the layout in the background (PERF-01). Clearing or
/// invalidating removes entries outright and bumps a version, so a layout built before that is never
/// stored (<see cref="Set"/>). A background timer garbage-collects entries that have not been accessed
/// in over an hour. The versions and the cleanup are those of <see cref="VersionedUserCache{TKey, TEntry}"/>.
/// </summary>
public sealed class SessionCache : IDisposable
{
    private const int CleanupIntervalMinutes = 5;
    private const int GarbageCollectAfterMinutes = 60;

    private readonly VersionedUserCache<(Guid UserId, string Lang), SessionData> _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionCache"/> class.
    /// </summary>
    /// <param name="timeProvider">Time source for entry expiry. Defaults to <see cref="TimeProvider.System"/>.</param>
    public SessionCache(TimeProvider? timeProvider = null)
    {
        _cache = new(
            key => key.UserId,
            (_, data, now) => data.LastAccessed < now - TimeSpan.FromMinutes(GarbageCollectAfterMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            timeProvider);
    }

    /// <summary>
    /// Attempts to retrieve a cached descriptor list for the given user and language, expired or not.
    /// A hit updates <see cref="SessionData.LastAccessed"/> for garbage collection, never the expiry.
    /// </summary>
    /// <param name="userId">The user whose layout to retrieve.</param>
    /// <param name="lang">The language the cached layout's display names were translated for.</param>
    /// <param name="ttl">The maximum age of a fresh entry, counted from when it was built.</param>
    /// <param name="descriptors">The cached descriptors, or null on a miss.</param>
    /// <param name="expired">True when the entry found is older than <paramref name="ttl"/>.</param>
    /// <returns>True if an entry was found, fresh or expired; otherwise false.</returns>
    public bool TryGet(Guid userId, string? lang, TimeSpan ttl, out IReadOnlyList<WidgetDescriptor>? descriptors, out bool expired)
    {
        var now = _cache.UtcNow;
        if (_cache.TryGet((userId, NormalizeKey(lang)), out var data))
        {
            data.LastAccessed = now;
            descriptors = data.Descriptors;
            expired = now - data.CreatedAt >= ttl;
            return true;
        }

        descriptors = null;
        expired = false;
        return false;
    }

    /// <summary>
    /// The user's current cache version: it changes whenever <see cref="Clear"/> or
    /// <see cref="Invalidate"/> drops the user's entries. Read it before building a layout and pass
    /// it to <see cref="Set"/>.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>A value that only grows.</returns>
    public long VersionOf(Guid userId) => _cache.VersionOf(userId);

    /// <summary>
    /// Stores or replaces the descriptor list for the given user and language, unless the user's
    /// entries were cleared or invalidated since <paramref name="builtAtVersion"/> was read: that
    /// layout reflects a configuration or a state that no longer holds
    /// (<c>Set_AfterClear_WithVersionReadBefore_IsDropped</c>).
    /// </summary>
    /// <param name="userId">The user whose layout to cache.</param>
    /// <param name="lang">The language the layout's display names were translated for.</param>
    /// <param name="descriptors">The ordered list of descriptors that passed MinItems filtering.</param>
    /// <param name="builtAtVersion">The <see cref="VersionOf"/> read before the build; null stores unconditionally.</param>
    /// <returns>True when the layout was stored.</returns>
    public bool Set(Guid userId, string? lang, IReadOnlyList<WidgetDescriptor> descriptors, long? builtAtVersion = null)
    {
        var now = _cache.UtcNow;
        var data = new SessionData { CreatedAt = now, LastAccessed = now, Descriptors = descriptors };
        return _cache.Set(
            (userId, NormalizeKey(lang)),
            data,
            () => builtAtVersion is not { } version || version == VersionOf(userId));
    }

    /// <summary>
    /// Removes every cached layout for the given user, across all languages, forcing a fresh
    /// rebuild on the user's next request regardless of which language they're viewing in.
    /// </summary>
    /// <param name="userId">The user whose cache entries to remove.</param>
    public void Invalidate(Guid userId) => _cache.Invalidate(userId);

    /// <summary>
    /// Normalizes a raw language tag to a stable cache-key component, mirroring
    /// <see cref="Localization.LocalizationService"/>'s own normalization ("fr-FR" -&gt; "fr") so
    /// equivalent language tags share one cache entry instead of quietly duplicating it.
    /// </summary>
    /// <param name="lang">The raw language tag.</param>
    /// <returns>The language part of the cache key.</returns>
    internal static string NormalizeKey(string? lang) =>
        string.IsNullOrWhiteSpace(lang) ? "en" : lang.Split('-', 2)[0].ToLowerInvariant();

    /// <summary>
    /// Removes all cached layouts, forcing a fresh rebuild for every user on their next request.
    /// Called when the plugin configuration changes so that the new widget settings take effect
    /// immediately rather than waiting for the per-user TTL to expire; an expired layout is not
    /// served after this, it is gone.
    /// </summary>
    public void Clear() => _cache.Clear();

    /// <inheritdoc/>
    public void Dispose() => _cache.Dispose();

    /// <summary>
    /// Test-only seam: backdates an existing entry's <see cref="SessionData.LastAccessed"/> so a
    /// unit test can simulate an entry old enough for <see cref="RunCleanupForTesting"/> to evict,
    /// without waiting for <see cref="GarbageCollectAfterMinutes"/> minutes to actually elapse.
    /// </summary>
    /// <param name="userId">The user whose entry to backdate.</param>
    /// <param name="lang">The language of the entry to backdate.</param>
    /// <param name="lastAccessed">The simulated last-accessed timestamp.</param>
    internal void SetLastAccessedForTesting(Guid userId, string? lang, DateTime lastAccessed)
    {
        if (_cache.TryGet((userId, NormalizeKey(lang)), out var data))
        {
            data.LastAccessed = lastAccessed;
        }
    }

    /// <summary>
    /// Test-only seam: runs the same garbage-collection pass the background <see cref="Timer"/>
    /// triggers every <see cref="CleanupIntervalMinutes"/> minutes, synchronously and on demand.
    /// </summary>
    internal void RunCleanupForTesting() => _cache.Cleanup();
}
