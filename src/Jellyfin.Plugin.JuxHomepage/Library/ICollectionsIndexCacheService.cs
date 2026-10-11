using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Library.Models;

namespace Jellyfin.Plugin.JuxHomepage.Library;

/// <summary>
/// Reads and refreshes the on-disk Collections reverse index (item -&gt; collections it belongs to),
/// global rather than per-user. Moved server-side (TODO_V3.md Phase 4.3) so the "Included In"
/// feature on an item's detail page (Phase 7.2) never has to scan every BoxSet live per request.
/// </summary>
public interface ICollectionsIndexCacheService
{
    /// <summary>Returns the collections a single item belongs to.</summary>
    /// <param name="itemId">The item to look up.</param>
    /// <returns>Every collection this item belongs to, or an empty list if none (or no refresh has completed yet).</returns>
    IReadOnlyList<CollectionRef> GetCollectionsFor(Guid itemId);

    /// <summary>
    /// Returns the collections a single item belongs to, as seen by <paramref name="user"/>: empty when
    /// the user may not open the item itself, otherwise only the collections the user may open.
    /// </summary>
    /// <param name="itemId">The item to look up.</param>
    /// <param name="user">The user the answer is returned to.</param>
    /// <returns>The visible collections this item belongs to, or an empty list.</returns>
    IReadOnlyList<CollectionRef> GetCollectionsFor(Guid itemId, User user);

    /// <summary>Returns whether the index is missing or older than the fixed refresh interval.</summary>
    /// <returns>True if the index is absent or stale; otherwise false.</returns>
    bool IsStale();

    /// <summary>
    /// Refreshes the whole index from the current state of the library. Atomically reserves the
    /// refresh slot (see <see cref="TryAcquireRefreshLock"/>) for the duration of the call, so a
    /// concurrent call does nothing and returns false rather than running a second refresh
    /// concurrently.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this call actually performed the refresh; false if one was already running.</returns>
    Task<bool> RefreshAsync(CancellationToken cancellationToken);

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
