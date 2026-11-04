using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// S1 (DR-033): the deferred level of a home screen is kept on disk. After a restart, the layout and the deferred
/// sections are read again from the stored order with live user data: no deferred widget is resolved (no scoring)
/// nor read (no probe), a title played since is left out, and a stored order run short is computed again in the
/// background.
/// </summary>
public sealed class StoredHomeScreensTests : IDisposable
{
    private const string Ordered = "test.deferred.ordered";
    private const int MaxItems = 5;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly User _user = TestMocks.DefaultUser();
    private readonly InMemoryFileSystem _disk = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly List<TestMovie> _movies = [.. Enumerable.Range(0, 20).Select(_ => new TestMovie { Id = Guid.NewGuid() })];
    private readonly HashSet<Guid> _played = [];
    private readonly List<IDisposable> _disposables = [];
    private PluginConfiguration _config = NewConfiguration();
    private UserConfiguration? _userConfig;

    [Fact]
    public async Task AfterRestart_TheDeferredSectionsAreReadFromTheStoredOrder_WithoutScoringNorProbing()
    {
        var before = Start();
        var first = await before.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        Assert.Equal([Ordered], first.Sections.Select(s => s.WidgetType));
        Assert.Equal((1, 1), (before.Widget.Resolves, before.Widget.Reads));

        var after = Start();
        var layout = await after.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        var section = await ReadSection(after, layout.Sections[0]);

        Assert.Equal([Ordered], layout.Sections.Select(s => s.WidgetType));
        Assert.Equal(_movies.Take(MaxItems).Select(m => m.Id), section!.Items.Select(i => i.Id));
        Assert.Equal((0, 0), (after.Widget.Resolves, after.Widget.Reads));
    }

    [Fact]
    public async Task AfterRestart_ATitlePlayedSince_IsLeftOutLive_AndTheShortOrderIsComputedAgainInTheBackground()
    {
        var before = Start();
        await before.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        _played.Add(_movies[1].Id);
        foreach (var movie in _movies.Skip(MaxItems + 1))
        {
            _played.Add(movie.Id);
        }

        var after = Start();
        var layout = await after.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        var section = await ReadSection(after, layout.Sections[0]);

        Assert.Equal(new[] { 0, 2, 3, 4, 5 }.Select(i => _movies[i].Id), section!.Items.Select(i => i.Id));
        Assert.Equal(0, after.Widget.Reads);

        // Fewer stored titles left than MaxItems would be after one more play: the margin of 2 x MaxItems ran short.
        _played.Add(_movies[2].Id);
        after.Sessions.Invalidate(_userId);
        await after.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        await after.Service.WhenIdleForTesting();

        Assert.Equal((1, 1), (after.Widget.Resolves, after.Widget.Reads));
    }

    [Fact]
    public async Task AStoredLayoutOlderThanTheFreshness_IsServed_ThenComputedAgainInTheBackground()
    {
        var before = Start();
        await before.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(25));

        var after = Start();
        var layout = await after.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        Assert.Equal([Ordered], layout.Sections.Select(s => s.WidgetType));
        await after.Service.WhenIdleForTesting();

        Assert.Equal((1, 1), (after.Widget.Resolves, after.Widget.Reads));
        var again = Start();
        await again.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        Assert.Equal((0, 0), (again.Widget.Resolves, again.Widget.Reads));
    }

    [Fact]
    public async Task ALayoutBuiltBeforeTheFirstScoring_IsNotStored()
    {
        var before = Start(pendingScoring: true);
        await before.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);

        Assert.Empty(_disk.Paths);
    }

    // R7 sisters of the cache invalidations (DR-033 E): a saved configuration and a change of the user's own settings
    // change the discriminant, so the stored layout is computed again at once.
    [Fact]
    public async Task AfterASavedConfigurationOrAChangedUserSetting_TheStoredLayoutIsComputedAgain()
    {
        await Start().Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);

        _config = NewConfiguration(maxItems: MaxItems + 1);
        var saved = Start();
        await saved.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        Assert.Equal((1, 1), (saved.Widget.Resolves, saved.Widget.Reads));

        _userConfig = new UserConfiguration { HiddenSections = ["jux.other#1"] };
        var changed = Start();
        await changed.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        Assert.Equal((1, 1), (changed.Widget.Resolves, changed.Widget.Reads));

        var unchanged = Start();
        await unchanged.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        Assert.Equal((0, 0), (unchanged.Widget.Resolves, unchanged.Widget.Reads));
    }

    // A new scoring or a change of access marks the user (UserAccessWatch, SectionCacheInvalidationListener), a library
    // scan or a refreshed list the sources it changed (SectionResultCache.ClearSources): the stored layout is served,
    // then computed again in the background, and the marks survive a restart.
    [Theory]
    [InlineData("user")]
    [InlineData("source")]
    public async Task AMarkedUserOrSource_IsServedAfterARestart_ThenComputedAgainInTheBackground(string mark)
    {
        var before = Start();
        await before.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(1));
        if (mark == "user")
        {
            before.Store.MarkUserStale(_userId);
        }
        else
        {
            before.Pages.ClearSources([SectionSources.Movies]);
        }

        var after = Start();
        var layout = await after.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        Assert.Equal([Ordered], layout.Sections.Select(s => s.WidgetType));
        await after.Service.WhenIdleForTesting();

        Assert.Equal((1, 1), (after.Widget.Resolves, after.Widget.Reads));
    }

    [Fact]
    public async Task AChangeOfAnotherSource_LeavesTheStoredLayoutFresh()
    {
        var before = Start();
        await before.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(1));
        before.Pages.ClearSources([SectionSources.Rewards]);

        var after = Start();
        await after.Service.GetWidgetsForUser(_userId, 0, "en", CancellationToken.None);
        await after.Service.WhenIdleForTesting();

        Assert.Equal((0, 0), (after.Widget.Resolves, after.Widget.Reads));
    }

    // A mark taken while a layout is computed is not cleared by the write that ends the computation.
    [Fact]
    public void AMarkTakenDuringTheComputation_KeepsTheLayoutWrittenAfterItDue()
    {
        var store = TestWidgetServices.NewStore(_disk, _clock);
        var startedAt = store.UtcNow;
        store.Write(_userId, "en", new StoredLayout { ComputedAtUtc = startedAt, Discriminant = "d", Rows = [] });
        _clock.Advance(TimeSpan.FromSeconds(1));
        store.MarkUserStale(_userId);
        store.Write(_userId, "en", new StoredLayout { ComputedAtUtc = startedAt, Discriminant = "d", Rows = [] });

        Assert.False(store.Read(_userId, "en", "d", TimeSpan.FromHours(24))!.Value.Fresh);
    }

    public void Dispose() => _disposables.ForEach(d => d.Dispose());

    private static Task<WidgetResult?> ReadSection(Server server, WidgetDescriptor section) =>
        server.Service.GetWidgetItems(server.UserId, section.WidgetType, section.AdditionalData, section.SectionKey, 0, section.MaxItems, CancellationToken.None);

    private Server Start(bool pendingScoring = false)
    {
        var widget = new OrderedWidget(this);
        var registry = new WidgetRegistry();
        registry.Register(widget);
        var userConfig = new Mock<IUserConfigurationStore>();
        userConfig.Setup(s => s.GetUserConfiguration(It.IsAny<Guid>())).Returns(() => _userConfig);
        var resolver = new WidgetLayoutResolver(
            registry,
            userConfig.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => _config,
            NullLogger<WidgetLayoutResolver>.Instance);
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) => [.. _movies.Where(m => query.ItemIds.Contains(m.Id) && (query.IsPlayed != false || !_played.Contains(m.Id)))]);
        var dto = new Mock<IDtoService>();
        dto.StubBaseItemDtos((items, _) => [.. items.Select(i => new BaseItemDto { Id = i.Id })]);
        var store = TestWidgetServices.NewStore(_disk, _clock);
        var sessions = new SessionCache(_clock);
        var pages = new SectionResultCache(sessions, _clock);
        _disposables.Add(sessions);
        _disposables.Add(pages);
        var service = TestWidgetServices.Create(
            registry,
            sessions,
            resolver,
            () => _config,
            pendingScoring ? new AlwaysPending() : null,
            pages,
            stored: store,
            storedReader: new StoredSectionReader(library.Object, TestMocks.UserManagerReturning(_user).Object, dto.Object));
        return new Server(service, widget, sessions, pages, store, _userId);
    }

    private static PluginConfiguration NewConfiguration(int maxItems = MaxItems) => new()
    {
        Widgets = [new WidgetConfig { WidgetType = Ordered, Enabled = true, Order = 1, MinItems = 1, MaxItems = maxItems }]
    };

    private sealed record Server(WidgetService Service, OrderedWidget Widget, SessionCache Sessions, SectionResultCache Pages, StoredHomeScreens Store, Guid UserId);

    private sealed class AlwaysPending : IPersonalizationState
    {
        public bool TakePendingServed(Guid userId) => true;
    }

    /// <summary>A deferred widget that hides played titles and reads the unplayed movies in library order.</summary>
    private sealed class OrderedWidget(StoredHomeScreensTests test) : IWidget, IHidesPlayedItems
    {
        public int Resolves { get; private set; }

        public int Reads { get; private set; }

        public string WidgetType => Ordered;

        public string DefaultDisplayName => Ordered;

        public WidgetCategory Category => WidgetCategory.Personalized;

        public FreshnessLevel Freshness => FreshnessLevel.Deferred;

        public IReadOnlyList<string> Sources => SectionSources.MoviesAndSeries;

        public int DefaultMinItems => 1;

        public IReadOnlyList<WidgetExtraParam> DefaultExtraParams => [];

        public bool HidesPlayed(IReadOnlyDictionary<string, string>? extraParams) => true;

        public IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank)
        {
            Resolves++;
            return this;
        }

        public Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
        {
            Reads++;
            BaseItem[] page = [.. test._movies.Where(m => !test._played.Contains(m.Id)).Skip(payload.StartIndex).Take(payload.Limit)];
            return Task.FromResult(WidgetResult.Project(TestMocks.DtoServiceReturningEmpty().Object, page, new(), test._user));
        }

        public WidgetDescriptor GetDescriptor() => new() { WidgetType = Ordered, DisplayName = Ordered };
    }
}
