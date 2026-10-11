using System.Collections.Concurrent;
using Jellyfin.Plugin.JuxHomepage.Configuration;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Thread-safe per-user session cache for the home screen widget layout.
/// Caches the list of <see cref="WidgetDescriptor"/> that passed MinItems filtering so that
/// <see cref="WidgetService.GetWidgetsForUser"/> avoids re-querying every widget on each page load.
/// Entries expire a fixed TTL after they were built (see <see cref="CacheConfig.SessionTtlMinutes"/>):
/// reading an entry does not extend it (F-02), so a page reloaded every minute still gets a fresh
/// layout once the TTL has passed. A background timer garbage-collects entries that have not been
/// accessed in over an hour.
/// </summary>
public sealed class SessionCache : IDisposable
{
    private const int CleanupIntervalMinutes = 5;
    private const int GarbageCollectAfterMinutes = 60;

    private readonly ConcurrentDictionary<(Guid UserId, string Lang), SessionData> _cache = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionCache"/> class.
    /// </summary>
    /// <param name="timeProvider">Time source for entry expiry. Defaults to <see cref="TimeProvider.System"/>.</param>
    public SessionCache(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cleanupTimer = new Timer(
            _ => Cleanup(),
            null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    /// <summary>
    /// Attempts to retrieve a cached descriptor list for the given user and language.
    /// A hit updates <see cref="SessionData.LastAccessed"/> for garbage collection, never the expiry.
    /// </summary>
    /// <param name="userId">The user whose layout to retrieve.</param>
    /// <param name="lang">The language the cached layout's display names were translated for.</param>
    /// <param name="ttl">The maximum age of an entry, counted from when it was built.</param>
    /// <param name="descriptors">The cached descriptors, or null on a miss.</param>
    /// <returns>True if a valid cached entry was found; otherwise false.</returns>
    public bool TryGet(Guid userId, string? lang, TimeSpan ttl, out IReadOnlyList<WidgetDescriptor>? descriptors)
    {
        var now = UtcNow;
        if (_cache.TryGetValue((userId, NormalizeKey(lang)), out var data) &&
            now - data.CreatedAt < ttl)
        {
            data.LastAccessed = now;
            descriptors = data.Descriptors;
            return true;
        }

        descriptors = null;
        return false;
    }

    /// <summary>
    /// Stores or replaces the descriptor list for the given user and language.
    /// </summary>
    /// <param name="userId">The user whose layout to cache.</param>
    /// <param name="lang">The language the layout's display names were translated for.</param>
    /// <param name="descriptors">The ordered list of descriptors that passed MinItems filtering.</param>
    public void Set(Guid userId, string? lang, IReadOnlyList<WidgetDescriptor> descriptors)
    {
        var now = UtcNow;
        _cache[(userId, NormalizeKey(lang))] = new SessionData
        {
            CreatedAt = now,
            LastAccessed = now,
            Descriptors = descriptors
        };
    }

    /// <summary>
    /// Removes every cached layout for the given user, across all languages, forcing a fresh
    /// rebuild on the user's next request regardless of which language they're viewing in.
    /// </summary>
    /// <param name="userId">The user whose cache entries to remove.</param>
    public void Invalidate(Guid userId)
    {
        foreach (var key in _cache.Keys.Where(k => k.UserId == userId).ToList())
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Normalizes a raw language tag to a stable cache-key component, mirroring
    /// <see cref="Localization.LocalizationService"/>'s own normalization ("fr-FR" -&gt; "fr") so
    /// equivalent language tags share one cache entry instead of quietly duplicating it.
    /// </summary>
    private static string NormalizeKey(string? lang) =>
        string.IsNullOrWhiteSpace(lang) ? "en" : lang.Split('-', 2)[0].ToLowerInvariant();

    /// <summary>
    /// Removes all cached layouts, forcing a fresh rebuild for every user on their next request.
    /// Called when the plugin configuration changes so that the new widget settings take effect
    /// immediately rather than waiting for the per-user TTL to expire.
    /// </summary>
    public void Clear() => _cache.Clear();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer.Dispose();
            _disposed = true;
        }
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private void Cleanup()
    {
        var cutoff = UtcNow - TimeSpan.FromMinutes(GarbageCollectAfterMinutes);
        foreach (var key in _cache.Keys.ToList())
        {
            if (_cache.TryGetValue(key, out var data) && data.LastAccessed < cutoff)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

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
        if (_cache.TryGetValue((userId, NormalizeKey(lang)), out var data))
        {
            data.LastAccessed = lastAccessed;
        }
    }

    /// <summary>
    /// Test-only seam: runs the same garbage-collection pass the background <see cref="Timer"/>
    /// triggers every <see cref="CleanupIntervalMinutes"/> minutes, synchronously and on demand.
    /// </summary>
    internal void RunCleanupForTesting() => Cleanup();
}
