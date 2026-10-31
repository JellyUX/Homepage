using Jellyfin.Plugin.JuxHomepage.Rewards.Models;

namespace Jellyfin.Plugin.JuxHomepage.Rewards;

/// <summary>
/// Reads and refreshes the on-disk Rewards cache, one file per admin-configured widget instance --
/// mirrors <see cref="TMDb.ITMDbCacheService"/>'s Discover Movies per-instance pattern.
/// </summary>
public interface IRewardsCacheService
{
    /// <summary>Returns the currently cached award winners for the given widget instance.</summary>
    /// <param name="instanceId">The widget instance id (a GUID, from <c>ExtraParams["value"]</c>).</param>
    /// <returns>The cached items, or an empty list if the instance id is invalid or no refresh has completed yet.</returns>
    IReadOnlyList<RewardsWinner> GetRewards(string instanceId);

    /// <summary>
    /// Refreshes every configured Rewards widget instance. Shared with the admin "Refresh now" action
    /// (<see cref="Controllers.JuxHomepageAdminController"/>) and the weekly scheduled task
    /// (<see cref="Tasks.RewardsWeeklyRefreshTask"/>), so the instance-enumeration logic isn't duplicated.
    /// Guarded by <see cref="TryAcquireRefreshLock"/>/<see cref="RunRefreshLockedAsync"/> internally --
    /// a concurrent call (manual button pressed twice, or overlapping with the weekly scheduled task)
    /// is logged and skipped rather than running two refreshes against Wikidata at once.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshAllInstancesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to reserve the refresh lock without blocking. Mirrors
    /// <see cref="TMDb.ITMDbCacheService.TryAcquireRefreshLock"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the lock was acquired; otherwise <see langword="false"/>.</returns>
    bool TryAcquireRefreshLock();

    /// <summary>
    /// Runs the refresh assuming the caller has already reserved the lock via
    /// <see cref="TryAcquireRefreshLock"/>. Always releases the lock when done, even on failure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RunRefreshLockedAsync(CancellationToken cancellationToken);
}
