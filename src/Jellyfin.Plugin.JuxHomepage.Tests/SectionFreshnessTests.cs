using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// REV-04.b (DR-032): a playback progress report keeps the user's section pages; a playback end, a played toggle or
/// a rating drops the user's direct pages and marks the deferred ones stale, served while they are read again in
/// the background.
/// </summary>
public sealed class SectionFreshnessTests : IDisposable
{
    private const string DirectType = "jux.native.watchlist";
    private const string DeferredType = "jux.admin.genre";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly ManualTimeProvider _clock = new();
    private readonly SessionCache _sessionCache;
    private readonly SectionResultCache _cache;
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly WidgetRegistry _registry = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly ScoringService _scoring = NewScoring();
    private int _deferredReads;

    public SectionFreshnessTests()
    {
        _sessionCache = new SessionCache(_clock);
        _cache = new SectionResultCache(_sessionCache, _clock);
        _registry.Register(Widget(DirectType, FreshnessLevel.Direct));
        _registry.Register(Widget(DeferredType, FreshnessLevel.Deferred));
    }

    public void Dispose()
    {
        _scoring.Dispose();
        _cache.Dispose();
        _sessionCache.Dispose();
    }

    [Theory]
    [InlineData(UserDataSaveReason.PlaybackProgress)]
    [InlineData(UserDataSaveReason.PlaybackStart)]
    [InlineData(UserDataSaveReason.Import)]
    [InlineData(UserDataSaveReason.UpdateUserData)]
    public async Task PlaybackStartProgressOrImport_KeepsTheUsersPagesFresh(UserDataSaveReason reason)
    {
        await StartListener();
        Store(_user, DirectType);
        Store(_user, DeferredType);

        Save(_user, reason);

        Assert.True(_cache.TryGet(Section(_user, DirectType), 0, 20, out _, out var directStale));
        Assert.True(_cache.TryGet(Section(_user, DeferredType), 0, 20, out _, out var deferredStale));
        Assert.False(directStale);
        Assert.False(deferredStale);
    }

    [Theory]
    [InlineData(UserDataSaveReason.PlaybackFinished)]
    [InlineData(UserDataSaveReason.TogglePlayed)]
    [InlineData(UserDataSaveReason.UpdateUserRating)]
    public async Task PlaybackEndToggleOrRating_DropsDirectPagesAndMarksDeferredPagesStale(UserDataSaveReason reason)
    {
        await StartListener();
        Store(_user, DirectType);
        Store(_user, DeferredType);
        Store(_other, DeferredType);

        Save(_user, reason);

        Assert.False(_cache.TryGet(Section(_user, DirectType), 0, 20, out _, out _));
        Assert.True(_cache.TryGet(Section(_user, DeferredType), 0, 20, out _, out var stale));
        Assert.True(stale);
        Assert.True(_cache.TryGet(Section(_other, DeferredType), 0, 20, out _, out var otherStale));
        Assert.False(otherStale);
    }

    [Fact]
    public async Task StalePage_IsServedAndReadAgainInTheBackground_ThenTheNewPageIsServed()
    {
        await StartListener();
        var service = Service();
        var first = await service.GetWidgetItems(_user, DeferredType, null, null, 0, 20, CancellationToken.None);
        Assert.Equal(1, _deferredReads);

        Save(_user, UserDataSaveReason.PlaybackFinished);
        var served = await service.GetWidgetItems(_user, DeferredType, null, null, 0, 20, CancellationToken.None);
        await service.WhenIdleForTesting();

        Assert.Same(first, served);
        Assert.Equal(2, _deferredReads);
        var next = await service.GetWidgetItems(_user, DeferredType, null, null, 0, 20, CancellationToken.None);
        Assert.NotSame(first, next);
        Assert.Equal(2, _deferredReads);
        Assert.True(_cache.TryGet(Section(_user, DeferredType), 0, 20, out _, out var stale));
        Assert.False(stale);
    }

    [Fact]
    public async Task DirectPage_StoredByAReadThatRacedAPlaybackEnd_IsReadAgainNotServedStale()
    {
        await StartListener();
        var service = Service();
        var directReads = 0;
        _registry.Register(Widget("jux.test.direct", FreshnessLevel.Direct, () => directReads++));
        var stamp = _cache.StampOf(_user);
        Save(_user, UserDataSaveReason.PlaybackFinished);
        Assert.True(_cache.Set(Section(_user, "jux.test.direct"), 0, 20, Page(), stamp, Ttl));

        await service.GetWidgetItems(_user, "jux.test.direct", null, null, 0, 20, CancellationToken.None);

        Assert.Equal(1, directReads);
    }

    [Fact]
    public async Task SectionRead_KeepsItsSources_SoAnotherSourceChangeKeepsItsPage()
    {
        var service = Service();
        var reads = 0;
        var widget = Widget("jux.test.series", FreshnessLevel.Deferred, () => reads++);
        Mock.Get(widget).SetupGet(w => w.Sources).Returns([SectionSources.Series]);
        _registry.Register(widget);

        await service.GetWidgetItems(_user, "jux.test.series", null, null, 0, 20, CancellationToken.None);
        _cache.ClearSources([SectionSources.Movies]);
        await service.GetWidgetItems(_user, "jux.test.series", null, null, 0, 20, CancellationToken.None);
        Assert.Equal(1, reads);

        _cache.ClearSources([SectionSources.Series]);
        await service.GetWidgetItems(_user, "jux.test.series", null, null, 0, 20, CancellationToken.None);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task StaleSectionReadsInTheBackground_DoNotHoldBackALayoutRebuild()
    {
        using var slowGate = new ManualResetEventSlim(false);
        var slowReads = 0;
        _registry.Register(Widget("jux.test.slow", FreshnessLevel.Deferred, () =>
        {
            if (Interlocked.Increment(ref slowReads) > 1)
            {
                slowGate.Wait(TimeSpan.FromSeconds(10));
            }
        }));
        var layoutReads = 0;
        var layout = Widget("jux.test.layout", FreshnessLevel.Deferred, () => Interlocked.Increment(ref layoutReads));
        Mock.Get(layout).Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>())).Returns(layout);
        _registry.Register(layout);
        var service = Service(new PluginConfiguration { Widgets = [new WidgetConfig { WidgetType = "jux.test.layout", Enabled = true, MinItems = 1 }] });
        await StartListener();
        var other = Guid.NewGuid();
        await service.GetWidgetsForUser(other, page: 0, lang: "en", CancellationToken.None);
        var readsBefore = layoutReads;

        await service.GetWidgetItems(_user, "jux.test.slow", null, null, 0, 20, CancellationToken.None);
        Save(_user, UserDataSaveReason.PlaybackFinished);
        await service.GetWidgetItems(_user, "jux.test.slow", null, null, 0, 20, CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => slowReads == 2, TimeSpan.FromSeconds(5)));
        _clock.UtcNowValue += TimeSpan.FromMinutes(16);
        await service.GetWidgetsForUser(other, page: 0, lang: "en", CancellationToken.None);

        var rebuilt = SpinWait.SpinUntil(() => layoutReads > readsBefore, TimeSpan.FromSeconds(3));
        slowGate.Set();
        await service.WhenIdleForTesting();
        Assert.True(rebuilt, "the layout rebuild waited behind a background section read");
    }

    [Fact]
    public async Task ANewScoringSnapshot_MarksTheUsersPagesStaleAgain()
    {
        await StartListener();
        Store(_user, DeferredType);
        Store(_other, DeferredType);

        _scoring.GetTopGenres(_user, 5);
        await _scoring.WhenIdleForTesting();

        Assert.True(_cache.TryGet(Section(_user, DeferredType), 0, 20, out _, out var stale));
        Assert.True(stale);
        Assert.True(_cache.TryGet(Section(_other, DeferredType), 0, 20, out _, out var otherStale));
        Assert.False(otherStale);
    }

    private static ScoringService NewScoring()
    {
        var users = new Mock<IUserManager>();
        users.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(new User("test", "Default", "Default"));
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([]);
        return new ScoringService(users.Object, library.Object, new Mock<IUserDataManager>().Object, () => new PluginConfiguration());
    }

    private static SectionId Section(Guid user, string type) => new(user, type, null, null);

    private static WidgetResult Page() => new([new BaseItemDto()], 1);

    private async Task StartListener()
    {
        var listener = new SectionCacheInvalidationListener(
            _userData.Object, _library.Object, _cache, _registry, Mock.Of<ITaskManager>(), _scoring, _clock, NullLogger<SectionCacheInvalidationListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
    }

    private void Store(Guid user, string type) =>
        Assert.True(_cache.Set(Section(user, type), 0, 20, Page(), _cache.StampOf(user), Ttl));

    private void Save(Guid user, UserDataSaveReason reason) =>
        _userData.Raise(m => m.UserDataSaved += null, new UserDataSaveEventArgs { UserId = user, SaveReason = reason, Item = new Movie() });

    private WidgetService Service(PluginConfiguration? configuration = null)
    {
        var config = configuration ?? new PluginConfiguration();
        var resolver = new WidgetLayoutResolver(
            _registry,
            Mock.Of<IUserConfigurationStore>(),
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => config,
            NullLogger<WidgetLayoutResolver>.Instance);
        return TestWidgetServices.Create(_registry, _sessionCache, resolver, () => config, sectionResults: _cache);
    }

    private IWidget Widget(string type, FreshnessLevel level, Action? onRead = null)
    {
        var mock = new Mock<IWidget>();
        mock.SetupGet(w => w.WidgetType).Returns(type);
        mock.SetupGet(w => w.Freshness).Returns(level);
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (type == DeferredType)
                {
                    Interlocked.Increment(ref _deferredReads);
                }

                onRead?.Invoke();

                return Page();
            });
        mock.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = type });
        return mock.Object;
    }
}
