using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class WidgetServiceTests : IDisposable
{
    private readonly SessionCache _sessionCache = new();
    private readonly Mock<IUserConfigurationStore> _userConfigStoreMock = new();

    // -------------------------------------------------------------------------
    // MinItems filtering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWidgetsForUser_WidgetMeetsMinItems_IncludedInResult()
    {
        const string widgetType = "enough-items";
        const int minItems = 4;

        var widget = MakeWidget(widgetType, totalRecordCount: 10);
        var service = BuildService(
            registeredWidgets: [widget],
            globalWidgets:
            [
                new WidgetConfig { WidgetType = widgetType, Enabled = true, MinItems = minItems, Order = 0 }
            ]);

        var result = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "en", CancellationToken.None)).Sections;

        Assert.Single(result);
        Assert.Equal(widgetType, result[0].WidgetType);
    }

    [Fact]
    public async Task GetWidgetsForUser_WidgetBelowMinItems_ExcludedFromResult()
    {
        const string widgetType = "few-items";
        const int minItems = 4;

        var widget = MakeWidget(widgetType, totalRecordCount: 2);
        var service = BuildService(
            registeredWidgets: [widget],
            globalWidgets:
            [
                new WidgetConfig { WidgetType = widgetType, Enabled = true, MinItems = minItems, Order = 0 }
            ]);

        var result = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "en", CancellationToken.None)).Sections;

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // Order sorting
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWidgetsForUser_WidgetsSortedByOrder()
    {
        var wA = MakeWidget("a", totalRecordCount: 10);
        var wB = MakeWidget("b", totalRecordCount: 10);
        var wC = MakeWidget("c", totalRecordCount: 10);

        var service = BuildService(
            registeredWidgets: [wA, wB, wC],
            globalWidgets:
            [
                new WidgetConfig { WidgetType = "a", Enabled = true, MinItems = 1, Order = 20 },
                new WidgetConfig { WidgetType = "b", Enabled = true, MinItems = 1, Order = 5 },
                new WidgetConfig { WidgetType = "c", Enabled = true, MinItems = 1, Order = 10 }
            ]);

        var result = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "en", CancellationToken.None)).Sections;

        Assert.Equal(3, result.Count);
        Assert.Equal("b", result[0].WidgetType);
        Assert.Equal("c", result[1].WidgetType);
        Assert.Equal("a", result[2].WidgetType);
    }

    // -------------------------------------------------------------------------
    // Pagination
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWidgetsForUser_Pagination_Page0Returns20()
    {
        var widgets = Enumerable.Range(0, 25)
            .Select(i => MakeWidget($"w{i:D2}", totalRecordCount: 10))
            .ToArray();

        var configs = widgets
            .Select((w, i) => new WidgetConfig
            {
                WidgetType = $"w{i:D2}",
                Enabled = true,
                MinItems = 1,
                Order = i
            })
            .ToArray();

        var service = BuildService(registeredWidgets: widgets, globalWidgets: configs);
        var userId = Guid.NewGuid();

        var page0 = (await service.GetWidgetsForUser(userId, page: 0, lang: "en", CancellationToken.None)).Sections;
        var page1 = (await service.GetWidgetsForUser(userId, page: 1, lang: "en", CancellationToken.None)).Sections;

        Assert.Equal(20, page0.Count);
        Assert.Equal(5, page1.Count);
    }

    // -------------------------------------------------------------------------
    // Multi-instance (admin widgets via ExtraParams["value"])
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWidgetsForUser_TwoRowsSameType_ProduceTwoDescriptorsWithDistinctAdditionalData()
    {
        const string widgetType = "jux.admin.genre";

        // One widget registered; two config rows differing only in ExtraParams["value"].
        var widget = MakeWidget(widgetType, totalRecordCount: 10);
        var service = BuildService(
            registeredWidgets: [widget],
            globalWidgets:
            [
                new WidgetConfig
                {
                    WidgetType = widgetType,
                    CustomDisplayName = "Action",
                    Enabled = true,
                    MinItems = 1,
                    Order = 0,
                    AllowUserOverride = false,
                    ExtraParams = [new WidgetExtraParam { Key = "value", Value = "Action" }]
                },
                new WidgetConfig
                {
                    WidgetType = widgetType,
                    CustomDisplayName = "Comedy",
                    Enabled = true,
                    MinItems = 1,
                    Order = 10,
                    AllowUserOverride = false,
                    ExtraParams = [new WidgetExtraParam { Key = "value", Value = "Comedy" }]
                }
            ]);

        var result = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "en", CancellationToken.None)).Sections;

        Assert.Equal(2, result.Count);
        Assert.Equal("Action", result[0].AdditionalData);
        Assert.Equal("Comedy", result[1].AdditionalData);
        Assert.Equal("Action", result[0].DisplayName);
        Assert.Equal("Comedy", result[1].DisplayName);
    }

    // -------------------------------------------------------------------------
    // Localization (11.3): DisplayName is translated per-request when no CustomDisplayName is set,
    // but an explicit CustomDisplayName always wins regardless of language.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWidgetsForUser_NoCustomDisplayName_TranslatesUsingRequestedLanguage()
    {
        const string widgetType = "jux.native.my-media";
        var localization = new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["fr"] = new Dictionary<string, string> { [widgetType] = "Mes médias" },
            ["en"] = new Dictionary<string, string> { [widgetType] = "My Media" }
        });

        var widget = MakeWidget(widgetType, totalRecordCount: 10);
        var service = BuildService(
            registeredWidgets: [widget],
            globalWidgets: [new WidgetConfig { WidgetType = widgetType, Enabled = true, MinItems = 1, Order = 0 }],
            localizationService: localization);

        var resultEn = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "en", CancellationToken.None)).Sections;
        var resultFr = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "fr", CancellationToken.None)).Sections;

        Assert.Equal("My Media", resultEn[0].DisplayName);
        Assert.Equal("Mes médias", resultFr[0].DisplayName);
    }

    [Fact]
    public async Task GetWidgetsForUser_CustomDisplayNameSet_IgnoresTranslationRegardlessOfLanguage()
    {
        const string widgetType = "jux.native.my-media";
        var localization = new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["fr"] = new Dictionary<string, string> { [widgetType] = "Mes médias" },
            ["en"] = new Dictionary<string, string> { [widgetType] = "My Media" }
        });

        var widget = MakeWidget(widgetType, totalRecordCount: 10);
        var service = BuildService(
            registeredWidgets: [widget],
            globalWidgets:
            [
                new WidgetConfig { WidgetType = widgetType, CustomDisplayName = "Ma section perso", Enabled = true, MinItems = 1, Order = 0 }
            ],
            localizationService: localization);

        var resultEn = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "en", CancellationToken.None)).Sections;
        var resultFr = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "fr", CancellationToken.None)).Sections;

        Assert.Equal("Ma section perso", resultEn[0].DisplayName);
        Assert.Equal("Ma section perso", resultFr[0].DisplayName);
    }

    // -------------------------------------------------------------------------
    // GetWidgetItems
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWidgetsForUser_ProbeAsksMinItemsAndCountsItemsWithoutTotal()
    {
        // F-05: the probe reads MinItems items and decides from what came back; a widget that skips
        // the count query reports the page size as its total.
        const string widgetType = "page-size-total";
        const int minItems = 4;
        var limits = new List<int>();
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.DefaultDisplayName).Returns(widgetType);
        mock.Setup(w => w.Resolve(It.IsAny<Guid>(), It.IsAny<WidgetInstanceConfig>(), It.IsAny<int>()))
            .Returns(() => mock.Object);
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WidgetPayload p, CancellationToken _) =>
            {
                limits.Add(p.Limit);
                var items = Enumerable.Range(0, p.Limit).Select(_ => new BaseItemDto()).ToList();
                return new WidgetResult(items, items.Count);
            });
        mock.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = widgetType });

        var service = BuildService(
            registeredWidgets: [mock.Object],
            globalWidgets: [new WidgetConfig { WidgetType = widgetType, Enabled = true, MinItems = minItems, Order = 0 }]);

        var result = (await service.GetWidgetsForUser(Guid.NewGuid(), page: 0, lang: "en", CancellationToken.None)).Sections;

        Assert.Equal(widgetType, Assert.Single(result).WidgetType);
        Assert.Equal(minItems, Assert.Single(limits));
    }

    // -------------------------------------------------------------------------
    // Session cache and pending personalization (F-03)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWidgetsForUser_PendingPersonalizationServed_LayoutNotCachedUntilReady()
    {
        const string widgetType = "any-widget";
        var userId = Guid.NewGuid();
        var pendingState = new Mock<IPersonalizationState>();
        pendingState.SetupSequence(s => s.TakePendingServed(userId)).Returns(true).Returns(false);

        var service = BuildService(
            registeredWidgets: [MakeWidget(widgetType, totalRecordCount: 10)],
            globalWidgets: [new WidgetConfig { WidgetType = widgetType, Enabled = true, MinItems = 1, Order = 0 }],
            personalizationState: pendingState.Object);
        var ttl = TimeSpan.FromMinutes(15);

        await service.GetWidgetsForUser(userId, page: 0, lang: "en", CancellationToken.None);
        Assert.False(_sessionCache.TryGet(userId, "en", ttl, out _));

        await service.GetWidgetsForUser(userId, page: 0, lang: "en", CancellationToken.None);
        Assert.True(_sessionCache.TryGet(userId, "en", ttl, out _));
    }

    [Fact]
    public async Task GetWidgetItems_KnownWidget_ReturnsResult()
    {
        const string widgetType = "known";
        var widget = MakeWidget(widgetType, totalRecordCount: 5);

        var service = BuildService(registeredWidgets: [widget], globalWidgets: []);

        var result = await service.GetWidgetItems(
            Guid.NewGuid(), widgetType, additionalData: null, sectionKey: null,
            startIndex: 0, limit: 20, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetWidgetItems_UnknownWidget_ReturnsNull()
    {
        var service = BuildService(registeredWidgets: [], globalWidgets: []);

        var result = await service.GetWidgetItems(
            Guid.NewGuid(), "ghost", additionalData: null, sectionKey: null,
            startIndex: 0, limit: 20, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetWidgetItems_TwoRowsSameType_ForwardsOwnInstanceExtraParams()
    {
        const string widgetType = "jux.connected.discover-movies";
        WidgetPayload? capturedPayload = null;

        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Callback<WidgetPayload, CancellationToken>((payload, _) => capturedPayload = payload)
            .ReturnsAsync(new WidgetResult([], 0));

        var service = BuildService(
            registeredWidgets: [mock.Object],
            globalWidgets:
            [
                new WidgetConfig
                {
                    WidgetType = widgetType,
                    Enabled = true,
                    Order = 0,
                    ExtraParams =
                    [
                        new WidgetExtraParam { Key = "value", Value = "instance-a" },
                        new WidgetExtraParam { Key = "sortBy", Value = "popularity.desc" }
                    ]
                },
                new WidgetConfig
                {
                    WidgetType = widgetType,
                    Enabled = true,
                    Order = 10,
                    ExtraParams =
                    [
                        new WidgetExtraParam { Key = "value", Value = "instance-b" },
                        new WidgetExtraParam { Key = "sortBy", Value = "vote_average.desc" }
                    ]
                }
            ]);

        await service.GetWidgetItems(
            Guid.NewGuid(), widgetType, additionalData: "instance-b", sectionKey: null,
            startIndex: 0, limit: 20, CancellationToken.None);

        Assert.NotNull(capturedPayload);
        Assert.Equal("vote_average.desc", capturedPayload!.ExtraParams!["sortBy"]);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private WidgetService BuildService(
        IWidget[] registeredWidgets,
        WidgetConfig[] globalWidgets,
        ILocalizationService? localizationService = null,
        IPersonalizationState? personalizationState = null)
    {
        var registry = new WidgetRegistry();
        foreach (var w in registeredWidgets)
        {
            registry.Register(w);
        }

        _userConfigStoreMock
            .Setup(s => s.GetUserConfiguration(It.IsAny<Guid>()))
            .Returns((UserConfiguration?)null);

        var config = new PluginConfiguration { Widgets = globalWidgets };

        var layoutResolver = new WidgetLayoutResolver(
            registry,
            _userConfigStoreMock.Object,
            localizationService ?? new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            () => config,
            NullLogger<WidgetLayoutResolver>.Instance);

        return new WidgetService(
            registry,
            _sessionCache,
            layoutResolver,
            () => config,
            NullLogger<WidgetService>.Instance,
            personalizationState);
    }

    private static IWidget MakeWidget(string widgetType, int totalRecordCount)
    {
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.DefaultDisplayName).Returns(widgetType);

        mock.Setup(w => w.Resolve(
                It.IsAny<Guid>(),
                It.IsAny<WidgetInstanceConfig>(),
                It.IsAny<int>()))
            .Returns(() => mock.Object);

        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WidgetResult([], totalRecordCount));

        mock.Setup(w => w.GetDescriptor())
            .Returns(new WidgetDescriptor { WidgetType = widgetType });

        return mock.Object;
    }

    // -------------------------------------------------------------------------
    // HasMore (TODO_V4.md 2.3: the home lazy loader stops on it instead of fetching empty pages)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(14, 0, 14, false)]
    [InlineData(20, 0, 20, false)]
    [InlineData(21, 0, 20, true)]
    [InlineData(21, 1, 1, false)]
    [InlineData(14, 1, 0, false)]
    public void Paginate_ReportsWhetherALaterPageExists(int total, int page, int expectedCount, bool expectedHasMore)
    {
        var all = Enumerable.Range(0, total).Select(i => new WidgetDescriptor { WidgetType = "w" + i, Order = i }).ToList();

        var result = WidgetService.Paginate(all, page);

        Assert.Equal(expectedCount, result.Sections.Count);
        Assert.Equal(expectedHasMore, result.HasMore);
    }

    public void Dispose() => _sessionCache.Dispose();

    [Theory]
    [InlineData(100000, 0, 7, 0)]
    [InlineData(0, -5, 1, 0)]
    [InlineData(5, 3, 5, 3)]
    public async Task GetWidgetItems_BoundsLimitByMaxItemsAndStartIndexAtZero(int limit, int startIndex, int expectedLimit, int expectedStart)
    {
        const string widgetType = "jux.test.bounded";
        WidgetPayload? captured = null;
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        mock.Setup(w => w.GetItemsAsync(It.IsAny<WidgetPayload>(), It.IsAny<CancellationToken>()))
            .Callback<WidgetPayload, CancellationToken>((payload, _) => captured = payload)
            .ReturnsAsync(new WidgetResult([], 0));
        var service = BuildService(
            registeredWidgets: [mock.Object],
            globalWidgets: [new WidgetConfig { WidgetType = widgetType, Enabled = true, MaxItems = 7 }]);

        await service.GetWidgetItems(Guid.NewGuid(), widgetType, additionalData: null, sectionKey: null, startIndex, limit, CancellationToken.None);

        Assert.Equal(expectedLimit, captured!.Limit);
        Assert.Equal(expectedStart, captured.StartIndex);
    }
}
