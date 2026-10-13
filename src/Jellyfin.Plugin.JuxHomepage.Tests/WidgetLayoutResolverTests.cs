using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

// Covers the global/user-override merge logic extracted into WidgetLayoutResolver (Phase 7.1 of
// TODO_V2.md). This is a real coverage gap fix: WidgetServiceTests' BuildService helper always mocks
// IUserConfigurationStore.GetUserConfiguration to return null, so the override-merge branch was never
// previously exercised by any test. Everything else (sorting, pagination, fan-out, MinItems
// filtering, translation) is already covered end-to-end via WidgetServiceTests' public entry points
// and is not duplicated here.
public sealed class WidgetLayoutResolverTests
{
    [Fact]
    public async Task BuildDescriptors_AllowUserOverrideTrue_UserOverrideWins()
    {
        var widget = MakeWidget("jux.test.widget", totalRecordCount: 10);
        var registry = new WidgetRegistry();
        registry.Register(widget);

        var globalConfig = new WidgetConfig
        {
            WidgetType = "jux.test.widget",
            CustomDisplayName = "Global Name",
            AllowUserOverride = true,
            Enabled = true,
            MinItems = 4
        };
        var userOverride = new WidgetConfig
        {
            WidgetType = "jux.test.widget",
            CustomDisplayName = "Override Name",
            AllowUserOverride = true,
            Enabled = true,
            MinItems = 4
        };

        var userConfigStoreMock = new Mock<IUserConfigurationStore>();
        userConfigStoreMock
            .Setup(s => s.GetUserConfiguration(It.IsAny<Guid>()))
            .Returns(new UserConfiguration { WidgetOverrides = [userOverride] });

        var resolver = new WidgetLayoutResolver(
            registry,
            userConfigStoreMock.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration { Widgets = [globalConfig] },
            NullLogger<WidgetLayoutResolver>.Instance);

        var descriptors = await resolver.BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("Override Name", descriptor.DisplayName);
    }

    [Fact]
    public async Task BuildDescriptors_AllowUserOverrideFalse_UserOverrideIgnored()
    {
        var widget = MakeWidget("jux.test.widget", totalRecordCount: 10);
        var registry = new WidgetRegistry();
        registry.Register(widget);

        var globalConfig = new WidgetConfig
        {
            WidgetType = "jux.test.widget",
            CustomDisplayName = "Global Name",
            AllowUserOverride = false,
            Enabled = true,
            MinItems = 4
        };
        var userOverride = new WidgetConfig
        {
            WidgetType = "jux.test.widget",
            CustomDisplayName = "Override Name",
            AllowUserOverride = false,
            Enabled = true,
            MinItems = 4
        };

        var userConfigStoreMock = new Mock<IUserConfigurationStore>();
        userConfigStoreMock
            .Setup(s => s.GetUserConfiguration(It.IsAny<Guid>()))
            .Returns(new UserConfiguration { WidgetOverrides = [userOverride] });

        var resolver = new WidgetLayoutResolver(
            registry,
            userConfigStoreMock.Object,
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => new PluginConfiguration { Widgets = [globalConfig] },
            NullLogger<WidgetLayoutResolver>.Instance);

        var descriptors = await resolver.BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("Global Name", descriptor.DisplayName);
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
    public async Task BuildDescriptors_TwoRowsOfTheSameType_OverrideAppliesToOneRowByRank()
    {
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));

        var first = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "Row 1", Order = 10, AllowUserOverride = true, Enabled = true, MinItems = 1 };
        var second = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "Row 2", Order = 20, AllowUserOverride = true, Enabled = true, MinItems = 1 };
        var userOverride = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "Mine", Order = 10, AllowUserOverride = true, Enabled = true, MinItems = 1 };

        var descriptors = await BuildResolverWithOverrides(registry, [first, second], [userOverride])
            .BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal(["Mine", "Row 2"], descriptors.Select(d => d.DisplayName));
    }

    [Fact]
    public async Task BuildDescriptors_InstanceRows_OverrideMatchedByInstanceValue()
    {
        var registry = new WidgetRegistry();
        registry.Register(MakeWidget("jux.test.widget", totalRecordCount: 10));

        // Instance rows (Discover, Rewards, Seasonal) carry a GUID instance id in ExtraParams["value"].
        var idA = Guid.NewGuid().ToString();
        var idB = Guid.NewGuid().ToString();
        var a = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "A", Order = 10, AllowUserOverride = true, Enabled = true, MinItems = 1, ExtraParams = [new WidgetExtraParam { Key = "value", Value = idA }] };
        var b = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "B", Order = 20, AllowUserOverride = true, Enabled = true, MinItems = 1, ExtraParams = [new WidgetExtraParam { Key = "value", Value = idB }] };
        var overrideB = new WidgetConfig { WidgetType = "jux.test.widget", CustomDisplayName = "Mine B", Order = 20, AllowUserOverride = true, Enabled = true, MinItems = 1, ExtraParams = [new WidgetExtraParam { Key = "value", Value = idB }] };

        var descriptors = await BuildResolverWithOverrides(registry, [a, b], [overrideB])
            .BuildDescriptors(Guid.NewGuid(), lang: null, CancellationToken.None);

        Assert.Equal(["A", "Mine B"], descriptors.Select(d => d.DisplayName));
    }

    [Fact]
    public void ResolveConfigRow_ReadsTheUsersOverride_SoASectionKeepsTheOrderOfItsProbe()
    {
        var global = new WidgetConfig { WidgetType = "jux.test.widget", Order = 10, AllowUserOverride = true, SortBy = WidgetSort.SortName };
        var userOverride = new WidgetConfig { WidgetType = "jux.test.widget", Order = 10, AllowUserOverride = true, SortBy = WidgetSort.Random };

        var row = BuildResolverWithOverrides(new WidgetRegistry(), [global], [userOverride])
            .ResolveConfigRow(Guid.NewGuid(), "jux.test.widget", additionalData: null);

        Assert.Equal(WidgetSort.Random, row!.SortBy);
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

        var keyed = resolver.ResolveConfigRow(Guid.NewGuid(), "jux.personalized.favorite-genre", "Drama", "jux.personalized.favorite-genre#2");
        var unkeyed = resolver.ResolveConfigRow(Guid.NewGuid(), "jux.personalized.favorite-genre", "Drama");

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
            .ResolveConfigRow(Guid.NewGuid(), "jux.test.widget", additionalData: null, sectionKey: "jux.test.widget#9");

        Assert.Same(row, resolved);
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

    private static IWidget MakeWidget(string widgetType, int totalRecordCount)
    {
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.DefaultDisplayName).Returns(widgetType);
        mock.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>()))
            .Returns((Guid _, WidgetInstanceConfig _, int _) => mock.Object);
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WidgetResult([], totalRecordCount));
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
