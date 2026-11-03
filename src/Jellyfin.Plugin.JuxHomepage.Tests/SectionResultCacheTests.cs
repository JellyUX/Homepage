using Jellyfin.Data.Events;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// PERF-03.b, PERF-06.a: one page per section and user, served within the layout TTL and never after
/// the user's data, the library, the configuration or the user's settings changed (DEC-P07-2).
/// </summary>
public sealed class SectionResultCacheTests : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly ManualTimeProvider _clock = new();
    private readonly SessionCache _sessionCache;
    private readonly SectionResultCache _cache;
    private readonly Guid _user = Guid.NewGuid();

    public SectionResultCacheTests()
    {
        _sessionCache = new SessionCache(_clock);
        _cache = new SectionResultCache(_sessionCache, _clock);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _sessionCache.Dispose();
    }

    [Fact]
    public void StoredPage_IsServedForTheSamePageOnly()
    {
        var id = Section(_user);
        var page = Page();
        Assert.True(_cache.Set(id, 0, 20, page, _cache.StampOf(_user), Ttl));

        Assert.True(_cache.TryGet(id, 0, 20, out var hit, out _));
        Assert.Same(page, hit);
        Assert.False(_cache.TryGet(id, 20, 20, out _, out _));
        Assert.False(_cache.TryGet(id, 0, 10, out _, out _));
        Assert.False(_cache.TryGet(Section(Guid.NewGuid()), 0, 20, out _, out _));
    }

    [Fact]
    public void AnotherPage_ReplacesTheStoredOne_OnePagePerSection()
    {
        var id = Section(_user);
        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _cache.Set(id, 20, 20, Page(), _cache.StampOf(_user), Ttl);

        Assert.Equal(1, _cache.CountForTesting());
        Assert.False(_cache.TryGet(id, 0, 20, out _, out _));
        Assert.True(_cache.TryGet(id, 20, 20, out _, out _));
    }

    [Fact]
    public void EmptyAdditionalDataAndSectionKey_AreTheSameAsNone()
    {
        _cache.Set(new SectionId(_user, "w", string.Empty, string.Empty), 0, 20, Page(), _cache.StampOf(_user), Ttl);

        Assert.True(_cache.TryGet(new SectionId(_user, "w", null, null), 0, 20, out _, out _));
    }

    [Fact]
    public void StoredPage_IsNotServedOnceTheLayoutTtlEnds()
    {
        var id = Section(_user);
        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);

        _clock.UtcNowValue += Ttl - TimeSpan.FromSeconds(1);
        Assert.True(_cache.TryGet(id, 0, 20, out _, out _));
        _clock.UtcNowValue += TimeSpan.FromSeconds(1);
        Assert.False(_cache.TryGet(id, 0, 20, out _, out _));
    }

    [Fact]
    public void StoredPage_IsNotServedAfterAConfigurationChangeOrTheUsersSettingsChange()
    {
        var id = Section(_user);
        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _sessionCache.Clear();
        Assert.False(_cache.TryGet(id, 0, 20, out _, out _));

        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _sessionCache.Invalidate(_user);
        Assert.False(_cache.TryGet(id, 0, 20, out _, out _));
    }

    [Fact]
    public void ClearSources_DropsEveryUsersPagesOfThoseSources_AndKeepsTheOthers()
    {
        var other = Guid.NewGuid();
        var movies = new SectionId(_user, "jux.test.movies", null, null);
        var shows = new SectionId(other, "jux.test.shows", null, null);
        var anything = new SectionId(other, "jux.test.anything", null, null);
        _cache.Set(movies, 0, 20, Page(), _cache.StampOf(_user), Ttl, [SectionSources.Movies]);
        _cache.Set(shows, 0, 20, Page(), _cache.StampOf(other), Ttl, [SectionSources.Series]);
        _cache.Set(anything, 0, 20, Page(), _cache.StampOf(other), Ttl);

        _cache.ClearSources([SectionSources.Movies]);

        Assert.False(_cache.TryGet(movies, 0, 20, out _, out _));
        Assert.False(_cache.TryGet(anything, 0, 20, out _, out _));
        Assert.True(_cache.TryGet(shows, 0, 20, out _, out _));
    }

    [Fact]
    public void Section_InvalidatedWhileRead_IsNotStored()
    {
        // A page computed before the library or the layout changed reflects the old state.
        var stamp = _cache.StampOf(_user);
        _cache.ClearSources([SectionSources.Series]);
        Assert.False(_cache.Set(Section(_user), 0, 20, Page(), stamp, Ttl, [SectionSources.Movies]));

        stamp = _cache.StampOf(_user);
        _sessionCache.Clear();
        Assert.False(_cache.Set(Section(_user), 0, 20, Page(), stamp, Ttl));
        Assert.Equal(0, _cache.CountForTesting());
    }

    [Fact]
    public void Remove_DropsTheFailedPage_NotOneStoredSince()
    {
        var id = Section(_user);
        var failed = Page();
        _cache.Set(id, 0, 20, failed, _cache.StampOf(_user), Ttl);
        var stored = Page();
        _cache.Set(id, 0, 20, stored, _cache.StampOf(_user), Ttl);

        _cache.Remove(id, failed);
        Assert.True(_cache.TryGet(id, 0, 20, out var hit, out _));
        Assert.Same(stored, hit);

        _cache.Remove(id, stored);
        Assert.False(_cache.TryGet(id, 0, 20, out _, out _));
    }

    [Fact]
    public void Cleanup_RemovesExpiredAndOutdatedPages()
    {
        var other = Guid.NewGuid();
        _cache.Set(Section(_user), 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), TimeSpan.FromMinutes(1));
        _cache.Set(Section(Guid.NewGuid()), 0, 20, Page(), _cache.StampOf(Guid.Empty), Ttl);
        _sessionCache.Invalidate(_user);
        _clock.UtcNowValue += TimeSpan.FromMinutes(2);

        _cache.RunCleanupForTesting();

        Assert.Equal(1, _cache.CountForTesting());
    }

    [Fact]
    public async Task Listener_LibraryChange_DropsThePagesOfTheChangedKindOnly_OnceTheLibraryIsQuiet()
    {
        var library = new Mock<ILibraryManager>();
        using var listener = Listener(library, new Mock<ITaskManager>());
        var other = Guid.NewGuid();
        await listener.StartAsync(CancellationToken.None);

        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), Ttl, [SectionSources.Series]);
        Added(library, new Movie());
        Added(library, new Person());
        _clock.Advance(LibraryChangeBatcher.QuietDelay);
        Assert.True(_cache.TryGet(Section(other), 0, 20, out _, out _));

        Added(library, new Episode());
        Assert.True(_cache.TryGet(Section(other), 0, 20, out _, out _));
        _clock.Advance(LibraryChangeBatcher.QuietDelay);
        Assert.False(_cache.TryGet(Section(other), 0, 20, out _, out _));

        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), Ttl);
        library.Raise(m => m.ItemRemoved += null, library.Object, new ItemChangeEventArgs { Item = new Movie() });
        _clock.Advance(LibraryChangeBatcher.QuietDelay);
        Assert.False(_cache.TryGet(Section(other), 0, 20, out _, out _));

        await listener.StopAsync(CancellationToken.None);
        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), Ttl);
        Added(library, new Movie());
        _clock.Advance(LibraryChangeBatcher.QuietDelay);
        Assert.True(_cache.TryGet(Section(other), 0, 20, out _, out _));
    }

    [Fact]
    public async Task Listener_LibraryScan_KeepsEveryPageUntilTheScanEnds_ThenDropsTheChangedKindsOnce()
    {
        var library = new Mock<ILibraryManager>();
        var tasks = new Mock<ITaskManager>();
        using var listener = Listener(library, tasks);
        await listener.StartAsync(CancellationToken.None);
        var scan = Worker(SectionCacheInvalidationListener.LibraryScanTaskKey);
        var movies = new SectionId(_user, "jux.test.movies", null, null);
        var shows = new SectionId(_user, "jux.test.shows", null, null);
        _cache.Set(movies, 0, 20, Page(), _cache.StampOf(_user), Ttl, [SectionSources.Movies]);
        _cache.Set(shows, 0, 20, Page(), _cache.StampOf(_user), Ttl, [SectionSources.Series]);

        tasks.Raise(m => m.TaskExecuting += null, tasks.Object, new GenericEventArgs<IScheduledTaskWorker>(scan));
        for (var i = 0; i < 500; i++)
        {
            Added(library, new Movie());
        }

        _clock.Advance(LibraryChangeBatcher.MaxDelay);
        Assert.True(_cache.TryGet(movies, 0, 20, out _, out _));

        tasks.Raise(m => m.TaskCompleted += null, tasks.Object, new TaskCompletionEventArgs(scan, new TaskResult()));
        Assert.False(_cache.TryGet(movies, 0, 20, out _, out _));
        Assert.True(_cache.TryGet(shows, 0, 20, out _, out _));
    }

    private static IScheduledTaskWorker Worker(string key)
    {
        var task = new Mock<IScheduledTask>();
        task.SetupGet(t => t.Key).Returns(key);
        var worker = new Mock<IScheduledTaskWorker>();
        worker.SetupGet(w => w.ScheduledTask).Returns(task.Object);
        return worker.Object;
    }

    private static void Added(Mock<ILibraryManager> library, BaseItem item) =>
        library.Raise(m => m.ItemAdded += null, library.Object, new ItemChangeEventArgs { Item = item });

    private SectionCacheInvalidationListener Listener(Mock<ILibraryManager> library, Mock<ITaskManager> tasks) => new(
        new Mock<IUserDataManager>().Object,
        library.Object,
        _cache,
        new WidgetRegistry(),
        tasks.Object,
        new ScoringService(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IUserDataManager>(), () => null),
        _clock,
        NullLogger<SectionCacheInvalidationListener>.Instance);

    private static SectionId Section(Guid user) => new(user, "jux.test.section", "value", "jux.test.section#1");

    private static WidgetResult Page() => new([new BaseItemDto()], 1);
}
