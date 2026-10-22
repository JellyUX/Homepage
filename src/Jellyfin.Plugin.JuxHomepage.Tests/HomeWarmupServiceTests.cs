using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// PERF-07, DEC-P07-3 and -10: a while after the server started, the home screens of the recently
/// active users are built ahead, scoring first, in the language of their last home screen.
/// </summary>
public sealed class HomeWarmupServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _clock = new() { UtcNowValue = Now };
    private readonly SessionCache _sessionCache = new();
    private readonly List<User> _users = [];
    private readonly Dictionary<Guid, UserConfiguration> _settings = [];
    private readonly ScoringService _scoring;
    private readonly FakeLifetime _lifetime = new();
    private readonly Dictionary<Guid, int> _settingsReads = [];
    private Guid _languageReadCancelled;

    public HomeWarmupServiceTests()
    {
        var userManager = new Mock<IUserManager>();
        userManager.Setup(m => m.GetUsers()).Returns(() => _users);
        userManager.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((Guid id) => _users.FirstOrDefault(u => u.Id == id));
        var film = new Movie { Id = Guid.NewGuid(), Name = "Film", Genres = ["Drama"] };
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie) ? [film] : []);
        library.Setup(m => m.GetPeople(It.IsAny<BaseItem>())).Returns([]);
        _scoring = new ScoringService(userManager.Object, library.Object, new Mock<IUserDataManager>().Object, () => new PluginConfiguration());
        UserManager = userManager.Object;
    }

    private IUserManager UserManager { get; }

    public void Dispose()
    {
        _scoring.Dispose();
        _sessionCache.Dispose();
    }

    [Fact]
    public async Task Warmup_AfterTheDelay_BuildsRecentUsersHomeInTheirLanguage_AndLeavesOthers()
    {
        var recent = AddUser(TimeSpan.FromDays(1), homeLanguage: "fr");
        var noLanguage = AddUser(TimeSpan.FromHours(2));
        var old = AddUser(TimeSpan.FromDays(8));
        var off = AddUser(TimeSpan.FromHours(1));
        _settings[off.Id].Enabled = false;
        using var warmup = Build();
        await warmup.StartAsync(CancellationToken.None);
        _lifetime.Start();

        _clock.Advance(warmup.Delay - TimeSpan.FromSeconds(1));
        Assert.False(_scoring.HasSnapshotForTesting(recent.Id));

        _clock.Advance(TimeSpan.FromSeconds(1));
        await warmup.RunningForTesting!;

        Assert.True(Cached(recent, "fr"));
        Assert.True(Cached(noLanguage, "de"));
        Assert.True(_scoring.HasSnapshotForTesting(recent.Id));
        Assert.False(_scoring.HasSnapshotForTesting(old.Id));
        Assert.False(Cached(old, "de"));
        Assert.False(_scoring.HasSnapshotForTesting(off.Id));
    }

    [Fact]
    public async Task Warmup_TakesTheTenMostRecentlyActiveUsers()
    {
        var users = Enumerable.Range(1, 12).Select(hours => AddUser(TimeSpan.FromHours(hours))).ToList();
        using var warmup = Build();
        await warmup.StartAsync(CancellationToken.None);
        _lifetime.Start();

        _clock.Advance(warmup.Delay);
        await warmup.RunningForTesting!;

        Assert.All(users.Take(HomeWarmupService.MaxUsers), u => Assert.True(Cached(u, "de")));
        Assert.All(users.Skip(HomeWarmupService.MaxUsers), u => Assert.False(Cached(u, "de")));
    }

    [Fact]
    public async Task Warmup_OneUserCancelledWhileTheServerRuns_StillBuildsTheOthers()
    {
        // R11 review (DEC-P07-17): only the server stopping ends the warmup.
        var first = AddUser(TimeSpan.FromHours(1));
        var second = AddUser(TimeSpan.FromHours(2));
        _languageReadCancelled = first.Id;
        using var warmup = Build();
        await warmup.StartAsync(CancellationToken.None);
        _lifetime.Start();

        _clock.Advance(warmup.Delay);
        await warmup.RunningForTesting!;

        Assert.False(Cached(first, "de"));
        Assert.True(Cached(second, "de"));
    }

    [Fact]
    public async Task Warmup_ServerStoppingBeforeTheDelay_BuildsNothing()
    {
        var user = AddUser(TimeSpan.FromHours(1));
        using var warmup = Build();
        await warmup.StartAsync(CancellationToken.None);
        _lifetime.Start();

        await warmup.StopAsync(CancellationToken.None);
        _clock.Advance(warmup.Delay);
        await warmup.RunningForTesting!;

        Assert.False(_scoring.HasSnapshotForTesting(user.Id));
        Assert.False(Cached(user, "de"));
    }

    [Theory]
    [InlineData(1, 90)]
    [InlineData(2, 90)]
    [InlineData(4, 30)]
    public void Delay_IsLongerOnAMachineOfTwoProcessorsOrFewer(int processors, int seconds)
    {
        using var warmup = Build(processors);

        Assert.Equal(TimeSpan.FromSeconds(seconds), warmup.Delay);
    }

    private User AddUser(TimeSpan lastActiveAgo, string? homeLanguage = null)
    {
        var user = new User("user" + _users.Count, "Default", "Default") { LastActivityDate = Now.UtcDateTime - lastActiveAgo };
        _users.Add(user);
        _settings[user.Id] = new UserConfiguration { UserId = user.Id, HomeLanguage = homeLanguage };
        return user;
    }

    private bool Cached(User user, string language) => _sessionCache.TryGet(user.Id, language, TimeSpan.FromMinutes(15), out _, out _);

    private HomeWarmupService Build(int processors = 8)
    {
        var store = new Mock<IUserConfigurationStore>();
        store.Setup(s => s.GetUserConfiguration(It.IsAny<Guid>())).Returns((Guid id) =>
        {
            // The second read of a user's settings is the language read of the warmup.
            _settingsReads[id] = _settingsReads.GetValueOrDefault(id) + 1;
            if (id == _languageReadCancelled && _settingsReads[id] == 2)
            {
                throw new OperationCanceledException("A cancellation unrelated to the server stopping.");
            }

            return _settings.GetValueOrDefault(id);
        });
        var localization = new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>(),
            ["fr"] = new Dictionary<string, string>(),
            ["de"] = new Dictionary<string, string>()
        });
        var registry = new WidgetRegistry();
        var resolver = new WidgetLayoutResolver(registry, store.Object, localization, () => new PluginConfiguration(), NullLogger<WidgetLayoutResolver>.Instance);
        var widgets = new WidgetService(registry, _sessionCache, resolver, () => new PluginConfiguration(), NullLogger<WidgetService>.Instance, _scoring);
        var server = new Mock<IServerConfigurationManager>();
        server.Setup(s => s.Configuration).Returns(new MediaBrowser.Model.Configuration.ServerConfiguration { UICulture = "de-DE" });

        return new HomeWarmupService(
            _lifetime,
            UserManager,
            _scoring,
            widgets,
            new HomeLanguageMemory(store.Object),
            server.Object,
            localization,
            _clock,
            NullLogger<HomeWarmupService>.Instance,
            processors);
    }
}
