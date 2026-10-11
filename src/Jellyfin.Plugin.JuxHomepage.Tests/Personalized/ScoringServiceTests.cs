using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Personalized;

public sealed class ScoringServiceTests
{
    private static PersonInfo Actor(string name) => new() { Name = name, Type = PersonKind.Actor };

    private static PersonInfo Director(string name) => new() { Name = name, Type = PersonKind.Director };

    private static ScoringService BuildService(
        User user,
        IReadOnlyList<BaseItem> watched,
        IReadOnlyList<BaseItem> favorites,
        Func<BaseItem, IReadOnlyList<PersonInfo>> people,
        IReadOnlyList<BaseItem>? watchedSeries = null,
        IReadOnlyDictionary<Guid, DateTime>? lastPlayed = null,
        TimeProvider? timeProvider = null)
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        // Each watched series is reached through one played episode, as ComputeSnapshot does.
        var series = watchedSeries ?? [];
        foreach (var s in series.Where(s => s.Id == Guid.Empty))
        {
            s.Id = Guid.NewGuid();
        }

        var episodes = series
            .Select(s => (BaseItem)new Episode { Id = Guid.NewGuid(), SeriesId = s.Id, Name = $"{s.Name} E1" })
            .ToList();

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns(watched);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Episode }))))
            .Returns((InternalItemsQuery q) => episodes.Skip(q.StartIndex ?? 0).Take(q.Limit ?? int.MaxValue).ToList());
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Series }))))
            .Returns((InternalItemsQuery q) => series.Where(s => q.ItemIds.Contains(s.Id)).ToList());
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(q => q.IsFavorite == true)))
            .Returns(favorites.Select(f => f.Id).ToList());
        libraryManagerMock
            .Setup(m => m.GetPeople(It.IsAny<BaseItem>()))
            .Returns((BaseItem item) => people(item));

        // Without a lastPlayed entry an item falls back to DateTime.MinValue, so the stable
        // OrderByDescending keeps the movies-then-series concat order for tests that do not exercise
        // cross-type recency. An episode's date is looked up under its series id.
        var userDataManagerMock = new Mock<IUserDataManager>();
        if (lastPlayed is not null)
        {
            userDataManagerMock
                .Setup(m => m.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()))
                .Returns((User _, BaseItem item) =>
                {
                    var key = item is Episode episode ? episode.SeriesId : item.Id;
                    return lastPlayed.TryGetValue(key, out var date)
                        ? new UserItemData { Key = key.ToString(), LastPlayedDate = date }
                        : null;
                });
        }

        return new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            userDataManagerMock.Object,
            () => new PluginConfiguration(),
            timeProvider: timeProvider);
    }

    /// <summary>
    /// Builds a service whose computation blocks for <paramref name="computeDelay"/> in the watched
    /// movies query, and records how many computations ran and the peak number running at once.
    /// </summary>
    private static ScoringService BuildSlowService(
        TimeSpan computeDelay,
        TimeSpan firstComputeTimeout,
        ComputeCounters counters,
        TimeProvider? timeProvider = null)
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock
            .Setup(m => m.GetUserById(It.IsAny<Guid>()))
            .Returns(() => new User("test", "Default", "Default"));

        var genreMovie = new Movie { Id = Guid.NewGuid(), Name = "Film", Genres = ["Drama"] };
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns(() =>
            {
                Interlocked.Increment(ref counters.Computations);
                var running = Interlocked.Increment(ref counters.Running);
                lock (counters)
                {
                    counters.PeakRunning = Math.Max(counters.PeakRunning, running);
                }

                Thread.Sleep(computeDelay);
                Interlocked.Decrement(ref counters.Running);
                return [genreMovie];
            });
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite == true || !q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns([]);
        libraryManagerMock.Setup(m => m.GetPeople(It.IsAny<BaseItem>())).Returns([]);

        return new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration(),
            timeProvider: timeProvider,
            firstComputeTimeout: firstComputeTimeout);
    }

    // -------------------------------------------------------------------------
    // Single-flight, stale-while-revalidate, global bound (F-03)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetSnapshot_TwoSimultaneousCallsOfOneUser_ComputeOnce()
    {
        var counters = new ComputeCounters();
        var service = BuildSlowService(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5), counters);
        var userId = Guid.NewGuid();

        var results = await Task.WhenAll(
            Task.Run(() => service.GetTopGenres(userId, 5)),
            Task.Run(() => service.GetTopGenres(userId, 5)));

        Assert.Equal(1, counters.Computations);
        Assert.All(results, r => Assert.Equal("Drama", Assert.Single(r).Value));
    }

    [Fact]
    public async Task GetSnapshot_TenUsersFirstAccess_AtMostOneConcurrentComputeAndBoundedWait()
    {
        var counters = new ComputeCounters();
        var timeout = TimeSpan.FromMilliseconds(500);
        var service = BuildSlowService(TimeSpan.FromMilliseconds(300), timeout, counters);

        var elapsed = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            service.GetTopGenres(Guid.NewGuid(), 5);
            return stopwatch.Elapsed;
        })));

        Assert.Equal(1, counters.PeakRunning);
        Assert.All(elapsed, e => Assert.True(e < timeout + TimeSpan.FromMilliseconds(400), $"waited {e}"));
    }

    [Fact]
    public void GetSnapshot_FirstComputeTimesOut_ServesEmptyAndFlagsPending()
    {
        var counters = new ComputeCounters();
        var service = BuildSlowService(TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(100), counters);
        var userId = Guid.NewGuid();

        var first = service.GetTopGenres(userId, 5);

        Assert.Empty(first);
        Assert.True(service.TakePendingServed(userId));
        Assert.False(service.TakePendingServed(userId));

        Thread.Sleep(800);
        Assert.Equal("Drama", Assert.Single(service.GetTopGenres(userId, 5)).Value);
        Assert.False(service.TakePendingServed(userId));
    }

    [Fact]
    public void GetSnapshot_StaleSnapshot_ServedImmediatelyWhileRecomputing()
    {
        var counters = new ComputeCounters();
        var service = BuildSlowService(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), counters);
        var userId = Guid.NewGuid();
        service.GetTopGenres(userId, 5);

        service.MarkStale(userId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stale = service.GetTopGenres(userId, 5);
        stopwatch.Stop();

        Assert.Equal("Drama", Assert.Single(stale).Value);
        Assert.True(stopwatch.ElapsedMilliseconds < 50, $"waited {stopwatch.ElapsedMilliseconds} ms");
        Assert.False(service.TakePendingServed(userId));
        Thread.Sleep(800);
        Assert.Equal(2, counters.Computations);
    }

    [Fact]
    public void Clear_DuringComputation_DoesNotStoreTheOldResult()
    {
        var counters = new ComputeCounters();
        var service = BuildSlowService(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50), counters);
        var userId = Guid.NewGuid();

        service.GetTopGenres(userId, 5);
        service.Clear();
        Thread.Sleep(500);
        service.GetTopGenres(userId, 5);

        Assert.Equal(2, counters.Computations);
    }

    // -------------------------------------------------------------------------
    // Daily rotation and invalidation (F-02)
    // -------------------------------------------------------------------------

    private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetRecentlyWatched_SameDay_SameChoiceAcrossReloadsAndRestarts()
    {
        var user = new User("test", "Default", "Default");
        var items = Enumerable.Range(0, 8)
            .Select(i => (BaseItem)new Movie { Id = Guid.NewGuid(), Name = $"Film {i}", Genres = [] })
            .ToList();
        var clock = new ManualTimeProvider { UtcNowValue = Noon };

        var morning = BuildService(user, items, [], _ => [], timeProvider: clock).GetRecentlyWatched(user.Id, 3);
        clock.UtcNowValue = Noon.AddHours(11);
        var restarted = BuildService(user, items, [], _ => [], timeProvider: clock).GetRecentlyWatched(user.Id, 3);

        Assert.Equal(morning.Select(r => r.Value), restarted.Select(r => r.Value));
    }

    [Fact]
    public void GetSnapshot_DayBoundary_RecomputesAndPurgesInactiveUsers()
    {
        var counters = new ComputeCounters();
        var clock = new ManualTimeProvider { UtcNowValue = Noon };
        var service = BuildSlowService(TimeSpan.Zero, TimeSpan.FromSeconds(5), counters, clock);
        var active = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        service.GetTopGenres(active, 5);
        service.GetTopGenres(inactive, 5);

        clock.UtcNowValue = Noon.AddHours(11);
        service.GetTopGenres(active, 5);
        Assert.Equal(2, counters.Computations);

        clock.UtcNowValue = Noon.AddHours(13);
        service.GetTopGenres(active, 5);

        Assert.Equal(3, counters.Computations);
        Assert.False(service.HasSnapshotForTesting(inactive));
    }

    [Fact]
    public void GetSnapshot_EmptyHistory_ExpiresAfterSessionTtl()
    {
        var clock = new ManualTimeProvider { UtcNowValue = Noon };
        var user = new User("test", "Default", "Default");
        var computations = 0;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
            {
                if (q.IncludeItemTypes.Contains(BaseItemKind.Movie) && q.IsFavorite != true)
                {
                    Interlocked.Increment(ref computations);
                }

                return [];
            });
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);
        var service = new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration(),
            timeProvider: clock);

        service.GetTopGenres(user.Id, 5);
        clock.UtcNowValue = Noon.AddMinutes(14);
        service.GetTopGenres(user.Id, 5);
        Assert.Equal(1, computations);

        clock.UtcNowValue = Noon.AddMinutes(16);
        service.GetTopGenres(user.Id, 5);
        Thread.Sleep(200);
        Assert.Equal(2, computations);
    }

    // -------------------------------------------------------------------------
    // GetTopGenres
    // -------------------------------------------------------------------------

    [Fact]
    public void GetTopGenres_TalliesAcrossWatchedItems_MostFrequentFirst()
    {
        var user = new User("test", "Default", "Default");

        var action1 = new Movie { Name = "Action 1", Genres = ["Action"] };
        var action2 = new Movie { Name = "Action 2", Genres = ["Action"] };
        var drama1 = new Movie { Name = "Drama 1", Genres = ["Drama"] };

        var service = BuildService(
            user,
            watched: [action1, action2, drama1],
            favorites: [],
            people: _ => []);

        var result = service.GetTopGenres(user.Id, 5);

        Assert.Equal("Action", result[0].Value);
        Assert.Equal("Drama", result[1].Value);
    }

    [Fact]
    public void GetTopGenres_FavoriteOutranksNonFavoriteWithFewerWatches()
    {
        var user = new User("test", "Default", "Default");

        // Drama watched once but favorited should outrank Comedy watched twice, unfavored.
        // Distinct Ids are required: BaseItem.Id defaults to Guid.Empty, which would make every
        // item match a favorite-id set built from any single unfavored item.
        var dramaFavorite = new Movie { Id = Guid.NewGuid(), Name = "Drama Fav", Genres = ["Drama"] };
        var comedy1 = new Movie { Id = Guid.NewGuid(), Name = "Comedy 1", Genres = ["Comedy"] };
        var comedy2 = new Movie { Id = Guid.NewGuid(), Name = "Comedy 2", Genres = ["Comedy"] };

        var service = BuildService(
            user,
            watched: [dramaFavorite, comedy1, comedy2],
            favorites: [dramaFavorite],
            people: _ => []);

        var result = service.GetTopGenres(user.Id, 5);

        Assert.Equal("Drama", result[0].Value);
    }

    [Fact]
    public void GetTopGenres_NoWatchHistory_ReturnsEmpty()
    {
        var user = new User("test", "Default", "Default");
        var service = BuildService(user, watched: [], favorites: [], people: _ => []);

        var result = service.GetTopGenres(user.Id, 5);

        Assert.Empty(result);
    }

    [Fact]
    public void GetTopGenres_SeriesContributeToTallyAlongsideMovies()
    {
        var user = new User("test", "Default", "Default");

        var action1 = new Movie { Name = "Action Movie", Genres = ["Action"] };
        var dramaSeries1 = new Series { Name = "Drama Series 1", Genres = ["Drama"] };
        var dramaSeries2 = new Series { Name = "Drama Series 2", Genres = ["Drama"] };

        var service = BuildService(
            user,
            watched: [action1],
            favorites: [],
            people: _ => [],
            watchedSeries: [dramaSeries1, dramaSeries2]);

        var result = service.GetTopGenres(user.Id, 5);

        Assert.Equal("Drama", result[0].Value);
    }

    [Fact]
    public void GetTopGenres_UnknownUser_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var service = new ScoringService(
            userManagerMock.Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration());

        var result = service.GetTopGenres(Guid.NewGuid(), 5);

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // GetTopActors / GetTopDirectors
    // -------------------------------------------------------------------------

    [Fact]
    public void GetTopActors_TalliesActorsAcrossWatchedItems()
    {
        var user = new User("test", "Default", "Default");
        var film1 = new Movie { Name = "Film 1", Genres = [] };
        var film2 = new Movie { Name = "Film 2", Genres = [] };

        var service = BuildService(
            user,
            watched: [film1, film2],
            favorites: [],
            people: item => item == film1
                ? [Actor("Brad Pitt"), Director("Someone")]
                : [Actor("Brad Pitt")]);

        var result = service.GetTopActors(user.Id, 5);

        Assert.Equal("Brad Pitt", result[0].Value);
    }

    [Fact]
    public void GetTopDirectors_ExcludesActorsFromDirectorTally()
    {
        var user = new User("test", "Default", "Default");
        var film1 = new Movie { Name = "Film 1", Genres = [] };

        var service = BuildService(
            user,
            watched: [film1],
            favorites: [],
            people: _ => [Actor("Brad Pitt"), Director("Christopher Nolan")]);

        var result = service.GetTopDirectors(user.Id, 5);

        var single = Assert.Single(result);
        Assert.Equal("Christopher Nolan", single.Value);
    }

    // -------------------------------------------------------------------------
    // GetRecentlyWatched
    // -------------------------------------------------------------------------

    [Fact]
    public void GetRecentlyWatched_ReturnsBothWatchedItems_RegardlessOfOrder()
    {
        // TODO_V3.md Phase 9.2: candidate order is now randomized (see
        // GetRecentlyWatched_IsStableWithinCachePeriod / _OnlyPicksFromTopPoolSize below for the
        // behavior this replaces asserting on a fixed position), so this only checks membership.
        var user = new User("test", "Default", "Default");
        var recent = new Movie { Name = "Recent", Genres = [] };
        var older = new Movie { Name = "Older", Genres = [] };

        var service = BuildService(user, watched: [recent, older], favorites: [], people: _ => []);

        var result = service.GetRecentlyWatched(user.Id, 5);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Label == "Recent" && r.Value == recent.Id.ToString());
        Assert.Contains(result, r => r.Label == "Older" && r.Value == older.Id.ToString());
    }

    [Fact]
    public void GetRecentlyWatched_IsStableWithinCachePeriod()
    {
        // Two calls against the same ScoringService instance hit the same cached ScoreSnapshot, so
        // the seeded shuffle must produce the exact same order both times -- content should only
        // vary at the day boundary or after the user finishes a title.
        var user = new User("test", "Default", "Default");
        var items = Enumerable.Range(0, 8)
            .Select(i => new Movie { Id = Guid.NewGuid(), Name = $"Film {i}", Genres = [] })
            .Cast<BaseItem>()
            .ToList();

        var service = BuildService(user, watched: items, favorites: [], people: _ => []);

        var first = service.GetRecentlyWatched(user.Id, 8);
        var second = service.GetRecentlyWatched(user.Id, 8);

        Assert.Equal(first.Select(r => r.Value), second.Select(r => r.Value));
    }

    [Fact]
    public void GetRecentlyWatched_OnlyPicksFromTopPoolSize()
    {
        // With more entries than the fixed candidate pool size (10), only the most recently watched
        // 10 may ever be returned -- the 11th-oldest+ entries must never appear regardless of limit.
        var user = new User("test", "Default", "Default");
        var items = Enumerable.Range(0, 15)
            .Select(i => new Movie { Id = Guid.NewGuid(), Name = $"Film {i}", Genres = [] })
            .Cast<BaseItem>()
            .ToList();

        var service = BuildService(user, watched: items, favorites: [], people: _ => []);

        var result = service.GetRecentlyWatched(user.Id, 10);

        var poolNames = items.Take(10).Select(i => i.Name).ToHashSet();
        Assert.Equal(10, result.Count);
        Assert.All(result, r => Assert.Contains(r.Label, poolNames));
    }

    [Fact]
    public void GetRecentlyWatched_RespectsLimit()
    {
        var user = new User("test", "Default", "Default");
        var items = Enumerable.Range(0, 5)
            .Select(i => new Movie { Name = $"Film {i}", Genres = [] })
            .Cast<BaseItem>()
            .ToList();

        var service = BuildService(user, watched: items, favorites: [], people: _ => []);

        var result = service.GetRecentlyWatched(user.Id, 2);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetRecentlyWatched_ScopeMovies_ExcludesSeries()
    {
        var user = new User("test", "Default", "Default");
        var movie = new Movie { Name = "A Movie", Genres = [] };
        var series = new Series { Name = "A Series", Genres = [] };

        var service = BuildService(
            user,
            watched: [movie],
            favorites: [],
            people: _ => [],
            watchedSeries: [series]);

        var result = service.GetRecentlyWatched(user.Id, 5, BecauseYouWatchedScope.Movies);

        var single = Assert.Single(result);
        Assert.Equal("A Movie", single.Label);
    }

    [Fact]
    public void GetRecentlyWatched_ScopeSeries_ExcludesMovies()
    {
        var user = new User("test", "Default", "Default");
        var movie = new Movie { Name = "A Movie", Genres = [] };
        var series = new Series { Name = "A Series", Genres = [] };

        var service = BuildService(
            user,
            watched: [movie],
            favorites: [],
            people: _ => [],
            watchedSeries: [series]);

        var result = service.GetRecentlyWatched(user.Id, 5, BecauseYouWatchedScope.Series);

        var single = Assert.Single(result);
        Assert.Equal("A Series", single.Label);
    }

    [Fact]
    public void GetRecentlyWatched_SeriesDatedByLatestEpisode_EntersPoolAfterTenMovies()
    {
        // F-67: ten movies watched, then one episode. The series must be dated by that episode and
        // rank first; dated as MinValue it would be the 11th entry, outside the 10-entry pool.
        var user = new User("test", "Default", "Default");
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var movies = Enumerable.Range(0, 10)
            .Select(i => (BaseItem)new Movie { Id = Guid.NewGuid(), Name = $"Film {i}", Genres = [] })
            .ToList();
        var series = new Series { Id = Guid.NewGuid(), Name = "A Series", Genres = [] };
        var lastPlayed = movies
            .Select((m, i) => (m.Id, Date: start.AddDays(i)))
            .Append((series.Id, start.AddDays(10)))
            .ToDictionary(p => p.Item1, p => p.Item2);

        var service = BuildService(
            user,
            watched: movies,
            favorites: [],
            people: _ => [],
            watchedSeries: [series],
            lastPlayed: lastPlayed);

        var result = service.GetRecentlyWatched(user.Id, 5, BecauseYouWatchedScope.Series);

        var single = Assert.Single(result);
        Assert.Equal(series.Id.ToString(), single.Value);
    }

    [Fact]
    public void GetRecentlyWatched_ScopeBoth_IncludesMoviesAndSeries()
    {
        var user = new User("test", "Default", "Default");
        var movie = new Movie { Name = "A Movie", Genres = [] };
        var series = new Series { Name = "A Series", Genres = [] };

        var service = BuildService(
            user,
            watched: [movie],
            favorites: [],
            people: _ => [],
            watchedSeries: [series]);

        var result = service.GetRecentlyWatched(user.Id, 5, BecauseYouWatchedScope.Both);

        Assert.Equal(2, result.Count);
    }

    private sealed class ComputeCounters
    {
        public int Computations;
        public int Running;
        public int PeakRunning;
    }
}
