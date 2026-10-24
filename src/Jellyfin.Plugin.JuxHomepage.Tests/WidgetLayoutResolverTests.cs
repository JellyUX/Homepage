using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Local;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

// Covers the layout built by WidgetLayoutResolver from the global rows: section keys, personalized
// ranks, the row a section reads, and per-user overrides ignored while frozen (QUAL-02). Sorting,
// pagination and MinItems filtering are covered end-to-end via WidgetServiceTests' public entry points.
public sealed class WidgetLayoutResolverTests
{
    // QUAL-02 (D-05): per-user overrides are frozen; a user file that still has one changes nothing.
    [Fact]
    public async Task BuildDescriptors_UserWidgetOverride_IsIgnoredWhileFrozen()
    {
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));
        var global = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "Global Name", AllowUserOverride = true, Enabled = true, MinItems = 4 };
        var userOverride = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "Override Name", AllowUserOverride = true, Enabled = true, MinItems = 4 };

        var descriptors = await BuildResolverWithOverrides(registry, [global], [userOverride])
            .BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal("Global Name", Assert.Single(descriptors).DisplayName);
    }

    [Fact]
    public void ResolveConfigRow_UserWidgetOverride_IsIgnoredWhileFrozen()
    {
        var global = new WidgetConfig { WidgetType = "jux.test.widget", Order = 10, AllowUserOverride = true, SortBy = WidgetSort.SortName };
        var userOverride = new WidgetConfig { WidgetType = "jux.test.widget", Order = 10, AllowUserOverride = true, SortBy = WidgetSort.Random };

        var row = BuildResolverWithOverrides(new WidgetRegistry(), [global], [userOverride])
            .ResolveConfigRow("jux.test.widget", additionalData: null);

        Assert.Same(global, row);
    }

    // -------------------------------------------------------------------------
    // Personalized rank resolution, end to end: WidgetLayoutResolver -> rank computation ->
    // FavoriteGenreWidget.CreateInstances/GetItemsAsync -> WidgetDescriptor (TODO_V2.md Phase 8.6
    // garde-fou -- a real widget/ScoringService, not an isolated unit test, so a dead fan-out would
    // be caught the way it wasn't in V1).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildDescriptors_TwoFavoriteGenreRows_ProduceDistinctGenreSections()
    {
        var user = new User("test", "Default", "Default");
        var action1 = new Movie { Id = Guid.NewGuid(), Name = "A1", Genres = ["Action"] };
        var action2 = new Movie { Id = Guid.NewGuid(), Name = "A2", Genres = ["Action"] };
        var drama1 = new Movie { Id = Guid.NewGuid(), Name = "D1", Genres = ["Drama"] };

        var registry = new WidgetRegistry();
        registry.Register(BuildFavoriteGenreWidget(user, [action1, action2, drama1]));

        var row1 = new WidgetConfig { WidgetType = "jux.personalized.favorite-genre", Order = 0, MinItems = 1 };
        var row2 = new WidgetConfig { WidgetType = "jux.personalized.favorite-genre", Order = 10, MinItems = 1 };

        var resolver = BuildResolver(registry, [row1, row2]);

        var descriptors = await resolver.BuildDescriptors(user.Id, lang: null, CancellationToken.None);

        Assert.Equal(2, descriptors.Count);
        var genres = descriptors.Select(d => d.AdditionalData).ToList();
        Assert.Equal(["Action", "Drama"], genres);
    }

    [Fact]
    public async Task BuildDescriptors_ThreeFavoriteGenreRowsOnlyTwoDistinctGenres_ExcludesThirdRow()
    {
        var user = new User("test", "Default", "Default");
        var action1 = new Movie { Id = Guid.NewGuid(), Name = "A1", Genres = ["Action"] };
        var drama1 = new Movie { Id = Guid.NewGuid(), Name = "D1", Genres = ["Drama"] };

        var registry = new WidgetRegistry();
        registry.Register(BuildFavoriteGenreWidget(user, [action1, drama1]));

        var row1 = new WidgetConfig { WidgetType = "jux.personalized.favorite-genre", Order = 0, MinItems = 1 };
        var row2 = new WidgetConfig { WidgetType = "jux.personalized.favorite-genre", Order = 10, MinItems = 1 };
        var row3 = new WidgetConfig { WidgetType = "jux.personalized.favorite-genre", Order = 20, MinItems = 1 };

        var resolver = BuildResolver(registry, [row1, row2, row3]);

        var descriptors = await resolver.BuildDescriptors(user.Id, lang: null, CancellationToken.None);

        // Only 2 distinct genres exist, so the 3rd row (rank 3) must be excluded -- not fall back to
        // a duplicate of an already-shown genre (the V1 bug this phase eliminates).
        Assert.Equal(2, descriptors.Count);
        var genres = descriptors.Select(d => d.AdditionalData).ToList();
        Assert.Equal(["Action", "Drama"], genres);
    }

    [Fact]
    public async Task BuildDescriptors_GivesEachSectionTheKeyOfItsRow()
    {
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));
        var first = new WidgetConfig { WidgetType = "jux.test.widget", Order = 10, Enabled = true, CustomDisplayName = "First" };
        var second = new WidgetConfig { WidgetType = "jux.test.widget", Order = 20, Enabled = true, CustomDisplayName = "Second" };

        var descriptors = await BuildResolverWithOverrides(registry, [second, first], [])
            .BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal(["jux.test.widget#1", "jux.test.widget#2"], descriptors.Select(d => d.SectionKey));
    }

    [Fact]
    public async Task BuildDescriptors_WidgetConfigEnabledFalse_LeavesTheRowOut()
    {
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));
        var shown = new WidgetConfig { WidgetType = "jux.test.widget", Order = 10, Enabled = true, CustomDisplayName = "Shown" };
        var off = new WidgetConfig { WidgetType = "jux.test.widget", Order = 20, Enabled = false, CustomDisplayName = "Off" };

        var descriptors = await BuildResolverWithOverrides(registry, [shown, off], [])
            .BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal(["Shown"], descriptors.Select(d => d.DisplayName));
    }

    [Fact]
    public void ResolveConfigRow_SectionKey_ReadsTheSecondPersonalizedRowNotTheFirst()
    {
        // TODO_V4.md 9.3 (F-27): a Personalized section sends its scored value ("Drama"), never the
        // row's, so without the key the second row read the first row's excludeWatched.
        static WidgetConfig Row(int order, string excludeWatched) => new()
        {
            WidgetType = "jux.personalized.favorite-genre",
            Order = order,
            Enabled = true,
            ExtraParams = [new WidgetExtraParam { Key = "excludeWatched", Value = excludeWatched }]
        };
        var resolver = BuildResolverWithOverrides(new WidgetRegistry(), [Row(10, "true"), Row(20, "false")], []);

        var keyed = resolver.ResolveConfigRow("jux.personalized.favorite-genre", "Drama", "jux.personalized.favorite-genre#2");
        var unkeyed = resolver.ResolveConfigRow("jux.personalized.favorite-genre", "Drama");

        Assert.Equal("false", keyed!.ExtraParams.Single(p => p.Key == "excludeWatched").Value);
        Assert.Equal("true", unkeyed!.ExtraParams.Single(p => p.Key == "excludeWatched").Value);
    }

    [Fact]
    public async Task BuildDescriptors_NamesAPresetByItsTranslatedKey_UnlessTheAdminNamedIt()
    {
        // TODO_V4.md 9.4 (F-64).
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));
        static WidgetConfig Row(int order, string? name) => new()
        {
            WidgetType = "jux.test.widget",
            Order = order,
            Enabled = true,
            CustomDisplayName = name,
            ExtraParams = [new WidgetExtraParam { Key = "nameKey", Value = "jux.seasonal.christmas" }]
        };
        var resolver = new WidgetLayoutResolver(
            registry,
            new Mock<IUserConfigurationStore>().Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["en"] = new Dictionary<string, string> { ["jux.seasonal.christmas"] = "Christmas" },
                ["fr"] = new Dictionary<string, string> { ["jux.seasonal.christmas"] = "Noël" }
            }),
            () => new PluginConfiguration { Widgets = [Row(10, null), Row(20, "Our Christmas")] },
            NullLogger<WidgetLayoutResolver>.Instance);

        var descriptors = await resolver.BuildDescriptors(Guid.NewGuid(), lang: "fr", CancellationToken.None);

        Assert.Equal(["Noël", "Our Christmas"], descriptors.Select(d => d.DisplayName));
    }

    [Fact]
    public void ResolveConfigRow_UnknownSectionKey_FallsBackToTheFirstRowOfTheType()
    {
        var row = new WidgetConfig { WidgetType = "jux.test.widget", Order = 10 };

        var resolved = BuildResolverWithOverrides(new WidgetRegistry(), [row], [])
            .ResolveConfigRow("jux.test.widget", additionalData: null, sectionKey: "jux.test.widget#9");

        Assert.Same(row, resolved);
    }

    [Fact]
    public async Task BuildDescriptors_SlowSynchronousProbes_OverlapUpToTheBound()
    {
        // PERF-02: widget probes do their library reads synchronously and return a completed task,
        // so Task.WhenAll alone runs them one after another. Each of the first four probes holds its
        // library call until four are inside at once: they must overlap, and the two others can only
        // start once one of them has left, so the peak is exactly the fan-out bound of 4.
        var library = new TestMocks.CountingLibraryManager();
        var arrived = 0;
        var timedOut = false;
        using var fourInside = new ManualResetEventSlim();
        library.Inner.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref arrived) == 4)
                {
                    fourInside.Set();
                }

                timedOut |= !fourInside.Wait(TimeSpan.FromSeconds(10));
                return [];
            });
        var registry = new WidgetRegistry();
        var rows = new List<WidgetConfig>();
        for (var i = 0; i < 6; i++)
        {
            var widgetType = $"jux.test.slow-{i}";
            registry.Register(MakeSynchronousLibraryWidget(widgetType, library.Object));
            rows.Add(new WidgetConfig { WidgetType = widgetType, Enabled = true, MinItems = 1, Order = i });
        }

        var resolver = BuildResolver(registry, [.. rows]);

        var descriptors = await resolver.BuildDescriptors(Guid.NewGuid(), "en", CancellationToken.None);

        Assert.False(timedOut, "Four probes never ran at the same time.");
        Assert.Equal(rows.Select(r => r.WidgetType), descriptors.Select(d => d.WidgetType));
        Assert.Equal(6, library.Count("GetItemIds"));
        Assert.Equal(4, library.MaxInFlight);
    }

    [Fact]
    public async Task BuildDescriptors_ResolveWaitingForScoring_OverlapsTheOtherProbes()
    {
        // PERF-02.b: a Personalized row's Resolve can wait up to 2 s for the user's first scoring
        // (ScoringService). That wait must not hold the rows after it: here the first row's Resolve
        // only returns once the second row has been probed.
        using var secondProbed = new ManualResetEventSlim();
        var waited = false;
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.waiting", totalRecordCount: 10, onResolve: () => waited = secondProbed.Wait(TimeSpan.FromSeconds(10))));
        registry.Register(MakeWidget("jux.test.other", totalRecordCount: 10, onProbe: secondProbed.Set));
        var resolver = BuildResolver(
            registry,
            [
                new WidgetConfig { WidgetType = "jux.test.waiting", Enabled = true, MinItems = 1, Order = 0 },
                new WidgetConfig { WidgetType = "jux.test.other", Enabled = true, MinItems = 1, Order = 1 }
            ]);

        var descriptors = await resolver.BuildDescriptors(Guid.NewGuid(), "en", CancellationToken.None);

        Assert.True(waited, "The second row was not probed while the first one waited in Resolve.");
        Assert.Equal(["jux.test.waiting", "jux.test.other"], descriptors.Select(d => d.WidgetType));
    }

    private static IWidget MakeSynchronousLibraryWidget(string widgetType, ILibraryManager libraryManager)
    {
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.DefaultDisplayName).Returns(widgetType);
        mock.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>()))
            .Returns((Guid _, WidgetInstanceConfig _, int _) => mock.Object);
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                libraryManager.GetItemIds(new InternalItemsQuery());
                return Task.FromResult(new WidgetResult([], 10));
            });
        mock.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = widgetType });
        return mock.Object;
    }

    private static WidgetLayoutResolver BuildResolverWithOverrides(IWidgetRegistry registry, WidgetConfig[] widgets, WidgetConfig[] overrides)
    {
        var userConfigStoreMock = new Mock<IUserConfigurationStore>();
        userConfigStoreMock
            .Setup(s => s.GetUserConfiguration(It.IsAny<Guid>()))
            .Returns(new UserConfiguration { WidgetOverrides = overrides });

        return new WidgetLayoutResolver(
            registry,
            userConfigStoreMock.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration { Widgets = widgets },
            NullLogger<WidgetLayoutResolver>.Instance);
    }

    private static WidgetLayoutResolver BuildResolver(IWidgetRegistry registry, WidgetConfig[] widgets)
    {
        var userConfigStoreMock = new Mock<IUserConfigurationStore>();
        userConfigStoreMock
            .Setup(s => s.GetUserConfiguration(It.IsAny<Guid>()))
            .Returns((UserConfiguration?)null);

        return new WidgetLayoutResolver(
            registry,
            userConfigStoreMock.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration { Widgets = widgets },
            NullLogger<WidgetLayoutResolver>.Instance);
    }

    private static FavoriteGenreWidget BuildFavoriteGenreWidget(User user, IReadOnlyList<BaseItem> watched)
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns(watched);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Series }))))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Episode }))))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IsFavorite == true)))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetPeople(It.IsAny<BaseItem>()))
            .Returns([]);

        // Probe query (Limit=1, called by WidgetLayoutResolver.FetchInstanceDescriptor) -- enough
        // items to clear each row's MinItems=1 threshold.
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>([new Movie { Id = Guid.NewGuid(), Name = "Result" }]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User _, BaseItem _) =>
                items.Select(i => new BaseItemDto { Id = i.Id }).ToList());

        var scoringService = new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration());

        return new FavoriteGenreWidget(
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            scoringService,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["en"] = new Dictionary<string, string>
                {
                    ["jux.personalized.favorite-genre.format"] = "More {value}"
                }
            }));
    }

    private static IWidget MakeWidget(string widgetType, int totalRecordCount, Action? onResolve = null, Action? onProbe = null)
    {
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.DefaultDisplayName).Returns(widgetType);
        mock.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>()))
            .Returns((Guid _, WidgetInstanceConfig _, int _) =>
            {
                onResolve?.Invoke();
                return mock.Object;
            });
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                onProbe?.Invoke();
                return Task.FromResult(new WidgetResult([], totalRecordCount));
            });
        mock.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = widgetType });
        return mock.Object;
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(500, WidgetConfig.MaxItemsCeiling)]
    public async Task BuildDescriptors_CarriesMaxItemsUpToTheCeiling(int configured, int expected)
    {
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));
        var resolver = new WidgetLayoutResolver(
            registry,
            Mock.Of<IUserConfigurationStore>(),
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration
            {
                Widgets = [new WidgetConfig { WidgetType = "jux.test.widget", Enabled = true, MinItems = 1, MaxItems = configured }]
            },
            NullLogger<WidgetLayoutResolver>.Instance);

        var descriptors = await resolver.BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(descriptors).MaxItems);
    }

    [Theory]
    [InlineData(WidgetViewMode.Landscape)]
    [InlineData(WidgetViewMode.Portrait)]
    public async Task BuildDescriptors_RowViewMode_IsTheSectionViewMode(string viewMode)
    {
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));
        var resolver = BuildResolver(registry, [new WidgetConfig { WidgetType = "jux.test.widget", Enabled = true, MinItems = 1, ViewMode = viewMode }]);

        var descriptors = await resolver.BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal(viewMode, Assert.Single(descriptors).ViewMode);
    }

    [Fact]
    public async Task BuildDescriptors_RowSortByAndSortOrder_ReachTheProbe()
    {
        WidgetPayload? probed = null;
        var widget = new Mock<IWidget>();
        widget.Setup(w => w.WidgetType).Returns("jux.test.widget");
        widget.Setup(w => w.DefaultDisplayName).Returns("jux.test.widget");
        widget.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>())).Returns(() => widget.Object);
        widget.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = "jux.test.widget" });
        widget.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Callback<WidgetPayload, CancellationToken>((payload, _) => probed = payload)
            .ReturnsAsync(new WidgetResult([], 10));
        var registry = new WidgetRegistry();
        registry.Register(widget.Object);
        var resolver = BuildResolver(registry,
            [new WidgetConfig { WidgetType = "jux.test.widget", Enabled = true, MinItems = 1, SortBy = WidgetSort.DateCreated, SortOrder = WidgetSort.Descending }]);

        await resolver.BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal(WidgetSort.DateCreated, probed?.SortBy);
        Assert.Equal(WidgetSort.Descending, probed?.SortOrder);
    }

    // KTW-01.a, DEC-P11-4: a featured row reaches the section and its probe, unless the widget cannot feature.
    [Theory]
    [InlineData(null, true, 7)]
    [InlineData(false, false, 0)]
    public async Task BuildDescriptors_FeaturedRowWithFeaturedMinRating_ReachesTheSectionAndProbeWhenTheWidgetCanFeature(
        bool? canFeature, bool expectedFeatured, int expectedMinRating)
    {
        WidgetPayload? probed = null;
        var widget = new Mock<IWidget>();
        widget.Setup(w => w.WidgetType).Returns("jux.test.widget");
        widget.Setup(w => w.DefaultDisplayName).Returns("jux.test.widget");
        widget.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>())).Returns(() => widget.Object);
        widget.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = "jux.test.widget", CanFeature = canFeature });
        widget.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Callback<WidgetPayload, CancellationToken>((payload, _) => probed = payload)
            .ReturnsAsync(new WidgetResult([], 10));
        var registry = new WidgetRegistry();
        registry.Register(widget.Object);
        var resolver = BuildResolver(registry,
            [new WidgetConfig { WidgetType = "jux.test.widget", Enabled = true, MinItems = 1, Featured = true, FeaturedMinRating = 7 }]);

        var descriptor = Assert.Single(await resolver.BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None));

        Assert.Equal(expectedFeatured, descriptor.Featured);
        Assert.Equal((expectedFeatured, expectedMinRating), (probed?.Featured, probed?.FeaturedMinRating));
    }

    // KTW-01.a, R11 constat 3: a featured section with fewer than 3 backdrops keeps every title for its
    // cards, counted against MinItems like a row of cards, its titles with a backdrop first.
    [Fact]
    public async Task BuildDescriptors_FeaturedSectionWithFewBackdrops_KeepsItsTitlesForTheCards()
    {
        var user = TestMocks.DefaultUser();
        var users = new Mock<IUserManager>();
        users.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);
        Movie Movie(string name, bool backdrop) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            ImageInfos = backdrop ? [new ItemImageInfo { Type = ImageType.Backdrop, Path = "/b.jpg" }] : []
        };
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>([Movie("A", false), Movie("B", true), Movie("C", false), Movie("D", true)]));
        var dtos = new Mock<IDtoService>();
        dtos.Setup(s => s.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => items.Select(i => new BaseItemDto { Name = i.Name }).ToList());
        var registry = new WidgetRegistry();
        registry.Register(new TopRatedWidget(users.Object, library.Object, dtos.Object, new ManualTimeProvider()));
        var resolver = BuildResolver(registry,
            [new WidgetConfig { WidgetType = "jux.local.top-rated", Enabled = true, MinItems = 4, Featured = true }]);

        var descriptor = Assert.Single(await resolver.BuildDescriptors(user.Id, lang: null, CancellationToken.None));
        var items = await registry.GetByType("jux.local.top-rated")!.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, Limit = 20 }.WithFeatured(new WidgetConfig { Featured = true }, descriptor), CancellationToken.None);

        Assert.True(descriptor.Featured);
        Assert.Equal(["B", "D", "A", "C"], items.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task BuildDescriptors_RowNotFeatured_SendsNoMinRatingToTheProbe()
    {
        WidgetPayload? probed = null;
        var widget = new Mock<IWidget>();
        widget.Setup(w => w.WidgetType).Returns("jux.test.widget");
        widget.Setup(w => w.DefaultDisplayName).Returns("jux.test.widget");
        widget.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>())).Returns(() => widget.Object);
        widget.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = "jux.test.widget" });
        widget.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Callback<WidgetPayload, CancellationToken>((payload, _) => probed = payload)
            .ReturnsAsync(new WidgetResult([], 10));
        var registry = new WidgetRegistry();
        registry.Register(widget.Object);
        var resolver = BuildResolver(registry, [new WidgetConfig { WidgetType = "jux.test.widget", Enabled = true, MinItems = 1, FeaturedMinRating = 8 }]);

        var descriptor = Assert.Single(await resolver.BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None));

        Assert.False(descriptor.Featured);
        Assert.Equal((false, 0), (probed?.Featured, probed?.FeaturedMinRating));
    }

    // TODO_V4.md 6.5 (F-62): a preview reads the row sent by the admin page, never the saved one.
    [Fact]
    public async Task PreviewAsync_ReadsTheUnsavedRowUpToItsMaxItems()
    {
        WidgetPayload? seen = null;
        var item = new BaseItemDto { Name = "Alien" };
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns("jux.admin.genre");
        mock.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>())).Returns(mock.Object);
        mock.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = "jux.admin.genre" });
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Callback((WidgetPayload p, CancellationToken _) => seen = p)
            .ReturnsAsync(new WidgetResult([item], 1));
        var registry = new WidgetRegistry();
        registry.Register(mock.Object);
        var saved = new WidgetConfig { WidgetType = "jux.admin.genre", ExtraParams = [new WidgetExtraParam { Key = "value", Value = "Drame" }] };
        var resolver = new WidgetLayoutResolver(
            registry,
            Mock.Of<IUserConfigurationStore>(),
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration { Widgets = [saved] },
            NullLogger<WidgetLayoutResolver>.Instance);
        var edited = new WidgetConfig
        {
            WidgetType = "jux.admin.genre",
            CustomDisplayName = "Science-Fiction",
            MinItems = 50,
            MaxItems = 500,
            ExtraParams = [new WidgetExtraParam { Key = "value", Value = "Science-Fiction" }]
        };

        var preview = await resolver.PreviewAsync(Guid.NewGuid(), edited, lang: null, CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal("Science-Fiction", seen!.AdditionalData);
        Assert.Equal(WidgetConfig.MaxItemsCeiling, seen.Limit);
        Assert.Equal("Science-Fiction", preview.DisplayName);
        Assert.Same(item, Assert.Single(preview.Items));
        Assert.Equal(1, preview.TotalRecordCount);
        Assert.Equal(50, preview.MinItems);
        Assert.Equal(WidgetPreview.HiddenBelowMinItems, preview.HiddenReason);
    }

    [Fact]
    public async Task PreviewAsync_RowWithoutAnInstanceForTheUser_IsHiddenWithNoValue()
    {
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns("jux.personalized.favorite-genre");
        mock.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>())).Returns((IWidget?)null);
        var registry = new WidgetRegistry();
        registry.Register(mock.Object);
        var resolver = new WidgetLayoutResolver(
            registry,
            Mock.Of<IUserConfigurationStore>(),
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration(),
            NullLogger<WidgetLayoutResolver>.Instance);

        var preview = await resolver.PreviewAsync(
            Guid.NewGuid(), new WidgetConfig { WidgetType = "jux.personalized.favorite-genre" }, lang: null, CancellationToken.None);

        Assert.Equal(WidgetPreview.HiddenNoValue, preview!.HiddenReason);
        Assert.Empty(preview.Items);
        Assert.Null(await resolver.PreviewAsync(Guid.NewGuid(), new WidgetConfig { WidgetType = "jux.unknown" }, null, CancellationToken.None));
    }
}
