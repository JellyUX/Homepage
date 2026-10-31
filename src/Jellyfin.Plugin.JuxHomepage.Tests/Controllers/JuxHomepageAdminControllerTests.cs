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

// Covers the routes of the admin page: the widget-values guard (400/404), the TMDb and Wikidata lookups and their
// deadline, the TMDb status and warning codes, and the widget catalog.
public sealed class JuxHomepageAdminControllerTests
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
        time.Advance(JuxHomepageAdminController.InteractiveDeadline - TimeSpan.FromMilliseconds(1));
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
        time.Advance(JuxHomepageAdminController.InteractiveDeadline);

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
        time.Advance(JuxHomepageAdminController.InteractiveDeadline);
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
        var statuses = Assert.IsAssignableFrom<IReadOnlyList<JuxHomepageAdminController.TMDbCacheStatusDto>>(ok.Value);
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

        var statuses = Assert.IsAssignableFrom<IReadOnlyList<JuxHomepageAdminController.TMDbCacheStatusDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
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

        Assert.Equal(["TMDbInvalidKey", "TMDbUnreachable"], JuxHomepageAdminController.TMDbWarningCodes(tmdbCacheServiceMock.Object));
        Assert.Empty(JuxHomepageAdminController.TMDbWarningCodes(new Mock<ITMDbCacheService>().Object));
    }

    [Fact]
    public void GetTMDbItems_UnhandledCacheType_ReturnsNoItemAndLogsWarning()
    {
        // Guards against the switch silently reporting 0 for a future TMDbCacheType value that was
        // added to the enum but never wired into this switch (Phase 9 fix, TODO_V2.md).
        var loggerMock = new Mock<ILogger<JuxHomepageAdminController>>();
        var controller = BuildController(logger: loggerMock.Object);

        var method = typeof(JuxHomepageAdminController).GetMethod(
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
        var catalog = Assert.IsType<JuxHomepageAdminController.WidgetCatalog>(ok.Value);
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

        var catalog = Assert.IsType<JuxHomepageAdminController.WidgetCatalog>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var row = Assert.Single(catalog.Widgets).DefaultRow!;
        Assert.Equal("jux.connected.discover-movies", row.WidgetType);
        Assert.True(row.Enabled);
        Assert.Equal(1, row.MinItems);
        Assert.Equal(20, row.MaxItems);
        Assert.Equal(WidgetViewMode.Portrait, row.ViewMode);
        Assert.Equal("minVotes=50", string.Join(';', row.ExtraParams.Select(p => $"{p.Key}={p.Value}")));
    }

    private static JuxHomepageAdminController BuildController(
        Mock<IWidgetRegistry>? registryMock = null,
        Mock<IUserManager>? userManagerMock = null,
        Mock<ITMDbCacheService>? tmdbCacheServiceMock = null,
        Mock<ITMDbApiClient>? tmdbApiClientMock = null,
        ILogger<JuxHomepageAdminController>? logger = null,
        ITMDbApiClient? tmdbApiClient = null,
        IWikidataApiClient? wikidataApiClient = null,
        TimeProvider? timeProvider = null)
    {
        return new JuxHomepageAdminController(
            (registryMock ?? new Mock<IWidgetRegistry>()).Object,
            (userManagerMock ?? new Mock<IUserManager>()).Object,
            (tmdbCacheServiceMock ?? new Mock<ITMDbCacheService>()).Object,
            tmdbApiClient ?? (tmdbApiClientMock ?? new Mock<ITMDbApiClient>()).Object,
            Mock.Of<IRewardsCacheService>(),
            wikidataApiClient ?? Mock.Of<IWikidataApiClient>(),
            new LocalizationService(new Dictionary<string, IReadOnlyDictionary<string, string>>()),
            new RequestUserResolver(new Mock<IAuthorizationContext>().Object),
            logger ?? NullLogger<JuxHomepageAdminController>.Instance,
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
