using Jellyfin.Plugin.JuxHomepage.TMDb.Models;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Reads and refreshes the on-disk TMDb data cache. Each refresh calls <see cref="ITMDbApiClient"/>
/// and cross-references the results against the local Jellyfin library (by IMDb ID, falling back
/// to TMDb ID), so cached items already know whether -- and where -- they exist locally.
/// </summary>
public interface ITMDbCacheService
{
    /// <summary>Reads the cached weekly trending movies.</summary>
    /// <returns>The cached movies, or an empty list if no refresh has completed yet.</returns>
    IReadOnlyList<TMDbMovie> GetTrendingMovies();

    /// <summary>Reads the cached weekly trending TV shows.</summary>
    /// <returns>The cached shows, or an empty list if no refresh has completed yet.</returns>
    IReadOnlyList<TMDbShow> GetTrendingShows();

    /// <summary>Reads the cached TV shows airing today.</summary>
    /// <returns>The cached shows, or an empty list if no refresh has completed yet.</returns>
    IReadOnlyList<TMDbShow> GetAiringToday();

    /// <summary>Reads the cached all-time top rated movies.</summary>
    /// <returns>The cached movies, or an empty list if no refresh has completed yet.</returns>
    IReadOnlyList<TMDbMovie> GetTopRatedMovies();

    /// <summary>Reads the cached all-time top rated TV shows.</summary>
    /// <returns>The cached shows, or an empty list if no refresh has completed yet.</returns>
    IReadOnlyList<TMDbShow> GetTopRatedShows();

    /// <summary>Reads the cached movies currently in theatres.</summary>
    /// <returns>The cached movies, or an empty list if no refresh has completed yet.</returns>
    IReadOnlyList<TMDbMovie> GetNowPlayingMovies();

    /// <summary>Reads the cached movies for a single Discover widget instance.</summary>
    /// <param name="instanceId">The Discover widget instance's identifier (its config row's <c>ExtraParams["value"]</c>).</param>
    /// <returns>The cached movies, or an empty list if no refresh has completed yet.</returns>
    IReadOnlyList<TMDbMovie> GetDiscoverMovies(string instanceId);

    /// <summary>Refreshes the trending movies cache from TMDb, cross-referencing the local library.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshTrendingMoviesAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the trending shows cache from TMDb, cross-referencing the local library.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshTrendingShowsAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the airing-today cache from TMDb, cross-referencing the local library.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshAiringTodayAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the top-rated-movies cache from TMDb, cross-referencing the local library.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshTopRatedMoviesAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the top-rated-shows cache from TMDb, cross-referencing the local library.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshTopRatedShowsAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the now-playing-movies cache from TMDb, cross-referencing the local library.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshNowPlayingMoviesAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes a single Discover widget instance's cache from TMDb, cross-referencing the local library.</summary>
    /// <param name="instanceId">The Discover widget instance's identifier (its config row's <c>ExtraParams["value"]</c>).</param>
    /// <param name="filter">The instance's configured filter parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshDiscoverMoviesAsync(string instanceId, TMDbDiscoverFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether the given cache type is missing or due: older than the configured refresh
    /// interval (<see cref="Configuration.CacheConfig.TMDbRefreshIntervalHours"/>) less the one-hour
    /// period of the scheduled task, so a list written a moment after a run started is not left
    /// for one more run.
    /// </summary>
    /// <param name="type">The cache type to check.</param>
    /// <returns>True if the cache is absent or due; otherwise false.</returns>
    bool IsStale(TMDbCacheType type);

    /// <summary>
    /// Returns the UTC timestamp of the last successful refresh for the given cache type, for
    /// display in the admin UI.
    /// </summary>
    /// <param name="type">The cache type to check.</param>
    /// <returns>The last refresh timestamp, or null if the cache has never been refreshed.</returns>
    DateTime? GetLastRefreshedUtc(TMDbCacheType type);

    /// <summary>
    /// Returns why the last refresh of the given list came back empty (FIAB-10.c), for the admin UI. Kept in
    /// memory, cleared by the next refresh that returns items, and ignored once the API key has changed.
    /// </summary>
    /// <param name="type">The cache type to check.</param>
    /// <returns>The failure of the last refresh, or null if it succeeded, found nothing, or never ran.</returns>
    TMDbFailureKind? GetLastError(TMDbCacheType type);

    /// <summary>
    /// Refreshes all six fixed TMDb lists in sequence, then every configured Discover widget
    /// instance, whatever their age, like the admin "Refresh now" action. Each individual refresh is
    /// already fault-tolerant (a failure is logged and does not abort the others).
    /// <para>
    /// Atomically reserves the refresh slot (see <see cref="TryAcquireRefreshLock"/>) for the
    /// duration of the call, so a concurrent call (whether the daily scheduled task or a manual
    /// admin trigger) that arrives while one is already running does nothing and returns false,
    /// rather than running a second refresh concurrently.
    /// </para>
    /// </summary>
    /// <param name="progress">
    /// Optional progress reporter, updated as each refresh completes. Pass null when progress
    /// reporting is not needed (e.g. a fire-and-forget trigger).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this call actually performed the refresh; false if one was already running.</returns>
    Task<bool> RefreshAllAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes, among the six fixed lists and the configured Discover instances, only those
    /// <see cref="IsStale"/> calls due, for the hourly scheduled task and the startup catch-up
    /// (QUAL-01: <see cref="Configuration.CacheConfig.TMDbRefreshIntervalHours"/> sets how often a
    /// list is refreshed). Reserves the refresh slot like <see cref="RefreshAllAsync"/>. Does nothing
    /// when no TMDb API key is configured.
    /// </summary>
    /// <param name="progress">Optional progress reporter, updated as each fixed list is handled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this call ran; false if a refresh was already running.</returns>
    Task<bool> RefreshDueAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Non-blocking attempt to reserve the refresh slot, for callers (like the admin "Refresh now"
    /// endpoint) that need a synchronous accept/reject decision before starting a long-running
    /// refresh in the background. Must be paired with a call to
    /// <see cref="RunRefreshLockedAsync"/> to release the slot -- <see cref="RefreshAllAsync"/>
    /// already does this internally and is the safer choice for callers that don't need to
    /// straddle the reservation across a fire-and-forget boundary.
    /// </summary>
    /// <returns>True if the slot was reserved; false if a refresh is already in progress.</returns>
    bool TryAcquireRefreshLock();

    /// <summary>
    /// Runs the refresh, assuming the slot was already reserved via <see cref="TryAcquireRefreshLock"/>.
    /// Always releases the slot afterward, even on failure. Asks TMDb again for the titles it had no
    /// IMDb id for, which a single-list refresh does not (DEC-P06-4).
    /// </summary>
    /// <param name="progress">Optional progress reporter, updated as each refresh completes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RunRefreshLockedAsync(IProgress<double>? progress, CancellationToken cancellationToken);
}
