using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// The in-memory cache mechanics the layout cache (<see cref="SessionCache"/>) and the section page cache
/// (<see cref="SectionResultCache"/>) share (QUAL-02, DR-032): entries keyed per user, a global version and one
/// version per user that <see cref="Clear"/> and <see cref="Invalidate"/> bump, a write refused when the state it
/// was computed from no longer holds (<see cref="Set"/>), and a periodic cleanup.
/// </summary>
/// <typeparam name="TKey">The entry key; <c>userOf</c> tells its user.</typeparam>
/// <typeparam name="TEntry">The stored entry.</typeparam>
public sealed class VersionedUserCache<TKey, TEntry> : IDisposable
    where TKey : notnull
    where TEntry : class
{
    private readonly ConcurrentDictionary<TKey, TEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, long> _userVersions = new();
    private readonly Lock _writeLock = new();
    private readonly Func<TKey, Guid> _userOf;
    private readonly Func<TKey, TEntry, DateTime, bool> _isGarbage;
    private readonly TimeProvider _timeProvider;
    private readonly Timer _cleanupTimer;
    private long _version;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionedUserCache{TKey, TEntry}"/> class.
    /// </summary>
    /// <param name="userOf">The user an entry key belongs to.</param>
    /// <param name="isGarbage">Whether the cleanup drops an entry, given the current UTC time.</param>
    /// <param name="cleanupInterval">How often the cleanup runs.</param>
    /// <param name="timeProvider">Time source. Defaults to <see cref="TimeProvider.System"/>.</param>
    public VersionedUserCache(
        Func<TKey, Guid> userOf,
        Func<TKey, TEntry, DateTime, bool> isGarbage,
        TimeSpan cleanupInterval,
        TimeProvider? timeProvider = null)
    {
        _userOf = userOf;
        _isGarbage = isGarbage;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cleanupTimer = new Timer(_ => Cleanup(), null, cleanupInterval, cleanupInterval);
    }

    /// <summary>Gets the current UTC time of the cache's time source.</summary>
    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Gets the number of stored entries.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// The user's current version: it changes whenever <see cref="Clear"/> or <see cref="Invalidate"/> drops the
    /// user's entries. Read it before computing an entry, and check it again in <see cref="Set"/>.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>A value that only grows.</returns>
    public long VersionOf(Guid userId) => Interlocked.Read(ref _version) + _userVersions.GetValueOrDefault(userId);

    /// <summary>Returns the stored entry of a key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry, or null on a miss.</param>
    /// <returns>True on a hit.</returns>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TEntry entry) => _entries.TryGetValue(key, out entry);

    /// <summary>
    /// Stores or replaces the entry of a key, unless <paramref name="isCurrent"/>, checked under the write lock
    /// that <see cref="Clear"/> and <see cref="Invalidate"/> take, says the state it was computed from changed.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="isCurrent">Whether the entry still reflects the current versions.</param>
    /// <returns>True when the entry was stored.</returns>
    public bool Set(TKey key, TEntry entry, Func<bool> isCurrent)
    {
        lock (_writeLock)
        {
            if (!isCurrent())
            {
                return false;
            }

            _entries[key] = entry;
            return true;
        }
    }

    /// <summary>Drops the entry of a key when it is still <paramref name="entry"/>; one stored since is kept.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry to drop.</param>
    public void Remove(TKey key, TEntry entry) => _entries.TryRemove(KeyValuePair.Create(key, entry));

    /// <summary>Drops the entries <paramref name="match"/> names, without bumping any version.</summary>
    /// <param name="match">Whether an entry is dropped.</param>
    public void RemoveWhere(Func<TKey, TEntry, bool> match)
    {
        foreach (var entry in _entries)
        {
            if (match(entry.Key, entry.Value))
            {
                _entries.TryRemove(entry);
            }
        }
    }

    /// <summary>Drops every entry of the user and bumps the user's version.</summary>
    /// <param name="userId">The user.</param>
    public void Invalidate(Guid userId)
    {
        lock (_writeLock)
        {
            _userVersions.AddOrUpdate(userId, 1, (_, v) => v + 1);
            foreach (var key in _entries.Keys.Where(k => _userOf(k) == userId).ToList())
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Drops every entry and bumps the global version.</summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            Interlocked.Increment(ref _version);
            _entries.Clear();
        }
    }

    /// <summary>Runs the cleanup the timer runs, synchronously.</summary>
    public void Cleanup()
    {
        var now = UtcNow;
        foreach (var entry in _entries)
        {
            if (_isGarbage(entry.Key, entry.Value, now))
            {
                _entries.TryRemove(entry);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer.Dispose();
            _disposed = true;
        }
    }
}
