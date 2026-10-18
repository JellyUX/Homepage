using System.Collections.Concurrent;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Analyzes a user's watch history and favorites to derive scored preferences (top genres, top
/// actors, top directors, recently watched films/shows) consumed by personalized widgets.
/// <para>
/// Scoring is computed once per user and day (F-02): a snapshot stays valid until the next day
/// boundary in server time, and the Because You Watched rotation is seeded by (user, date), so
/// reloads on the same day pick the same titles. A user without history gets an empty snapshot
/// that expires after <see cref="Configuration.CacheConfig.SessionTtlMinutes"/> instead, so a first
/// finished title shows up the same day. The first access of a new day purges the previous days'
/// snapshots, including those of users who no longer visit.
/// </para>
/// <para>
/// A request never waits on a recompute (F-03): an expired snapshot is served while a fresh one is
/// computed in the background, concurrent requests of one user share a single computation, and at
/// most one computation runs at a time for the whole server, behind a bounded queue. Only a user's
/// very first access waits, and at most <c>firstComputeTimeout</c>; past it an empty result is
/// served and the layout built from it is not cached (<see cref="IPersonalizationState"/>).
/// </para>
/// </summary>
public sealed class ScoringService : IPersonalizationState, IDisposable
{
    private const int MaxQueuedComputes = 8;

    private const int MaxWatchedScan = 500;

    private const int EpisodePageSize = 500;

    /// <summary>
    /// Number of most recent titles whose people (actors, directors) are read. Jellyfin has no
    /// batch people lookup, so each title costs one query (~3.8 ms each on a host copy of a
    /// 3000-film library: 1.9 s for 500 titles, F-06); genres still use the whole history.
    /// </summary>
    private const int MaxPeopleScan = 100;

    /// <summary>
    /// People scan of a user's very first computation, which a request may wait on (2 s at most):
    /// ~650 ms for 25 titles on a 40-people-per-film copy (1.7). The full <see cref="MaxPeopleScan"/>
    /// computation follows in the background at once and replaces it.
    /// </summary>
    private const int InitialPeopleScan = 25;

    /// <summary>
    /// Upper bound on played-episode pages read to find watched series, so a user with a very long
    /// episode history of few series cannot turn one recompute into an unbounded walk.
    /// </summary>
    private const int MaxEpisodePages = 10;

    /// <summary>
    /// Size of the "recently watched" candidate pool shuffled by <see cref="GetRecentlyWatched"/>
    /// before scope-filtering (TODO_V3.md Phase 9.2). Fixed and independent of any caller's
    /// requested <c>limit</c>, so that multiple Because You Watched rows (each calling this method
    /// with a different, increasing <c>limit</c> -- see <see cref="PersonalizedWidgetBase.Resolve"/>)
    /// read consistent prefixes of the very same shuffled sequence rather than each shuffling
    /// independently, which would risk the same candidate being picked by two different rows.
    /// </summary>
    private const int RecentlyWatchedPoolSize = 10;

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ConcurrentDictionary<Guid, ScoreSnapshot> _cache = new();
    private readonly ConcurrentDictionary<Guid, Lazy<Task<ScoreSnapshot>?>> _inFlight = new();
    private readonly ConcurrentDictionary<Guid, byte> _pendingServed = new();
    private readonly SemaphoreSlim _computeGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ILogger<ScoringService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _firstComputeTimeout;
    private int _queuedComputes;
    private int _generation;
    private int _lastPurgeDay;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScoringService"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="userDataManager">
    /// Jellyfin user data manager, used to read each watched item's actual last-played date so
    /// movies and series (queried separately, see <see cref="ComputeSnapshot"/>) can be merged into a
    /// single, genuinely recency-ordered list rather than one type always ranking before the other.
    /// </param>
    /// <param name="getConfiguration">
    /// Factory that returns the current plugin configuration, used to read the cache TTL.
    /// Defaults to <c>Plugin.Instance?.Configuration</c> in production.
    /// </param>
    /// <param name="logger">Logger for background computation failures. Defaults to a null logger.</param>
    /// <param name="timeProvider">
    /// Time source for snapshot expiry and for the first-access wait. Defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    /// <param name="firstComputeTimeout">
    /// How long a user's first access waits for their first snapshot. Defaults to 2 seconds.
    /// </param>
    public ScoringService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<ScoringService>? logger = null,
        TimeProvider? timeProvider = null,
        TimeSpan? firstComputeTimeout = null)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _getConfiguration = getConfiguration;
        _logger = logger ?? NullLogger<ScoringService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _firstComputeTimeout = firstComputeTimeout ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Returns the user's top genres by watch history, ranked highest-scored first.</summary>
    /// <param name="userId">The user to score.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>Up to <paramref name="limit"/> scored genres.</returns>
    public IReadOnlyList<ScoredValue> GetTopGenres(Guid userId, int limit) =>
        GetSnapshot(userId).Genres.Take(limit).ToList().AsReadOnly();

    /// <summary>Returns the user's top actors by watch history, ranked highest-scored first.</summary>
    /// <param name="userId">The user to score.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>Up to <paramref name="limit"/> scored actors.</returns>
    public IReadOnlyList<ScoredValue> GetTopActors(Guid userId, int limit) =>
        GetSnapshot(userId).Actors.Take(limit).ToList().AsReadOnly();

    /// <summary>Returns the user's top directors by watch history, ranked highest-scored first.</summary>
    /// <param name="userId">The user to score.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>Up to <paramref name="limit"/> scored directors.</returns>
    public IReadOnlyList<ScoredValue> GetTopDirectors(Guid userId, int limit) =>
        GetSnapshot(userId).Directors.Take(limit).ToList().AsReadOnly();

    /// <summary>Returns the user's most recently watched movies and/or series, most recent first.</summary>
    /// <param name="userId">The user to score.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="scope">
    /// Restricts which item types are eligible (see <see cref="BecauseYouWatchedScope"/>). Defaults to
    /// <see cref="BecauseYouWatchedScope.Both"/>.
    /// </param>
    /// <returns>Up to <paramref name="limit"/> recently watched items (Value=item GUID, Label=title).</returns>
    public IReadOnlyList<ScoredValue> GetRecentlyWatched(
        Guid userId,
        int limit,
        BecauseYouWatchedScope scope = BecauseYouWatchedScope.Both)
    {
        var snapshot = GetSnapshot(userId);

        // Shuffle a fixed-size pool of the most recent entries -- not the full RecentlyWatched list,
        // and not scope-filtered yet -- BEFORE applying scope/limit. Seeded from (userId, the
        // snapshot's day), so the order is the same all day, across recomputes and restarts, and
        // rotates at the day boundary (TODO_V3.md Phase 9.2: "Because You Watched" rotation).
        // HashCode.Combine is not used: it is randomized per process. Shuffling before the scope
        // filter, rather than filtering then shuffling per scope, keeps different rows' picks
        // consistent with each other: see the seed/pool-size remarks on RecentlyWatchedPoolSize.
        var pool = snapshot.RecentlyWatched.Take(RecentlyWatchedPoolSize).ToList();
        var seed = unchecked((userId.GetHashCode() * 397) ^ snapshot.Day.DayNumber);
        SeededShuffle.Shuffle(pool, seed);

        var entries = pool.AsEnumerable();
        entries = scope switch
        {
            BecauseYouWatchedScope.Movies => entries.Where(e => e.Kind == BaseItemKind.Movie),
            BecauseYouWatchedScope.Series => entries.Where(e => e.Kind == BaseItemKind.Series),
            _ => entries
        };

        return entries.Take(limit).Select(e => new ScoredValue(e.Value, e.Label)).ToList().AsReadOnly();
    }

    /// <summary>
    /// Clears all cached scoring snapshots, forcing recomputation on next access. A computation
    /// already running when this is called does not store its result.
    /// </summary>
    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _cache.Clear();
    }

    /// <summary>
    /// Marks the user's snapshot stale after they finished a title: it is still served, so the
    /// request does not wait, and a fresh one is computed in the background.
    /// </summary>
    /// <param name="userId">The user whose snapshot to refresh.</param>
    public void MarkStale(Guid userId)
    {
        if (_cache.TryGetValue(userId, out var cached) && !cached.Stale)
        {
            _cache.TryUpdate(userId, cached with { Stale = true }, cached);
        }
    }

    /// <inheritdoc/>
    public bool TakePendingServed(Guid userId) => _pendingServed.TryRemove(userId, out _);

    /// <inheritdoc/>
    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _computeGate.Dispose();
    }

    /// <summary>
    /// Test-only seam: returns whether a snapshot of <paramref name="userId"/> is held in memory.
    /// </summary>
    /// <param name="userId">The user to look up.</param>
    /// <returns>True if a snapshot is cached for the user.</returns>
    internal bool HasSnapshotForTesting(Guid userId) => _cache.ContainsKey(userId);

    /// <summary>
    /// Test-only seam: completes once no computation is running, including the full computation
    /// that follows a partial first snapshot. Call it after the computation was started.
    /// </summary>
    /// <returns>A task that completes when the in-flight table is empty.</returns>
    internal async Task WhenIdleForTesting()
    {
        while (!_inFlight.IsEmpty)
        {
            foreach (var running in _inFlight.Values)
            {
                if (running.Value is { } compute)
                {
                    await Task.WhenAny(compute).ConfigureAwait(false);
                }
            }
        }
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    private ScoreSnapshot GetSnapshot(Guid userId)
    {
        var today = Today;
        PurgeIfNewDay(today);

        var hasCached = _cache.TryGetValue(userId, out var cached);
        if (hasCached && IsFresh(cached!, today))
        {
            return cached!;
        }

        var compute = StartCompute(userId);
        if (hasCached)
        {
            return cached!;
        }

        if (compute is not null && WaitForFirstCompute(compute))
        {
            return compute.Result;
        }

        _pendingServed[userId] = 0;
        return ScoreSnapshot.CreateEmpty(UtcNow, Today);
    }

    private bool IsFresh(ScoreSnapshot snapshot, DateOnly today)
    {
        if (snapshot.Stale || snapshot.Partial)
        {
            return false;
        }

        if (snapshot.RecentlyWatched.Count == 0)
        {
            var ttl = TimeSpan.FromMinutes(_getConfiguration()?.Cache?.SessionTtlMinutes ?? 15);
            return UtcNow - snapshot.ComputedAt < ttl;
        }

        return snapshot.Day == today;
    }

    /// <summary>
    /// On the first access of a new day, drops every snapshot computed on an earlier day, so the
    /// cache never holds more than one day of users.
    /// </summary>
    private void PurgeIfNewDay(DateOnly today)
    {
        var previous = Volatile.Read(ref _lastPurgeDay);
        if (previous == today.DayNumber
            || Interlocked.CompareExchange(ref _lastPurgeDay, today.DayNumber, previous) != previous)
        {
            return;
        }

        foreach (var entry in _cache)
        {
            if (entry.Value.Day < today)
            {
                _cache.TryRemove(entry);
            }
        }
    }

    private bool WaitForFirstCompute(Task<ScoreSnapshot> compute)
    {
        try
        {
            // Through the time provider, so a test can expire the wait without sleeping.
            compute.WaitAsync(_firstComputeTimeout, _timeProvider).Wait();
            return compute.IsCompletedSuccessfully;
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the user's running computation, or starts one. Returns null when the queue is full:
    /// the caller then serves what it has, and a later request retries.
    /// </summary>
    private Task<ScoreSnapshot>? StartCompute(Guid userId)
    {
        if (_inFlight.TryGetValue(userId, out var running))
        {
            return running.Value;
        }

        // Only the one computation that enters the table takes a place in the queue, decided once
        // inside it: a caller that joins it never counts (A-01, a burst of callers counted each
        // other and was refused next to an almost full queue), and every caller of a refused
        // computation sees the same null.
        var peopleScan = _cache.ContainsKey(userId) ? MaxPeopleScan : InitialPeopleScan;
        Lazy<Task<ScoreSnapshot>?>? created = null;
        created = new Lazy<Task<ScoreSnapshot>?>(() =>
        {
            if (Interlocked.Increment(ref _queuedComputes) > MaxQueuedComputes)
            {
                Interlocked.Decrement(ref _queuedComputes);
                _inFlight.TryRemove(new KeyValuePair<Guid, Lazy<Task<ScoreSnapshot>?>>(userId, created!));
                return null;
            }

            // A dedicated thread, not the thread pool: callers block on this task
            // (WaitForFirstCompute), and under load a pool-scheduled computation would wait for a
            // pool thread held by those very callers. The bounded queue caps how many of these
            // threads can exist.
            return Task.Factory.StartNew(
                () => RunCompute(userId, peopleScan),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        });

        return _inFlight.GetOrAdd(userId, created).Value;
    }

    private ScoreSnapshot RunCompute(Guid userId, int peopleScan)
    {
        var snapshot = RunComputeOnce(userId, peopleScan, out var stored);

        // A partial first snapshot is followed at once by the full one, now that this computation
        // has left the in-flight table; meanwhile the partial snapshot is served.
        if (stored && snapshot.Partial)
        {
            StartCompute(userId);
        }

        return snapshot;
    }

    private ScoreSnapshot RunComputeOnce(Guid userId, int peopleScan, out bool stored)
    {
        stored = false;
        var generation = Volatile.Read(ref _generation);
        try
        {
            var token = _shutdown.Token;
            _computeGate.Wait(token);
            try
            {
                var snapshot = ComputeSnapshot(userId, peopleScan, token);
                if (generation == Volatile.Read(ref _generation))
                {
                    _cache[userId] = snapshot;
                    stored = true;
                }

                return snapshot;
            }
            finally
            {
                _computeGate.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
        {
            _logger.LogError(ex, "Scoring computation failed for user {UserId}", userId);
            throw;
        }
        finally
        {
            _inFlight.TryRemove(userId, out _);
            Interlocked.Decrement(ref _queuedComputes);
        }
    }

    private ScoreSnapshot ComputeSnapshot(Guid userId, int peopleScan, CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return ScoreSnapshot.CreateEmpty(UtcNow, Today);
        }

        var watchedMovies = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsPlayed = true,
            Recursive = true,
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            Limit = MaxWatchedScan,
            DtoOptions = new DtoOptions { Fields = [], EnableImages = false }
        });

        var seriesLastPlayed = GetWatchedSeriesLastPlayed(user, cancellationToken);
        var watchedSeries = seriesLastPlayed.Count == 0
            ? []
            : _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Series],
                ItemIds = [.. seriesLastPlayed.Keys],
                DtoOptions = new DtoOptions { Fields = [], EnableImages = false }
            });

        if (watchedMovies.Count == 0 && watchedSeries.Count == 0)
        {
            return ScoreSnapshot.CreateEmpty(UtcNow, Today);
        }

        // A Series has no last-played date of its own, so it is dated by its most recently played
        // episode; a movie by its own user data. Without that, every series sorted after every movie.
        var watched = watchedMovies.Concat(watchedSeries)
            .OrderByDescending(item => item is Series
                ? seriesLastPlayed.GetValueOrDefault(item.Id)
                : _userDataManager.GetUserData(user, item)?.LastPlayedDate ?? DateTime.MinValue)
            .Take(MaxWatchedScan)
            .ToList();

        var favoriteIds = _libraryManager.GetItemIds(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsFavorite = true,
            Recursive = true,
            Limit = MaxWatchedScan
        }).ToHashSet();

        var genreScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var actorScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var directorScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < watched.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = watched[i];

            // Earlier position in the DatePlayed-descending list means more recently watched;
            // give it a small recency bonus on top of the base weight of 1. Favorites get a
            // further fixed bonus so favorited titles influence scoring more than a single watch.
            var recencyWeight = 1.0 + ((watched.Count - i) / (double)watched.Count);
            var weight = recencyWeight + (favoriteIds.Contains(item.Id) ? 1.5 : 0);

            // Genres/People are read identically regardless of item type: a Series carries its own
            // main-cast People and Genres (populated by the TMDb series metadata provider), so no
            // special-casing is needed here -- confirmed against Jellyfin's actual provider behavior
            // before relying on it (see Phase 1.3 research notes).
            foreach (var genre in item.Genres)
            {
                genreScores[genre] = genreScores.GetValueOrDefault(genre) + weight;
            }

            if (i >= peopleScan)
            {
                continue;
            }

            foreach (var person in _libraryManager.GetPeople(item))
            {
                if (person.Type == PersonKind.Actor)
                {
                    actorScores[person.Name] = actorScores.GetValueOrDefault(person.Name) + weight;
                }
                else if (person.Type == PersonKind.Director)
                {
                    directorScores[person.Name] = directorScores.GetValueOrDefault(person.Name) + weight;
                }
            }
        }

        return new ScoreSnapshot(
            Rank(genreScores),
            Rank(actorScores),
            Rank(directorScores),
            watched.Select(i => new RecentlyWatchedEntry(
                i.Id.ToString(),
                i.Name,
                i is Series ? BaseItemKind.Series : BaseItemKind.Movie)).ToList(),
            UtcNow,
            Today)
        {
            Partial = peopleScan < MaxPeopleScan && watched.Count > peopleScan
        };
    }

    /// <summary>
    /// Returns the series the user has watched at least one episode of, mapped to the last-played
    /// date of their most recently played episode, most recent first.
    /// <para>
    /// Walks played episodes rather than querying <c>Series</c> with <c>IsPlayed</c>: that query
    /// compiles to correlated <c>EXISTS</c> sub-queries that scan every non-folder row per series
    /// candidate (F-01, ~110 s on a 3000-film library), while the episode query is served by the
    /// <c>Type</c> and <c>UserData</c> indexes. Pages are fetched until <see cref="MaxWatchedScan"/>
    /// distinct series are found, capped at <see cref="MaxEpisodePages"/> pages.
    /// </para>
    /// </summary>
    private Dictionary<Guid, DateTime> GetWatchedSeriesLastPlayed(User user, CancellationToken cancellationToken)
    {
        var lastPlayed = new Dictionary<Guid, DateTime>();

        for (var page = 0; page < MaxEpisodePages && lastPlayed.Count < MaxWatchedScan; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                IsPlayed = true,
                Recursive = true,
                OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
                StartIndex = page * EpisodePageSize,
                Limit = EpisodePageSize,
                DtoOptions = new DtoOptions { Fields = [], EnableImages = false }
            });

            // Episodes arrive most recently played first, so the first one seen for a series
            // carries that series' latest play date; later ones are skipped without a user-data read.
            foreach (var episode in episodes.OfType<Episode>())
            {
                if (episode.SeriesId != Guid.Empty && !lastPlayed.ContainsKey(episode.SeriesId))
                {
                    lastPlayed[episode.SeriesId] =
                        _userDataManager.GetUserData(user, episode)?.LastPlayedDate ?? DateTime.MinValue;
                }
            }

            if (episodes.Count < EpisodePageSize)
            {
                break;
            }
        }

        return lastPlayed;
    }

    private static IReadOnlyList<ScoredValue> Rank(Dictionary<string, double> scores) =>
        scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ScoredValue(kv.Key, kv.Key))
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// A recently-watched item retained for "Because You Watched" reference selection, tagged with
    /// its <see cref="BaseItemKind"/> so <see cref="GetRecentlyWatched"/> can honor a per-row scope
    /// (movies only / series only / both).
    /// </summary>
    private sealed record RecentlyWatchedEntry(string Value, string Label, BaseItemKind Kind);

    private sealed record ScoreSnapshot(
        IReadOnlyList<ScoredValue> Genres,
        IReadOnlyList<ScoredValue> Actors,
        IReadOnlyList<ScoredValue> Directors,
        IReadOnlyList<RecentlyWatchedEntry> RecentlyWatched,
        DateTime ComputedAt,
        DateOnly Day)
    {
        /// <summary>
        /// Gets a value indicating whether the user finished a title since this snapshot was
        /// computed: it is still served, but replaced in the background.
        /// </summary>
        public bool Stale { get; init; }

        /// <summary>
        /// Gets a value indicating whether people were read from the most recent titles only
        /// (<see cref="InitialPeopleScan"/>): served while the full computation runs.
        /// </summary>
        public bool Partial { get; init; }

        /// <summary>
        /// Creates an empty snapshot stamped with the current time, so that -- unlike a single
        /// shared static instance -- it still expires normally under <see cref="GetSnapshot"/>'s TTL
        /// check instead of appearing permanently stale (and thus always recomputed) the moment the
        /// process has been running longer than one TTL period.
        /// </summary>
        /// <param name="computedAt">The current time.</param>
        /// <param name="day">The current day in server time.</param>
        /// <returns>A freshly stamped empty snapshot.</returns>
        public static ScoreSnapshot CreateEmpty(DateTime computedAt, DateOnly day) => new([], [], [], [], computedAt, day);
    }
}
