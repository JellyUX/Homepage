using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Caching;

/// <summary>
/// A per-user <see cref="DiskJsonCache{T}"/> file computed from the local library (TODO_V4.md 8.2,
/// F-54): computed on the user's first read instead of waiting for the daily task, refreshed in the
/// background once older than its time-to-live, and patched in place after a watch.
/// <para>
/// Writes of one user are serialized, so a patch never interleaves with a recompute of the same file.
/// An empty result is written like any other (F-69): a user without history is computed once, not on
/// every read.
/// </para>
/// <para>
/// A list the disk refuses (full, read-only) is kept in memory and served until it is one
/// time-to-live old, so a failing disk costs one computation per time-to-live, not one per read
/// (FIAB-03; tests: PerUserComputedCacheTests "Get_WhenTheDiskRefusesTheWrite_ServesTheComputedListAndComputesOncePerTtl",
/// "Update_WhenTheDiskRefusesTheWrite_KeepsThePatchedListInMemory").
/// </para>
/// </summary>
/// <typeparam name="T">The cached entry type.</typeparam>
internal sealed class PerUserComputedCache<T>
{
    private readonly DiskJsonCache<T> _cache;
    private readonly TimeSpan _ttl;
    private readonly Func<Guid, IReadOnlyList<T>> _compute;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, object> _userLocks = new();
    private readonly ConcurrentDictionary<Guid, byte> _backgroundRefreshes = new();
    private readonly ConcurrentDictionary<Guid, Unsaved> _unsaved = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PerUserComputedCache{T}"/> class.
    /// </summary>
    /// <param name="cache">The disk cache holding one file per user.</param>
    /// <param name="ttl">Age after which a read refreshes the file in the background.</param>
    /// <param name="compute">Computes the full list of a user.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source of the time-to-live. Defaults to <see cref="TimeProvider.System"/>.</param>
    public PerUserComputedCache(DiskJsonCache<T> cache, TimeSpan ttl, Func<Guid, IReadOnlyList<T>> compute, ILogger logger, TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _ttl = ttl;
        _compute = compute;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Reads a user's list: computed now when the user has none yet, served as is (and refreshed in
    /// the background) when it is older than the time-to-live.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The user's entries.</returns>
    public IReadOnlyList<T> Get(Guid userId)
    {
        if (TryGetUnsaved(userId, out var kept))
        {
            return kept;
        }

        var fileName = FileName(userId);
        if (_cache.GetLastWriteUtc(fileName) is null)
        {
            lock (UserLock(userId))
            {
                if (_cache.GetLastWriteUtc(fileName) is null && !TryGetUnsaved(userId, out _))
                {
                    Store(userId, _compute(userId));
                }
            }

            if (TryGetUnsaved(userId, out kept))
            {
                return kept;
            }
        }
        else if (IsStale(userId) && _backgroundRefreshes.TryAdd(userId, 0))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    Refresh(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh the cache of user {UserId} in the background.", userId);
                }
                finally
                {
                    _backgroundRefreshes.TryRemove(userId, out _);
                }
            });
        }

        return _cache.Read(fileName);
    }

    /// <summary>Returns whether a user's file is missing or older than the time-to-live.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>True when the file is missing or stale.</returns>
    public bool IsStale(Guid userId) => _cache.IsStale(FileName(userId), _ttl);

    /// <summary>Recomputes and writes a user's full list.</summary>
    /// <param name="userId">The user.</param>
    public void Refresh(Guid userId)
    {
        var entries = _compute(userId);
        lock (UserLock(userId))
        {
            Store(userId, entries);
        }
    }

    /// <summary>
    /// Replaces a user's list by <paramref name="change"/> applied to it; computes the full list
    /// instead when the user has none yet.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="change">Returns the new list from the current one.</param>
    public void Update(Guid userId, Func<IReadOnlyList<T>, IReadOnlyList<T>> change)
    {
        lock (UserLock(userId))
        {
            var fileName = FileName(userId);
            IReadOnlyList<T> entries;
            if (TryGetUnsaved(userId, out var kept))
            {
                entries = change(kept);
            }
            else if (_cache.GetLastWriteUtc(fileName) is null)
            {
                entries = _compute(userId);
            }
            else
            {
                entries = change(_cache.Read(fileName));
            }

            Store(userId, entries);
        }
    }

    private static string FileName(Guid userId) => $"{userId}.json";

    // Writes a user's list, or keeps it in memory when the disk refuses it. Called under the user's lock.
    private void Store(Guid userId, IReadOnlyList<T> entries)
    {
        if (_cache.Write(FileName(userId), entries))
        {
            _unsaved.TryRemove(userId, out _);
        }
        else
        {
            _unsaved[userId] = new Unsaved(_timeProvider.GetUtcNow(), entries);
        }
    }

    private bool TryGetUnsaved(Guid userId, out IReadOnlyList<T> entries)
    {
        if (_unsaved.TryGetValue(userId, out var unsaved))
        {
            if (_timeProvider.GetUtcNow() - unsaved.ComputedAt < _ttl)
            {
                entries = unsaved.Entries;
                return true;
            }

            _unsaved.TryRemove(KeyValuePair.Create(userId, unsaved));
        }

        entries = [];
        return false;
    }

    private object UserLock(Guid userId) => _userLocks.GetOrAdd(userId, _ => new object());

    /// <summary>A list the disk refused, with the time it was computed.</summary>
    private sealed record Unsaved(DateTimeOffset ComputedAt, IReadOnlyList<T> Entries);
}
