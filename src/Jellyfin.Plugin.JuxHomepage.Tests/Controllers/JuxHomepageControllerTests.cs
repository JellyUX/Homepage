using System.Reflection;
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
            Mock.Of<MediaBrowser.Controller.Dto.IDtoService>());

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
    public void GetMeta_ReturnsWidgetCategoryNamesInEnumOrder()
    {
        var controller = BuildController();

        var result = controller.GetMeta();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var meta = Assert.IsType<JuxHomepageController.PluginMeta>(ok.Value);
        Assert.Equal(Enum.GetNames<WidgetCategory>(), meta.WidgetCategories);
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

    public static TheoryData<string, string, string> WebResourceActions => new()
    {
        { nameof(JuxHomepageController.GetScript), "jux-homepage.js", JavaScript },
        { nameof(JuxHomepageController.GetCoreScript), "jux-core.js", JavaScript },
        { nameof(JuxHomepageController.GetUiScript), "jux-ui.js", JavaScript },
        { nameof(JuxHomepageController.GetUiStylesheet), "jux-ui.css", Css },
        { nameof(JuxHomepageController.GetTabInjectorScript), "jux-tab-injector.js", JavaScript },
        { nameof(JuxHomepageController.GetWatchlistScript), "jux-watchlist.js", JavaScript },
        { nameof(JuxHomepageController.GetCardHooksScript), "jux-card-hooks.js", JavaScript },
        { nameof(JuxHomepageController.GetProgressScript), "jux-progress.js", JavaScript },
        { nameof(JuxHomepageController.GetHistoryScript), "jux-history.js", JavaScript },
        { nameof(JuxHomepageController.GetStatisticsScript), "jux-statistics.js", JavaScript },
        { nameof(JuxHomepageController.GetSeriesFlattenScript), "jux-series-flatten.js", JavaScript },
        { nameof(JuxHomepageController.GetCollectionsScript), "jux-collections.js", JavaScript },
        { nameof(JuxHomepageController.GetBreadcrumbScript), "jux-breadcrumb.js", JavaScript },
        { nameof(JuxHomepageController.GetStylesheet), "jux-homepage.css", Css },
        { nameof(JuxHomepageController.GetConfigStylesheet), "config.css", Css },
    };

    [Fact]
    public void WebResourceActions_CoverEveryAnonymousResourceEndpoint()
    {
        // Guards against a new asset endpoint escaping the theory below.
        var served = typeof(JuxHomepageController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Where(m => m.GetCustomAttribute<HttpGetAttribute>()?.Template is { } t
                && (t.EndsWith(".js", StringComparison.Ordinal) || t.EndsWith(".css", StringComparison.Ordinal)))
            .Select(m => m.Name)
            .Order();

        Assert.Equal(WebResourceActions.Select(row => (string)row[0]).Order(), served);
    }

    [Theory]
    [MemberData(nameof(WebResourceActions))]
    public void WebResource_ServesTheEmbeddedFileWithItsContentType(string action, string fileName, string contentType)
    {
        var result = InvokeWebResource(action, query: null, out _);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(contentType, file.ContentType);
        using var reader = new StreamReader(file.FileStream);
        var expected = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src", "Jellyfin.Plugin.JuxHomepage", "Web", fileName));
        Assert.Equal(expected, reader.ReadToEnd());
    }

    // Only the current content key pins a response for a year; a missing, empty or outdated key
    // (an index.html cached before an update) must be revalidated hourly.
    [Theory]
    [InlineData("CURRENT", "public, max-age=31536000, immutable")]
    [InlineData(null, "public, max-age=3600")]
    [InlineData("", "public, max-age=3600")]
    [InlineData("000000000000", "public, max-age=3600")]
    public void WebResource_CacheControl_DependsOnTheVersionQuery(string? version, string expected)
    {
        var query = version switch
        {
            null => null,
            "CURRENT" => $"?v={ClientAssetVersion.Current}",
            _ => $"?v={version}",
        };

        foreach (var action in WebResourceActions.Select(row => (string)row[0]))
        {
            InvokeWebResource(action, query, out var response);

            Assert.True(
                response.Headers.CacheControl.ToString() == expected,
                $"{action} returned Cache-Control '{response.Headers.CacheControl}' for query '{query}'");
        }
    }

    private static IActionResult InvokeWebResource(string action, string? query, out HttpResponse response)
    {
        var httpContext = new DefaultHttpContext();
        if (query is not null)
        {
            httpContext.Request.QueryString = new QueryString(query);
        }

        var controller = BuildController();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        response = httpContext.Response;
        return (IActionResult)typeof(JuxHomepageController).GetMethod(action)!.Invoke(controller, null)!;
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
            w => w.GetItems(caller.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
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
            w => w.GetItems(caller.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), 0, WidgetConfig.MaxItemsCeiling, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetWatchlistItems_ServiceThrows_AnswersAnExplicit500()
    {
        var caller = TestMocks.DefaultUser();
        var watchlistMock = new Mock<IWatchlistService>();
        watchlistMock
            .Setup(w => w.GetItems(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
        SessionCache? sessionCache = null)
    {
        var authContextMock = new Mock<IAuthorizationContext>();
        authContextMock
            .Setup(a => a.GetAuthorizationInfo(It.IsAny<HttpContext>()))
            .ReturnsAsync(new AuthorizationInfo { User = caller, IsApiKey = false });

        var controller = BuildController(
            userManagerMock: TestMocks.UserManagerReturning(caller),
            watchlistServiceMock: watchlistServiceMock,
            authContextMock: authContextMock,
            sessionCache: sessionCache);

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
        SessionCache? sessionCache = null)
    {
        var registry = new WidgetRegistry();
        var widgetService = new WidgetService(
            registry,
            sessionCache ?? new SessionCache(),
            new WidgetLayoutResolver(
                registry,
                Mock.Of<IUserConfigurationStore>(),
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
