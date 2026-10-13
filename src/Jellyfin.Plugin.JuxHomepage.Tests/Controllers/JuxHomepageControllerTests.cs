using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Controllers;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.Library.Models;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Rewards;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Admin;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Controllers;

// Covers the anonymous web resources (content, type, cache headers), the admin widget-values guard
// (400/404), the TMDb search/status endpoints, Included In,
// and the per-user IDOR guard of every endpoint taking a userId (discovered by reflection, so a new
// endpoint is covered without editing this file).
public sealed class JuxHomepageControllerTests
{
    [Fact]
    public void GetWidgetValues_WidgetIsNotAdminWidget_ReturnsBadRequest()
    {
        var nonAdminWidget = new Mock<IWidget>();
        nonAdminWidget.Setup(w => w.WidgetType).Returns("jux.native.continue-watching");

        var registryMock = new Mock<IWidgetRegistry>();
        registryMock.Setup(r => r.GetByType("jux.native.continue-watching")).Returns(nonAdminWidget.Object);

        var controller = BuildController(registryMock: registryMock);

        var result = controller.GetWidgetValues("jux.native.continue-watching", Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void GetWidgetValues_WidgetTypeNotRegistered_ReturnsNotFound()
    {
        var registryMock = new Mock<IWidgetRegistry>();
        registryMock.Setup(r => r.GetByType(It.IsAny<string>())).Returns((IWidget?)null);

        var controller = BuildController(registryMock: registryMock);

        var result = controller.GetWidgetValues("jux.admin.genre", Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void GetWidgetValues_UserNotFound_ReturnsNotFound()
    {
        var adminWidgetMock = new Mock<AdminWidgetBase>(
            Mock.Of<IUserManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<MediaBrowser.Controller.Dto.IDtoService>(),
            null!);

        var registryMock = new Mock<IWidgetRegistry>();
        registryMock.Setup(r => r.GetByType("jux.admin.genre")).Returns(adminWidgetMock.Object);

        var userManagerMock = TestMocks.UserManagerReturningNull();

        var controller = BuildController(registryMock: registryMock, userManagerMock: userManagerMock);

        var result = controller.GetWidgetValues("jux.admin.genre", Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchTMDbPerson_EmptyOrWhitespaceQuery_ReturnsEmptyWithoutCallingApi(string query)
    {
        var tmdbApiClientMock = new Mock<ITMDbApiClient>();

        var controller = BuildController(tmdbApiClientMock: tmdbApiClientMock);

        var result = await controller.SearchTMDbPerson(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AdminWidgetValue>>(ok.Value));
        tmdbApiClientMock.Verify(
            c => c.SearchPersonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GetTMDbStatus_ReturnsOneEntryPerCacheType_WithConfiguredCounts()
    {
        var tmdbCacheServiceMock = new Mock<ITMDbCacheService>();
        tmdbCacheServiceMock.Setup(s => s.GetTrendingMovies()).Returns([new TMDbMovie()]);
        tmdbCacheServiceMock.Setup(s => s.GetTrendingShows()).Returns([new TMDbShow(), new TMDbShow()]);
        tmdbCacheServiceMock.Setup(s => s.GetAiringToday()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetTopRatedMovies()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetTopRatedShows()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetNowPlayingMovies()).Returns([]);

        var controller = BuildController(tmdbCacheServiceMock: tmdbCacheServiceMock);

        var result = controller.GetTMDbStatus();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var statuses = Assert.IsAssignableFrom<IReadOnlyList<JuxHomepageController.TMDbCacheStatusDto>>(ok.Value);
        Assert.Equal(Enum.GetValues<TMDbCacheType>().Length, statuses.Count);
        Assert.Equal(1, statuses.Single(s => s.Type == nameof(TMDbCacheType.TrendingMovies)).ItemCount);
        Assert.Equal(2, statuses.Single(s => s.Type == nameof(TMDbCacheType.TrendingShows)).ItemCount);
    }

    [Fact]
    public void GetTMDbItemCount_UnhandledCacheType_ReturnsZeroAndLogsWarning()
    {
        // Guards against the switch silently reporting 0 for a future TMDbCacheType value that was
        // added to the enum but never wired into this switch (Phase 9 fix, TODO_V2.md).
        var loggerMock = new Mock<ILogger<JuxHomepageController>>();
        var controller = BuildController(logger: loggerMock.Object);

        var method = typeof(JuxHomepageController).GetMethod(
            "GetTMDbItemCount",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (int)method.Invoke(controller, [(TMDbCacheType)999])!;

        Assert.Equal(0, result);
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GetAdminCatalog_ReturnsEveryDescriptorAndTheCategoryNamesInEnumOrder()
    {
        var widget = new Mock<IWidget>();
        widget.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor { WidgetType = "jux.admin.genre" });
        var registryMock = new Mock<IWidgetRegistry>();
        registryMock.Setup(r => r.GetAll()).Returns([widget.Object]);
        var controller = BuildController(registryMock: registryMock);

        var result = controller.GetAdminCatalog();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var catalog = Assert.IsType<JuxHomepageController.WidgetCatalog>(ok.Value);
        Assert.Equal(Enum.GetNames<WidgetCategory>(), catalog.WidgetCategories);
        Assert.Equal("jux.admin.genre", Assert.Single(catalog.Widgets).WidgetType);
    }

    [Theory]
    [InlineData(nameof(JuxHomepageController.GetAdminCatalog))]
    [InlineData(nameof(JuxHomepageController.PreviewSection))]
    public void AdminPageEndpoints_AreRestrictedToAdministrators(string action)
    {
        var policies = typeof(JuxHomepageController)
            .GetMethod(action)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy);

        Assert.Contains(Policies.RequiresElevation, policies);
    }

    [Fact]
    public async Task GetCollectionsForItem_ReturnsRefsFromCache()
    {
        var itemId = Guid.NewGuid();
        var refs = new List<CollectionRef>
        {
            new() { CollectionId = Guid.NewGuid(), CollectionName = "Trilogy" }
        };

        var cacheMock = new Mock<ICollectionsIndexCacheService>();
        cacheMock.Setup(c => c.IsStale()).Returns(false);
        cacheMock.Setup(c => c.GetCollectionsFor(itemId)).Returns(refs);

        var controller = BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: ApiKeyAuth());

        var result = await controller.GetCollectionsForItem(itemId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(refs, ok.Value);
        cacheMock.Verify(c => c.TryAcquireRefreshLock(), Times.Never);
    }

    [Fact]
    public async Task GetCollectionsForItem_EmptyWhenNoMembership()
    {
        var cacheMock = new Mock<ICollectionsIndexCacheService>();
        cacheMock.Setup(c => c.IsStale()).Returns(false);
        cacheMock.Setup(c => c.GetCollectionsFor(It.IsAny<Guid>())).Returns([]);

        var controller = BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: ApiKeyAuth());

        var result = await controller.GetCollectionsForItem(Guid.NewGuid());

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<CollectionRef>>(ok.Value));
    }

    [Fact]
    public async Task GetCollectionsForItem_StaleIndex_AttemptsBackgroundRefresh()
    {
        var cacheMock = new Mock<ICollectionsIndexCacheService>();
        cacheMock.Setup(c => c.IsStale()).Returns(true);
        cacheMock.Setup(c => c.TryAcquireRefreshLock()).Returns(true);
        cacheMock.Setup(c => c.RunRefreshLockedAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        cacheMock.Setup(c => c.GetCollectionsFor(It.IsAny<Guid>())).Returns([]);

        var controller = BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: ApiKeyAuth());

        var result = await controller.GetCollectionsForItem(Guid.NewGuid());

        // The background refresh is fire-and-forget (Task.Run) -- what matters here is that the
        // endpoint never blocks on it and still returns the current (possibly stale) data immediately.
        Assert.IsType<OkObjectResult>(result.Result);
        cacheMock.Verify(c => c.TryAcquireRefreshLock(), Times.Once);
    }

    [Fact]
    public async Task GetCollectionsForItem_StaleButLockHeld_DoesNotStartSecondRefresh()
    {
        var cacheMock = new Mock<ICollectionsIndexCacheService>();
        cacheMock.Setup(c => c.IsStale()).Returns(true);
        cacheMock.Setup(c => c.TryAcquireRefreshLock()).Returns(false);
        cacheMock.Setup(c => c.GetCollectionsFor(It.IsAny<Guid>())).Returns([]);

        var controller = BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: ApiKeyAuth());

        var result = await controller.GetCollectionsForItem(Guid.NewGuid());

        Assert.IsType<OkObjectResult>(result.Result);
        cacheMock.Verify(c => c.TryAcquireRefreshLock(), Times.Once);
        cacheMock.Verify(c => c.RunRefreshLockedAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCollectionsForItem_UserSession_ReturnsTheUserScopedAnswer()
    {
        var user = TestMocks.DefaultUser();
        var itemId = Guid.NewGuid();
        var visibleRefs = new List<CollectionRef>
        {
            new() { CollectionId = Guid.NewGuid(), CollectionName = "Visible" }
        };

        var cacheMock = new Mock<ICollectionsIndexCacheService>();
        cacheMock.Setup(c => c.IsStale()).Returns(false);
        cacheMock.Setup(c => c.GetCollectionsFor(itemId)).Returns([.. visibleRefs, new CollectionRef { CollectionId = Guid.NewGuid(), CollectionName = "Hidden" }]);
        cacheMock.Setup(c => c.GetCollectionsFor(itemId, user)).Returns(visibleRefs);

        var authContextMock = new Mock<IAuthorizationContext>();
        authContextMock
            .Setup(a => a.GetAuthorizationInfo(It.IsAny<HttpContext>()))
            .ReturnsAsync(new AuthorizationInfo { User = user, IsApiKey = false });

        var controller = BuildController(
            userManagerMock: TestMocks.UserManagerReturning(user),
            collectionsIndexCacheServiceMock: cacheMock,
            authContextMock: authContextMock);

        var result = await controller.GetCollectionsForItem(itemId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(visibleRefs, ok.Value);
    }

    // -------------------------------------------------------------------------
    // Web resources (anonymous)
    // -------------------------------------------------------------------------

    private const string JavaScript = "application/javascript";
    private const string Css = "text/css";

    // Spliced into the native home chunk by TransformationPatches, never served on its own.
    private const string SpliceFragment = "jux-loadsections-inject.js";

    private static string WebDirectory() => Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web");

    private static IEnumerable<string> WebFiles(params string[] extensions) => Directory
        .EnumerateFiles(WebDirectory())
        .Select(Path.GetFileName)
        .OfType<string>()
        .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.Ordinal)))
        .Order(StringComparer.Ordinal);

    // Every script and stylesheet of Web/ except the splice fragment: adding a file there serves it,
    // with no second list to update.
    // Files Jellyfin serves itself as plugin pages (configurationpage?name=...), not through GetAsset.
    private static IEnumerable<string> PluginPageFiles() => ((Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin)))
        .GetPages()
        .Select(p => p.EmbeddedResourcePath[(p.EmbeddedResourcePath.IndexOf(".Web.", StringComparison.Ordinal) + 5)..]);

    private static IEnumerable<string> ServedWebAssetNames() => WebFiles(".js", ".css")
        .Where(f => f != SpliceFragment && !PluginPageFiles().Contains(f));

    public static TheoryData<string> ServedWebAssets => new(ServedWebAssetNames());

    [Fact]
    public void ServedWebAssets_AreDiscovered()
    {
        // Guards against the theories below silently testing nothing.
        Assert.Contains("jux-homepage.js", ServedWebAssetNames());
        Assert.Contains("jux-ui.css", ServedWebAssetNames());
    }

    [Fact]
    public void WebFiles_AreAllEmbedded()
    {
        var embedded = typeof(JuxHomepageController).Assembly.GetManifestResourceNames();

        foreach (var file in WebFiles(".js", ".css", ".html"))
        {
            Assert.Contains($"Jellyfin.Plugin.JuxHomepage.Web.{file}", embedded);
        }
    }

    [Theory]
    [MemberData(nameof(ServedWebAssets))]
    public void GetAsset_ServesTheEmbeddedFileWithItsContentType(string fileName)
    {
        var result = InvokeGetAsset(fileName, query: null, out _);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(fileName.EndsWith(".css", StringComparison.Ordinal) ? Css : JavaScript, file.ContentType);
        using var reader = new StreamReader(file.FileStream);
        Assert.Equal(File.ReadAllText(Path.Combine(WebDirectory(), fileName)), reader.ReadToEnd());
    }

    [Fact]
    public void GetAsset_MatchesTheFileNameCaseInsensitively()
    {
        Assert.IsType<FileStreamResult>(InvokeGetAsset("JUX-HOMEPAGE.JS", query: null, out _));
    }

    [Theory]
    [InlineData(SpliceFragment)]
    [InlineData("config.html")]
    [InlineData("config.js")]
    [InlineData("config.css")]
    [InlineData("jux-native-selectors.json")]
    [InlineData("en.json")]
    [InlineData("Plugin.cs")]
    [InlineData("unknown.js")]
    public void GetAsset_RefusesAnyFileOutsideTheServedAssets(string fileName)
    {
        Assert.IsType<NotFoundResult>(InvokeGetAsset(fileName, query: null, out _));
    }

    // Only the current content key pins a response for a year; a missing, empty or outdated key
    // (an index.html cached before an update) must be revalidated hourly.
    [Theory]
    [InlineData("CURRENT", "public, max-age=31536000, immutable")]
    [InlineData(null, "public, max-age=3600")]
    [InlineData("", "public, max-age=3600")]
    [InlineData("000000000000", "public, max-age=3600")]
    public void GetAsset_CacheControl_DependsOnTheVersionQuery(string? version, string expected)
    {
        var query = version switch
        {
            null => null,
            "CURRENT" => $"?v={ClientAssetVersion.Current}",
            _ => $"?v={version}",
        };

        foreach (var fileName in ServedWebAssetNames())
        {
            InvokeGetAsset(fileName, query, out var response);

            Assert.True(
                response.Headers.CacheControl.ToString() == expected,
                $"{fileName} returned Cache-Control '{response.Headers.CacheControl}' for query '{query}'");
        }
    }

    private static IActionResult InvokeGetAsset(string fileName, string? query, out HttpResponse response)
    {
        var httpContext = new DefaultHttpContext();
        if (query is not null)
        {
            httpContext.Request.QueryString = new QueryString(query);
        }

        var controller = BuildController();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        response = httpContext.Response;
        return controller.GetAsset(fileName);
    }

    // -------------------------------------------------------------------------
    // Per-user endpoints (IDOR)
    // -------------------------------------------------------------------------

    private static IReadOnlyList<MethodInfo> UserScopedActions() => typeof(JuxHomepageController)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m.GetParameters().Any(p => p.Name == "userId" && p.ParameterType == typeof(Guid)))
        .Where(m => m.GetCustomAttributes<AuthorizeAttribute>().All(a => a.Policy != Policies.RequiresElevation))
        .ToList();

    [Fact]
    public void UserScopedActions_AreAllDiscovered()
    {
        // Guards against the reflection probe below silently testing nothing.
        Assert.Equal(
            ["GetMovieHistoryItems", "GetSection", "GetSections", "GetSeriesProgressItems", "GetStatistics", "GetWatchlistIds", "GetWatchlistItems"],
            UserScopedActions().Select(m => m.Name).Order());
    }

    [Fact]
    public async Task UserScopedActions_RegularUserAskingForAnotherUser_IsRefused()
    {
        var caller = TestMocks.DefaultUser();

        foreach (var action in UserScopedActions())
        {
            var status = await InvokeAsUser(action, caller, requestedUserId: Guid.NewGuid());

            Assert.True(status == StatusCodes.Status403Forbidden, $"{action.Name} returned {status}");
        }
    }

    [Fact]
    public async Task UserScopedActions_RegularUserWithoutUserId_ReadsOwnData()
    {
        var caller = TestMocks.DefaultUser();

        foreach (var action in UserScopedActions())
        {
            var status = await InvokeAsUser(action, caller, requestedUserId: Guid.Empty);

            Assert.True(status != StatusCodes.Status403Forbidden, $"{action.Name} refused the caller's own data");
        }
    }

    [Fact]
    public async Task GetWatchlistItems_RegularUserWithoutUserId_QueriesTheTokenUser()
    {
        var caller = TestMocks.DefaultUser();
        var watchlistMock = new Mock<IWatchlistService>();
        var controller = BuildUserController(caller, isAdministrator: false, watchlistServiceMock: watchlistMock);

        await controller.GetWatchlistItems(Guid.Empty);

        watchlistMock.Verify(
            w => w.GetItems(caller.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetWatchlistItems_OutOfRangePaging_IsBounded()
    {
        var caller = TestMocks.DefaultUser();
        var watchlistMock = new Mock<IWatchlistService>();
        var controller = BuildUserController(caller, isAdministrator: false, watchlistServiceMock: watchlistMock);

        await controller.GetWatchlistItems(Guid.Empty, startIndex: -3, limit: 100000);

        watchlistMock.Verify(
            w => w.GetItems(caller.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), 0, WidgetConfig.MaxItemsCeiling, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetWatchlistItems_ServiceThrows_AnswersAnExplicit500()
    {
        var caller = TestMocks.DefaultUser();
        var watchlistMock = new Mock<IWatchlistService>();
        watchlistMock
            .Setup(w => w.GetItems(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("database locked"));
        var controller = BuildUserController(caller, isAdministrator: false, watchlistServiceMock: watchlistMock);

        var response = await controller.GetWatchlistItems(Guid.Empty);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(response.Result).StatusCode);
    }

    [Fact]
    public async Task GetSections_UnknownLanguage_SharesTheEnglishCacheEntry()
    {
        var caller = TestMocks.DefaultUser();
        using var sessionCache = new SessionCache();
        var controller = BuildUserController(caller, isAdministrator: false, sessionCache: sessionCache);

        await controller.GetSections(Guid.Empty, lang: "zz");

        Assert.True(sessionCache.TryGet(caller.Id, "en", TimeSpan.FromMinutes(5), out _));
        Assert.False(sessionCache.TryGet(caller.Id, "zz", TimeSpan.FromMinutes(5), out _));
    }

    [Fact]
    public async Task UserWhoTurnedJellyUxOff_GetsDisabledSectionsAndWatchlistIds()
    {
        var caller = TestMocks.DefaultUser();
        var store = new Mock<IUserConfigurationStore>();
        store.Setup(s => s.GetUserConfiguration(caller.Id)).Returns(new UserConfiguration { UserId = caller.Id, Enabled = false });
        var watchlist = new Mock<IWatchlistService>();
        var controller = BuildUserController(caller, isAdministrator: false, watchlistServiceMock: watchlist, userConfigStore: store.Object);

        var sections = Assert.IsType<OkObjectResult>((await controller.GetSections(Guid.Empty)).Result).Value as SectionsPage;
        var ids = Assert.IsType<OkObjectResult>((await controller.GetWatchlistIds(Guid.Empty)).Result).Value as WatchlistIdsResult;

        Assert.False(sections!.Enabled);
        Assert.Empty(sections.Sections);
        Assert.False(ids!.Enabled);
        Assert.Empty(ids.Ids);
        watchlist.Verify(w => w.GetLikedItemIds(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<int?> InvokeAsUser(MethodInfo action, User caller, Guid requestedUserId)
    {
        var controller = BuildUserController(caller, isAdministrator: false);
        var args = action.GetParameters()
            .Select(p => p.Name == "userId" ? requestedUserId
                : p.ParameterType == typeof(string) ? (p.HasDefaultValue ? p.DefaultValue : "jux.test")
                : p.HasDefaultValue ? p.DefaultValue
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
            .ToArray();

        var task = (Task)action.Invoke(controller, args)!;
        await task;
        var actionResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var result = actionResult.GetType().GetProperty("Result")!.GetValue(actionResult);
        return (result as IStatusCodeActionResult)?.StatusCode;
    }

    private static JuxHomepageController BuildUserController(
        User caller,
        bool isAdministrator,
        Mock<IWatchlistService>? watchlistServiceMock = null,
        SessionCache? sessionCache = null,
        IUserConfigurationStore? userConfigStore = null)
    {
        var authContextMock = new Mock<IAuthorizationContext>();
        authContextMock
            .Setup(a => a.GetAuthorizationInfo(It.IsAny<HttpContext>()))
            .ReturnsAsync(new AuthorizationInfo { User = caller, IsApiKey = false });

        var controller = BuildController(
            userManagerMock: TestMocks.UserManagerReturning(caller),
            watchlistServiceMock: watchlistServiceMock,
            authContextMock: authContextMock,
            sessionCache: sessionCache,
            userConfigStore: userConfigStore);

        var roles = isAdministrator ? new[] { new Claim(ClaimTypes.Role, "Administrator") } : [];
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(roles, "test")) }
        };
        return controller;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Mock<IAuthorizationContext> ApiKeyAuth()
    {
        var mock = new Mock<IAuthorizationContext>();
        mock.Setup(a => a.GetAuthorizationInfo(It.IsAny<HttpContext>()))
            .ReturnsAsync(new AuthorizationInfo { IsApiKey = true });
        return mock;
    }

    private static JuxHomepageController BuildController(
        Mock<IWidgetRegistry>? registryMock = null,
        Mock<IUserManager>? userManagerMock = null,
        Mock<ITMDbCacheService>? tmdbCacheServiceMock = null,
        Mock<ITMDbApiClient>? tmdbApiClientMock = null,
        Mock<IWatchlistService>? watchlistServiceMock = null,
        Mock<ISeriesProgressViewService>? seriesProgressViewServiceMock = null,
        Mock<IMovieHistoryViewService>? movieHistoryViewServiceMock = null,
        Mock<IStatisticsService>? statisticsServiceMock = null,
        Mock<ICollectionsIndexCacheService>? collectionsIndexCacheServiceMock = null,
        Mock<IAuthorizationContext>? authContextMock = null,
        ILogger<JuxHomepageController>? logger = null,
        SessionCache? sessionCache = null,
        IUserConfigurationStore? userConfigStore = null)
    {
        var registry = new WidgetRegistry();
        var widgetService = new WidgetService(
            registry,
            sessionCache ?? new SessionCache(),
            new WidgetLayoutResolver(
                registry,
                userConfigStore ?? Mock.Of<IUserConfigurationStore>(),
                new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
                () => new PluginConfiguration(),
                NullLogger<WidgetLayoutResolver>.Instance),
            () => new PluginConfiguration(),
            NullLogger<WidgetService>.Instance);

        return new JuxHomepageController(
            (registryMock ?? new Mock<IWidgetRegistry>()).Object,
            widgetService,
            (userManagerMock ?? new Mock<IUserManager>()).Object,
            (tmdbCacheServiceMock ?? new Mock<ITMDbCacheService>()).Object,
            (tmdbApiClientMock ?? new Mock<ITMDbApiClient>()).Object,
            Mock.Of<IRewardsCacheService>(),
            Mock.Of<IWikidataApiClient>(),
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            (watchlistServiceMock ?? new Mock<IWatchlistService>()).Object,
            (seriesProgressViewServiceMock ?? new Mock<ISeriesProgressViewService>()).Object,
            (movieHistoryViewServiceMock ?? new Mock<IMovieHistoryViewService>()).Object,
            (statisticsServiceMock ?? new Mock<IStatisticsService>()).Object,
            (collectionsIndexCacheServiceMock ?? new Mock<ICollectionsIndexCacheService>()).Object,
            (authContextMock ?? new Mock<IAuthorizationContext>()).Object,
            logger ?? NullLogger<JuxHomepageController>.Instance);
    }
}
