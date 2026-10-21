using Jellyfin.Plugin.JuxHomepage.Configuration;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// The liked movie and series ids of each user, kept in memory (PERF-05.a): every page load asks for
/// them (<c>Watchlist/Ids</c>), and the query reads the user data of the whole library.
/// <para>
/// <see cref="LikedItemIdsUpdateListener"/> keeps a user's ids exact as titles are liked and unliked
/// (<see cref="Apply"/>) or leave the library (<see cref="Remove"/>). What no event reports, such as
/// a change of the user's library access, lasts at most the layout TTL
/// (<see cref="CacheConfig.SessionTtlMinutes"/>), and the daily Watchlist task drops every user's ids
/// (<see cref="Clear"/>). Ids read before one of these changes are never stored
/// (<c>LikedIds_ChangedWhileRead_AreNotStored</c>).
/// </para>
/// </summary>
public sealed class LikedItemIdsCache
{
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<Guid, long> _userVersions = [];
    private readonly Lock _lock = new();
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly TimeProvider _timeProvider;
    private long _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="LikedItemIdsCache"/> class.
    /// </summary>
    /// <param name="getConfiguration">Returns the current plugin configuration, read for the TTL.</param>
    /// <param name="timeProvider">Time source for expiry. Defaults to <see cref="TimeProvider.System"/>.</param>
    public LikedItemIdsCache(Func<PluginConfiguration?> getConfiguration, TimeProvider? timeProvider = null)
    {
        _getConfiguration = getConfiguration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private TimeSpan Ttl => TimeSpan.FromMinutes(Math.Max(1, _getConfiguration()?.Cache?.SessionTtlMinutes ?? 15));

    /// <summary>
    /// The user's current version. Read it before querying the ids and pass it to <see cref="Set"/>.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The version.</returns>
    public long VersionOf(Guid userId)
    {
        lock (_lock)
        {
            return _version + _userVersions.GetValueOrDefault(userId);
        }
    }

    /// <summary>Returns the user's liked ids while they are stored and not expired.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ids">A copy of the ids, or an empty list on a miss.</param>
    /// <returns>True on a hit.</returns>
    public bool TryGet(Guid userId, out IReadOnlyList<Guid> ids)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(userId, out var entry) && UtcNow < entry.ExpiresAt)
            {
                ids = [.. entry.Ids];
                return true;
            }
        }

        ids = [];
        return false;
    }

    /// <summary>
    /// Stores the user's liked ids, unless a change reached them since <paramref name="version"/> was read.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="ids">The ids the query returned.</param>
    /// <param name="version">The <see cref="VersionOf"/> read before the query.</param>
    /// <returns>True when the ids were stored.</returns>
    public bool Set(Guid userId, IReadOnlyList<Guid> ids, long version)
    {
        lock (_lock)
        {
            if (version != _version + _userVersions.GetValueOrDefault(userId))
            {
                return false;
            }

            _entries[userId] = new Entry([.. ids], UtcNow + Ttl);
            return true;
        }
    }

    /// <summary>Records that the user liked or unliked a movie or a series.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="itemId">The movie or series.</param>
    /// <param name="liked">Whether the title is liked now.</param>
    public void Apply(Guid userId, Guid itemId, bool liked)
    {
        lock (_lock)
        {
            _userVersions[userId] = _userVersions.GetValueOrDefault(userId) + 1;
            if (!_entries.TryGetValue(userId, out var entry))
            {
                return;
            }

            if (liked)
            {
                entry.Ids.Add(itemId);
            }
            else
            {
                entry.Ids.Remove(itemId);
            }
        }
    }

    /// <summary>Removes an item that left the library from every user's ids.</summary>
    /// <param name="itemId">The item.</param>
    public void Remove(Guid itemId)
    {
        lock (_lock)
        {
            _version++;
            foreach (var entry in _entries.Values)
            {
                entry.Ids.Remove(itemId);
            }
        }
    }

    /// <summary>Drops every user's ids.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _version++;
            _entries.Clear();
        }
    }

    private sealed record Entry(HashSet<Guid> Ids, DateTime ExpiresAt);
}
