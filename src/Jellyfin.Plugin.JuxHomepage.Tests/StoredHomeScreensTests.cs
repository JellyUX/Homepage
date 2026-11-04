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

    public void Dispose() => _disposables.ForEach(d => d.Dispose());

    private static Task<WidgetResult?> ReadSection(Server server, WidgetDescriptor section) =>
        server.Service.GetWidgetItems(server.UserId, section.WidgetType, section.AdditionalData, section.SectionKey, 0, section.MaxItems, CancellationToken.None);

    private Server Start(bool pendingScoring = false)
    {
        var widget = new OrderedWidget(this);
        var registry = new WidgetRegistry();
        registry.Register(widget);
        var config = new PluginConfiguration
        {
            Widgets = [new WidgetConfig { WidgetType = Ordered, Enabled = true, Order = 1, MinItems = 1, MaxItems = MaxItems }]
        };
        var userConfig = new Mock<IUserConfigurationStore>();
        var resolver = new WidgetLayoutResolver(
            registry,
            userConfig.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => config,
            NullLogger<WidgetLayoutResolver>.Instance);
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) => [.. _movies.Where(m => query.ItemIds.Contains(m.Id) && (query.IsPlayed != false || !_played.Contains(m.Id)))]);
        var dto = new Mock<IDtoService>();
        dto.StubBaseItemDtos((items, _) => [.. items.Select(i => new BaseItemDto { Id = i.Id })]);
        var sessions = new SessionCache(_clock);
        var pages = new SectionResultCache(sessions, _clock);
        _disposables.Add(sessions);
        _disposables.Add(pages);
        var service = TestWidgetServices.Create(
            registry,
            sessions,
            resolver,
            () => config,
            pendingScoring ? new AlwaysPending() : null,
            pages,
            stored: TestWidgetServices.NewStore(_disk, _clock),
            storedReader: new StoredSectionReader(library.Object, TestMocks.UserManagerReturning(_user).Object, dto.Object));
        return new Server(service, widget, sessions, _userId);
    }

    private sealed record Server(WidgetService Service, OrderedWidget Widget, SessionCache Sessions, Guid UserId);

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
