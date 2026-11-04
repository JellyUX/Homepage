using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using Jellyfin.Plugin.JuxHomepage.Widgets.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// S2 (DR-033): every night, at 04:00 by default, the home screens of the users active in the last 7 days are computed
/// ahead, scoring first, and their deferred rows stored, in the language of their last home screen.
/// </summary>
public sealed class HomeSectionsRefreshTaskTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 4, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _clock = new() { UtcNowValue = Now };
    private readonly SessionCache _sessionCache = new();
    private readonly InMemoryFileSystem _disk = new();
    private readonly List<User> _users = [];
    private readonly Dictionary<Guid, UserConfiguration> _settings = [];
    private readonly HashSet<Guid> _failingLanguage = [];
    private Action _onScoring = () => { };
    private readonly ScoringService _scoring;
    private readonly StoredHomeScreens _stored;

    public HomeSectionsRefreshTaskTests()
    {
        var userManager = new Mock<IUserManager>();
        userManager.Setup(m => m.GetUsers()).Returns(() => _users);
        userManager.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((Guid id) => _users.FirstOrDefault(u => u.Id == id));
        var film = new Movie { Id = Guid.NewGuid(), Name = "Film", Genres = ["Drama"] };
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
            {
                _onScoring();
                return q.IsPlayed == true ? [film.Id] : [];
            });
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie) ? [film] : []);
        library.Setup(m => m.GetPeople(It.IsAny<BaseItem>())).Returns([]);
        _scoring = new ScoringService(userManager.Object, library.Object, new Mock<IUserDataManager>().Object, () => new PluginConfiguration());
        _stored = TestWidgetServices.NewStore(_disk, _clock);
        UserManager = userManager.Object;
    }

    private IUserManager UserManager { get; }

    public void Dispose()
    {
        _scoring.Dispose();
        _sessionCache.Dispose();
    }

    [Fact]
    public void DefaultTrigger_IsDailyAtFour()
    {
        var trigger = Assert.Single(Build().GetDefaultTriggers());

        Assert.Equal((TaskTriggerInfoType.DailyTrigger, TimeSpan.FromHours(4).Ticks), (trigger.Type, trigger.TimeOfDayTicks));
    }

    [Fact]
    public async Task Execute_ComputesAndStoresTheRecentUsersHomeInTheirLanguage_AndLeavesTheOthers()
    {
        var recent = AddUser(TimeSpan.FromDays(1), homeLanguage: "fr");
        var noLanguage = AddUser(TimeSpan.FromHours(2));
        var old = AddUser(TimeSpan.FromDays(8));
        var off = AddUser(TimeSpan.FromHours(1));
        _settings[off.Id].Enabled = false;
        var progress = new List<double>();

        await Build().ExecuteAsync(new SyncProgress(progress), CancellationToken.None);

        Assert.True(Stored(recent, "fr"));
        Assert.True(Stored(noLanguage, "de"));
        Assert.True(_scoring.HasSnapshotForTesting(recent.Id));
        Assert.False(Stored(old, "de"));
        Assert.False(_scoring.HasSnapshotForTesting(old.Id));
        Assert.False(Stored(off, "de"));
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public async Task Execute_AUserThatFails_IsLogged_AndTheOthersAreStillComputed()
    {
        var failing = AddUser(TimeSpan.FromHours(1));
        var other = AddUser(TimeSpan.FromHours(2));
        _failingLanguage.Add(failing.Id);

        await Build().ExecuteAsync(new SyncProgress([]), CancellationToken.None);

        Assert.False(Stored(failing, "de"));
        Assert.True(Stored(other, "de"));
    }

    // R11: past the memory bound of the warmup, a user's home screen is stored only, not kept in memory.
    [Fact]
    public async Task Execute_KeepsInMemoryOnlyTheUsersTheMemoryBoundAllows()
    {
        var first = AddUser(TimeSpan.FromHours(1));
        var second = AddUser(TimeSpan.FromHours(2));

        await Build(memory: 64L * 1024 * 1024).ExecuteAsync(new SyncProgress([]), CancellationToken.None);

        Assert.True(Stored(first, "de") && Stored(second, "de"));
        Assert.True(_sessionCache.TryGet(first.Id, "de", TimeSpan.FromMinutes(15), out _, out _));
        Assert.False(_sessionCache.TryGet(second.Id, "de", TimeSpan.FromMinutes(15), out _, out _));
    }

    // Phase 14: between two users the task waits as long as the last one took, so it never holds the server more than
    // half the time (p95 /Items/Latest 30 -> 274 ms during an unpaced task).
    [Fact]
    public async Task Execute_WaitsBetweenTwoUsers_AsLongAsTheLastOneTook()
    {
        var first = AddUser(TimeSpan.FromHours(1));
        var second = AddUser(TimeSpan.FromHours(2));
        var scored = 0;
        _onScoring = () =>
        {
            if (Interlocked.Exchange(ref scored, 1) == 0)
            {
                _clock.UtcNowValue += TimeSpan.FromSeconds(2);
            }
        };

        var run = Build().ExecuteAsync(new SyncProgress([]), CancellationToken.None);
        var waited = SpinWait.SpinUntil(() => _clock.ActiveTimers == 1 || run.IsCompleted, TimeSpan.FromSeconds(10));
        _clock.Advance(TimeSpan.FromSeconds(1));
        var stillWaiting = !run.IsCompleted && !Stored(second, "de");
        _clock.Advance(TimeSpan.FromSeconds(1));
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(waited && stillWaiting, "The task did not wait between the two users.");
        Assert.True(Stored(first, "de") && Stored(second, "de"));
    }

    [Fact]
    public async Task Execute_Cancelled_StopsBeforeTheNextUser()
    {
        var user = AddUser(TimeSpan.FromHours(1));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Build().ExecuteAsync(new SyncProgress([]), cancelled.Token));

        Assert.False(Stored(user, "de"));
    }

    private User AddUser(TimeSpan lastActiveAgo, string? homeLanguage = null)
    {
        var user = new User("user" + _users.Count, "Default", "Default") { LastActivityDate = Now.UtcDateTime - lastActiveAgo };
        _users.Add(user);
        _settings[user.Id] = new UserConfiguration { UserId = user.Id, HomeLanguage = homeLanguage };
        return user;
    }

    private bool Stored(User user, string language) =>
        _stored.Read(user.Id, language, StoredHomeScreens.Discriminant(new PluginConfiguration(), _settings.GetValueOrDefault(user.Id)), TimeSpan.FromDays(1)) is { Fresh: true };

    private HomeSectionsRefreshTask Build(long memory = 16L * 1024 * 1024 * 1024)
    {
        var store = new Mock<IUserConfigurationStore>();
        store.Setup(s => s.GetUserConfiguration(It.IsAny<Guid>())).Returns((Guid id) => _settings.GetValueOrDefault(id));
        var languages = new Mock<IUserConfigurationStore>();
        languages.Setup(s => s.GetUserConfiguration(It.IsAny<Guid>())).Returns((Guid id) =>
            _failingLanguage.Contains(id) ? throw new InvalidOperationException("unreadable settings") : _settings.GetValueOrDefault(id));
        var localization = new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>(),
            ["fr"] = new Dictionary<string, string>(),
            ["de"] = new Dictionary<string, string>()
        });
        var registry = new WidgetRegistry();
        var resolver = new WidgetLayoutResolver(registry, store.Object, localization, () => new PluginConfiguration(), NullLogger<WidgetLayoutResolver>.Instance);
        var widgets = TestWidgetServices.Create(registry, _sessionCache, resolver, () => new PluginConfiguration(), _scoring, stored: _stored);
        var server = new Mock<IServerConfigurationManager>();
        server.Setup(s => s.Configuration).Returns(new MediaBrowser.Model.Configuration.ServerConfiguration { UICulture = "de-DE" });

        return new HomeSectionsRefreshTask(
            new ActiveHomeUsers(UserManager, new HomeLanguageMemory(languages.Object), server.Object, localization, _clock, memory),
            _scoring,
            widgets,
            _clock,
            NullLogger<HomeSectionsRefreshTask>.Instance);
    }

    private sealed class SyncProgress(List<double> reports) : IProgress<double>
    {
        public void Report(double value) => reports.Add(value);
    }
}
