using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Reads and refreshes the on-disk, per-user Series Progress cache -- an exhaustive list of every
/// series a user has watched at least one episode of, with watched/total episode counts. Moved
/// server-side (TODO_V3.md Phase 4.3) so the "Series Progress" view (Phase 6.1) never has to compute
/// this live per request.
/// </summary>
public interface ISeriesProgressCacheService
{
    /// <summary>Reads the cached series progress entries for a user.</summary>
    /// <param name="userId">The user to read progress for.</param>
    /// <returns>The cached entries, computed first when the user has none yet (TODO_V4.md 8.2).</returns>
    IReadOnlyList<SeriesProgressEntry> GetProgress(Guid userId);

    /// <summary>
    /// Returns whether the cache for the given user is missing or older than the fixed refresh
    /// interval.
    /// </summary>
    /// <param name="userId">The user to check.</param>
    /// <returns>True if the cache is absent or stale; otherwise false.</returns>
    bool IsStale(Guid userId);

    /// <summary>
    /// Recomputes the entries of <paramref name="seriesIds"/> for a user after a watch (TODO_V4.md
    /// 8.2, F-54), leaving the user's other series as cached; computes the full list when the user
    /// has none yet. A series with no played episode left is removed.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="seriesIds">The series whose played state changed.</param>
    void UpdateSeries(Guid userId, IReadOnlyCollection<Guid> seriesIds);

    /// <summary>
    /// Refreshes every user's series progress cache in sequence. Each user's computation is
    /// independently fault-tolerant (a failure is logged and does not abort the others).
    /// Atomically reserves the refresh slot (see <see cref="TryAcquireRefreshLock"/>) for the
    /// duration of the call, so a concurrent call does nothing and returns false rather than running
    /// a second refresh concurrently.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this call actually performed the refresh; false if one was already running.</returns>
    Task<bool> RefreshAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Non-blocking attempt to reserve the refresh slot. Must be paired with a call to
    /// <see cref="RunRefreshLockedAsync"/> to release the slot.
    /// </summary>
    /// <returns>True if the slot was reserved; false if a refresh is already in progress.</returns>
    bool TryAcquireRefreshLock();

    /// <summary>
    /// Runs the refresh, assuming the slot was already reserved via <see cref="TryAcquireRefreshLock"/>.
    /// Always releases the slot afterward, even on failure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RunRefreshLockedAsync(CancellationToken cancellationToken);
}
