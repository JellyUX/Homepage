using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
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
    public async Task SearchTMDbPerson_TMDbNeverAnswers_AnswersEmptyAtTheInteractiveDeadline()
    {
        var time = new ManualTimeProvider();
        var handler = new SilentHttpMessageHandler();
        var tmdbApiClient = new TMDbApiClient(
            SilentHttpClientFactory(handler, "TMDb"),
            () => new PluginConfiguration { ApiKeys = new ApiKeysConfig { TMDb = "abcdef0123456789abcdef0123456789" } },
            NullLogger<TMDbApiClient>.Instance,
            time);
        var controller = BuildController(tmdbApiClient: tmdbApiClient, timeProvider: time);

        var search = controller.SearchTMDbPerson("Nolan", CancellationToken.None);
        await handler.FirstRequest.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(JuxHomepageController.InteractiveDeadline - TimeSpan.FromMilliseconds(1));
        Assert.False(search.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        var result = await search.WaitAsync(TimeSpan.FromSeconds(5));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AdminWidgetValue>>(ok.Value));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchRewardsCeremony_WikidataNeverAnswers_AnswersEmptyAtTheInteractiveDeadline()
    {
        var time = new ManualTimeProvider();
        var handler = new SilentHttpMessageHandler();
        var wikidataApiClient = new WikidataApiClient(SilentHttpClientFactory(handler, "Wikidata"), NullLogger<WikidataApiClient>.Instance);
        var controller = BuildController(wikidataApiClient: wikidataApiClient, timeProvider: time);

        var search = controller.SearchRewardsCeremony("Academy", CancellationToken.None);
        await handler.FirstRequest.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(JuxHomepageController.InteractiveDeadline);

        var result = await search.WaitAsync(TimeSpan.FromSeconds(5));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AdminWidgetValue>>(ok.Value));
    }

    [Fact]
    public async Task GetTMDbCountries_RequestAborted_IsNotAnsweredAsADeadline()
    {
        var tmdbApiClientMock = new Mock<ITMDbApiClient>();
        tmdbApiClientMock
            .Setup(c => c.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return [];
            });
        var controller = BuildController(tmdbApiClientMock: tmdbApiClientMock, timeProvider: new ManualTimeProvider());
        using var aborted = new CancellationTokenSource();

        var countries = controller.GetTMDbCountries(aborted.Token);
        aborted.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => countries.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task GetTMDbCountries_RequestAbortedAfterTheDeadline_IsStillCancelled()
    {
        // Both tokens fire before the lookup gives up: the abort wins, as the deadline filter excludes it (R11).
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tmdbApiClientMock = new Mock<ITMDbApiClient>();
        tmdbApiClientMock
            .Setup(c => c.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async token =>
            {
                await gate.Task;
                token.ThrowIfCancellationRequested();
                return [];
            });
        var time = new ManualTimeProvider();
        var controller = BuildController(tmdbApiClientMock: tmdbApiClientMock, timeProvider: time);
        using var aborted = new CancellationTokenSource();

        var countries = controller.GetTMDbCountries(aborted.Token);
        aborted.Cancel();
        time.Advance(JuxHomepageController.InteractiveDeadline);
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => countries.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void GetTMDbStatus_ReturnsOneEntryPerCacheType_WithConfiguredCounts()
    {
        var tmdbCacheServiceMock = new Mock<ITMDbCacheService>();
        tmdbCacheServiceMock.Setup(s => s.GetTrendingMovies()).Returns([new TMDbMovie(), new TMDbMovie { LibraryItemId = Guid.NewGuid() }, new TMDbMovie()]);
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
        var trendingMovies = statuses.Single(s => s.Type == nameof(TMDbCacheType.TrendingMovies));
        Assert.Equal(3, trendingMovies.ItemCount);
        Assert.Equal(1, trendingMovies.MatchedCount);
        var trendingShows = statuses.Single(s => s.Type == nameof(TMDbCacheType.TrendingShows));
        Assert.Equal(2, trendingShows.ItemCount);
        Assert.Equal(0, trendingShows.MatchedCount);
        Assert.Null(trendingShows.LastError);
    }

    [Fact]
    public void GetTMDbStatus_ListThatFailed_SaysWhy()
    {
        var tmdbCacheServiceMock = new Mock<ITMDbCacheService>();
        tmdbCacheServiceMock.Setup(s => s.GetTrendingMovies()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetTrendingShows()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetAiringToday()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetTopRatedMovies()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetTopRatedShows()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetNowPlayingMovies()).Returns([]);
        tmdbCacheServiceMock.Setup(s => s.GetLastError(TMDbCacheType.AiringToday)).Returns(TMDbFailureKind.InvalidKey);
        var failedAt = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
        tmdbCacheServiceMock.Setup(s => s.GetLastErrorUtc(TMDbCacheType.AiringToday)).Returns(failedAt);

        var result = BuildController(tmdbCacheServiceMock: tmdbCacheServiceMock).GetTMDbStatus();

        var statuses = Assert.IsAssignableFrom<IReadOnlyList<JuxHomepageController.TMDbCacheStatusDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("InvalidKey", statuses.Single(s => s.Type == nameof(TMDbCacheType.AiringToday)).LastError);
        Assert.Equal(failedAt, statuses.Single(s => s.Type == nameof(TMDbCacheType.AiringToday)).LastErrorUtc);
        Assert.All(statuses.Where(s => s.Type != nameof(TMDbCacheType.AiringToday)), s => Assert.Null(s.LastError));
    }

    [Fact]
    public void TMDbWarningCodes_OneCodePerKindOfFailure()
    {
        var tmdbCacheServiceMock = new Mock<ITMDbCacheService>();
        tmdbCacheServiceMock.Setup(s => s.GetLastError(TMDbCacheType.TrendingMovies)).Returns(TMDbFailureKind.Unreachable);
        tmdbCacheServiceMock.Setup(s => s.GetLastError(TMDbCacheType.TrendingShows)).Returns(TMDbFailureKind.InvalidKey);
        tmdbCacheServiceMock.Setup(s => s.GetLastError(TMDbCacheType.AiringToday)).Returns(TMDbFailureKind.InvalidKey);

        Assert.Equal(["TMDbInvalidKey", "TMDbUnreachable"], JuxHomepageController.TMDbWarningCodes(tmdbCacheServiceMock.Object));
        Assert.Empty(JuxHomepageController.TMDbWarningCodes(new Mock<ITMDbCacheService>().Object));
    }

    [Fact]
    public void GetTMDbItems_UnhandledCacheType_ReturnsNoItemAndLogsWarning()
    {
        // Guards against the switch silently reporting 0 for a future TMDbCacheType value that was
        // added to the enum but never wired into this switch (Phase 9 fix, TODO_V2.md).
        var loggerMock = new Mock<ILogger<JuxHomepageController>>();
        var controller = BuildController(logger: loggerMock.Object);

        var method = typeof(JuxHomepageController).GetMethod(
            "GetTMDbItems",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (IReadOnlyList<IExternalCacheItem>)method.Invoke(controller, [(TMDbCacheType)999])!;

        Assert.Empty(result);
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
        widget.SetupGet(w => w.DefaultExtraParams).Returns([]);
        var registryMock = new Mock<IWidgetRegistry>();
        registryMock.Setup(r => r.GetAll()).Returns([widget.Object]);
        var controller = BuildController(registryMock: registryMock);

        var result = controller.GetAdminCatalog();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var catalog = Assert.IsType<JuxHomepageController.WidgetCatalog>(ok.Value);
        Assert.Equal(Enum.GetNames<WidgetCategory>(), catalog.WidgetCategories);
        Assert.Equal("jux.admin.genre", Assert.Single(catalog.Widgets).WidgetType);
    }

    [Fact]
    public void GetAdminCatalog_GivesEachDescriptorTheRowTheAdminPageAdds()
    {
        var widget = new Mock<IWidget>();
        widget.SetupGet(w => w.WidgetType).Returns("jux.connected.discover-movies");
        widget.SetupGet(w => w.DefaultExtraParams).Returns([new WidgetExtraParam { Key = "minVotes", Value = "50" }]);
        widget.Setup(w => w.GetDescriptor()).Returns(new WidgetDescriptor
        {
            WidgetType = "jux.connected.discover-movies",
            MinItems = 1,
            ViewMode = WidgetViewMode.Portrait
        });
        var registryMock = new Mock<IWidgetRegistry>();
        registryMock.Setup(r => r.GetAll()).Returns([widget.Object]);

        var result = BuildController(registryMock: registryMock).GetAdminCatalog();

        var catalog = Assert.IsType<JuxHomepageController.WidgetCatalog>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var row = Assert.Single(catalog.Widgets).DefaultRow!;
        Assert.Equal("jux.connected.discover-movies", row.WidgetType);
        Assert.True(row.Enabled);
        Assert.Equal(1, row.MinItems);
        Assert.Equal(20, row.MaxItems);
        Assert.Equal(WidgetViewMode.Portrait, row.ViewMode);
        Assert.Equal("minVotes=50", string.Join(';', row.ExtraParams.Select(p => $"{p.Key}={p.Value}")));
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

        var controller = AsCaller(BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: TestCaller.AuthContext(null)), null);

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

        var controller = AsCaller(BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: TestCaller.AuthContext(null)), null);

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

        var controller = AsCaller(BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: TestCaller.AuthContext(null)), null);

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

        var controller = AsCaller(BuildController(collectionsIndexCacheServiceMock: cacheMock, authContextMock: TestCaller.AuthContext(null)), null);

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

        var controller = AsCaller(
            BuildController(
                userManagerMock: TestMocks.UserManagerReturning(user),
                collectionsIndexCacheServiceMock: cacheMock,
                authContextMock: TestCaller.AuthContext(user)),
            user);

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

    [Fact]
    public void GetAsset_ServesTheGeneratedLabelsScript_WithTheClientKeysOfEveryLanguage()
    {
        // TODO_V4.md 9.4 (F-64): jux-i18n.js is built from Localization/<lang>.json, not embedded as is.
        var result = Assert.IsType<ContentResult>(InvokeGetAsset("jux-i18n.js", query: null, out var response));
        Assert.Equal(JavaScript, result.ContentType);
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl.ToString());

        var script = new LocalizationService(NullLogger<LocalizationService>.Instance).GetClientScript();
        const string Prefix = "window.JuxI18n = ";
        Assert.StartsWith(Prefix, script);
        using var json = JsonDocument.Parse(script[Prefix.Length..script.LastIndexOf(';')]);
        Assert.Equal("Load more", json.RootElement.GetProperty("en").GetProperty("client.list.more").GetString());
        Assert.Equal("Charger plus", json.RootElement.GetProperty("fr").GetProperty("client.list.more").GetString());
        Assert.All(json.RootElement.GetProperty("en").EnumerateObject(), p => Assert.StartsWith("client.", p.Name, StringComparison.Ordinal));
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

        Assert.True(sessionCache.TryGet(caller.Id, "en", TimeSpan.FromMinutes(5), out _, out _));
        Assert.False(sessionCache.TryGet(caller.Id, "zz", TimeSpan.FromMinutes(5), out _, out _));
    }

    [Fact]
    public async Task GetSections_RemembersTheLanguageOfTheUsersOwnHomeScreenOnly()
    {
        // R11 review (DEC-P07-17): an administrator reading another user's home screen in their own
        // language must not change the language that user's home screen is built ahead in.
        var admin = TestMocks.DefaultUser();
        var other = Guid.NewGuid();
        var store = new Mock<IUserConfigurationStore>();

        await BuildUserController(admin, isAdministrator: true, userConfigStore: store.Object).GetSections(other, lang: "de");
        store.Verify(s => s.UpdateUserConfiguration(It.IsAny<Guid>(), It.IsAny<Func<UserConfiguration, bool>>()), Times.Never);

        await BuildUserController(admin, isAdministrator: true, userConfigStore: store.Object).GetSections(Guid.Empty, lang: "de");
        store.Verify(s => s.UpdateUserConfiguration(admin.Id, It.IsAny<Func<UserConfiguration, bool>>()), Times.Once);
    }

    [Fact]
    public async Task UserConfigurationEnabledFalse_GetsDisabledSectionsAndWatchlistIds()
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

    [Fact]
    public async Task UserScopedActions_AdministratorAskingForAnotherUser_IsServed()
    {
        var admin = TestMocks.DefaultUser();

        foreach (var action in UserScopedActions())
        {
            var status = await Invoke(action, BuildUserController(admin, isAdministrator: true), requestedUserId: Guid.NewGuid());

            Assert.True(status != StatusCodes.Status403Forbidden, $"{action.Name} refused an administrator");
        }
    }

    [Fact]
    public async Task UserScopedActions_ApiKey_MustNameAUser()
    {
        foreach (var action in UserScopedActions())
        {
            var withoutUser = await Invoke(action, BuildApiKeyController(), requestedUserId: Guid.Empty);
            var namingAUser = await Invoke(action, BuildApiKeyController(), requestedUserId: Guid.NewGuid());

            Assert.True(withoutUser == StatusCodes.Status403Forbidden, $"{action.Name} returned {withoutUser} to an API key naming no user");
            Assert.True(namingAUser != StatusCodes.Status403Forbidden, $"{action.Name} refused an API key naming a user");
        }
    }

    private static Task<int?> InvokeAsUser(MethodInfo action, User caller, Guid requestedUserId) =>
        Invoke(action, BuildUserController(caller, isAdministrator: false), requestedUserId);

    private static async Task<int?> Invoke(MethodInfo action, JuxHomepageController controller, Guid requestedUserId)
    {
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
        var controller = BuildController(
            userManagerMock: TestMocks.UserManagerReturning(caller),
            watchlistServiceMock: watchlistServiceMock,
            authContextMock: TestCaller.AuthContext(caller),
            sessionCache: sessionCache,
            userConfigStore: userConfigStore);

        return AsCaller(controller, caller, isAdministrator);
    }

    private static JuxHomepageController BuildApiKeyController() => AsCaller(
        BuildController(userManagerMock: TestMocks.UserManagerReturning(TestMocks.DefaultUser()), authContextMock: TestCaller.AuthContext(null)),
        null);

    [Fact]
    public void GetPatchStatus_SendsTheStateByName_AndUnknownBeforeStartupRan()
    {
        var status = new PatchStatus();
        var controller = BuildController();

        var before = Assert.IsType<PatchStatusView>(Assert.IsType<OkObjectResult>(controller.GetPatchStatus(status).Result).Value);
        status.SetLoadSections(LoadSectionsPatchState.HookNotFound);
        status.SetTabsAnchor(false);
        var after = Assert.IsType<PatchStatusView>(Assert.IsType<OkObjectResult>(controller.GetPatchStatus(status).Result).Value);

        Assert.Equal(new PatchStatusView("Unknown", null), before);
        Assert.Equal(new PatchStatusView("HookNotFound", false), after);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Sets the request's caller: a user session, or a server API key when <paramref name="user"/> is null.</summary>
    private static JuxHomepageController AsCaller(JuxHomepageController controller, User? user, bool isAdministrator = false)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = TestCaller.Principal(user, isAdministrator) }
        };
        return controller;
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
        IUserConfigurationStore? userConfigStore = null,
        ITMDbApiClient? tmdbApiClient = null,
        IWikidataApiClient? wikidataApiClient = null,
        TimeProvider? timeProvider = null)
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
            tmdbApiClient ?? (tmdbApiClientMock ?? new Mock<ITMDbApiClient>()).Object,
            Mock.Of<IRewardsCacheService>(),
            wikidataApiClient ?? Mock.Of<IWikidataApiClient>(),
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            (watchlistServiceMock ?? new Mock<IWatchlistService>()).Object,
            (seriesProgressViewServiceMock ?? new Mock<ISeriesProgressViewService>()).Object,
            (movieHistoryViewServiceMock ?? new Mock<IMovieHistoryViewService>()).Object,
            (statisticsServiceMock ?? new Mock<IStatisticsService>()).Object,
            (collectionsIndexCacheServiceMock ?? new Mock<ICollectionsIndexCacheService>()).Object,
            new RequestUserResolver((authContextMock ?? new Mock<IAuthorizationContext>()).Object),
            new HomeLanguageMemory(userConfigStore ?? Mock.Of<IUserConfigurationStore>()),
            logger ?? NullLogger<JuxHomepageController>.Instance,
            timeProvider);
    }

    private static IHttpClientFactory SilentHttpClientFactory(HttpMessageHandler handler, string name)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(name))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.example.invalid/") });
        return factoryMock.Object;
    }

    // An external service that accepts the request and never answers (the audit's black hole, FIAB-10.a): only
    // the request's own token ends it.
    private sealed class SilentHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public Task FirstRequest => _firstRequest.Task;

        public int CallCount => Volatile.Read(ref _callCount);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            _firstRequest.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        }
    }
}
