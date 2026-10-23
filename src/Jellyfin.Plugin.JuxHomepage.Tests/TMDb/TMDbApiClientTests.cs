using System.Net;
using System.Net.Http.Json;
using System.Text;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.TMDb;

public sealed class TMDbApiClientTests
{
    private const string V3Key = "abcdef0123456789abcdef0123456789";

    // -------------------------------------------------------------------------
    // Successful call / deserialization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_SuccessfulResponse_DeserializesResults()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new TMDbTrending<TMDbMovie>
        {
            Page = 1,
            Results = [new TMDbMovie { Id = 27205, Title = "Inception" }],
            TotalPages = 1,
            TotalResults = 1
        }));

        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(27205, result[0].Id);
        Assert.Equal("Inception", result[0].Title);
        Assert.Equal(1, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Missing key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_ApiKeysWithoutTMDbKey_ReturnsEmptyWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new TMDbTrending<TMDbMovie>()));
        var client = BuildClient(handler, apiKey: null);

        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Invalid key (HTTP 401)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_Unauthorized_ReturnsEmptyWithoutRetry()
    {
        var handler = new StubHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(1, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Malformed JSON response (Phase 1.2 of TODO_V3.md)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_MalformedJsonBody_ReturnsEmptyWithoutRetryOrThrowing()
    {
        var handler = new StubHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ this is not valid json", Encoding.UTF8, "application/json")
            });

        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Empty(result);
        // Unlike a network failure, a schema/parse problem is not retried -- one attempt only.
        Assert.Equal(1, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Network failure retry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_TransientNetworkFailureThenSuccess_RetriesOnceAndSucceeds()
    {
        var handler = new StubHttpMessageHandler(
            () => throw new HttpRequestException("network down"),
            () => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = 1, Title = "Recovered" }]
            }));

        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Recovered", result[0].Title);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetTrendingMoviesAsync_TwoConsecutiveNetworkFailures_ReturnsEmptyWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler(
            () => throw new HttpRequestException("network down"),
            () => throw new HttpRequestException("still down"));

        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetTrendingMoviesAsync_CallerCancels_ThrowsWithoutRetryOrCountingAFailure()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(
            () =>
            {
                cts.Cancel();
                throw new TaskCanceledException();
            },
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => JsonResponse(new TMDbTrending<TMDbMovie> { Results = [new TMDbMovie { Id = 1, Title = "Served" }] }));

        var client = BuildClient(handler, V3Key);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetTrendingMoviesAsync(1, cts.Token));
        Assert.Equal(1, handler.CallCount);

        // A cancelled call is not a TMDb failure: two failed calls after it leave the circuit closed
        // (it opens on the third), so the next call still reaches TMDb.
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(6, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Failure recorder (FIAB-10.c)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_RateLimited_RecordsItWithoutRetry()
    {
        var handler = new StubHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => JsonResponse(new TMDbTrending<TMDbMovie> { Results = [new TMDbMovie { Id = 1 }] }));
        var client = BuildClient(handler, V3Key);

        using (var recorder = TMDbFailureRecorder.Begin())
        {
            Assert.Empty(await client.GetTrendingMoviesAsync(1, CancellationToken.None));
            Assert.Equal(TMDbFailureKind.RateLimited, recorder.LastFailure);
        }

        Assert.Equal(1, handler.CallCount);

        // Not counted toward the circuit: two failed calls after it leave it closed.
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        Assert.Single(await client.GetTrendingMoviesAsync(1, CancellationToken.None));
    }

    public static TheoryData<Func<HttpResponseMessage>, TMDbFailureKind> FailureKinds => new()
    {
        { () => new HttpResponseMessage(HttpStatusCode.Unauthorized), TMDbFailureKind.InvalidKey },
        { () => throw new TaskCanceledException("HttpClient.Timeout"), TMDbFailureKind.Timeout },
        { () => throw new HttpRequestException("network down"), TMDbFailureKind.Unreachable },
        { () => new HttpResponseMessage(HttpStatusCode.InternalServerError), TMDbFailureKind.Unreachable },
    };

    [Theory]
    [MemberData(nameof(FailureKinds))]
    public async Task Recorder_KeepsWhyTheRequestGaveNoData(Func<HttpResponseMessage> respond, TMDbFailureKind expected)
    {
        var client = BuildClient(new StubHttpMessageHandler(respond, respond), V3Key);

        using var recorder = TMDbFailureRecorder.Begin();
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Equal(expected, recorder.LastFailure);
    }

    [Fact]
    public async Task Recorder_SeesOnlyTheRequestsMadeWhileItIsOpen()
    {
        var unauthorized = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var client = BuildClient(new StubHttpMessageHandler(unauthorized, unauthorized), V3Key);

        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        TMDbFailureKind? outer;
        using (var recorder = TMDbFailureRecorder.Begin())
        {
            using (TMDbFailureRecorder.Begin())
            {
                await client.GetTrendingMoviesAsync(1, CancellationToken.None);
            }

            outer = recorder.LastFailure;
        }

        Assert.Null(outer);
    }

    // -------------------------------------------------------------------------
    // Circuit breaker (Phase 3.2 of TODO_V2.md)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_SustainedFailures_OpensCircuitAfterThreshold()
    {
        // 3 logical failed calls, each retried once internally = 6 failing HTTP attempts.
        var handler = new StubHttpMessageHandler(
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"));

        var client = BuildClient(handler, V3Key);

        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        Assert.Equal(6, handler.CallCount);

        // The circuit should now be open: a further call makes no new HTTP request at all.
        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(6, handler.CallCount);
    }

    [Fact]
    public async Task GetTrendingMoviesAsync_CircuitOpen_AllowsProbeRequestAfterWindowElapses()
    {
        var handler = new StubHttpMessageHandler(
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = 1, Title = "Recovered" }]
            }));

        var timeProvider = new ManualTimeProvider();
        var client = BuildClient(handler, V3Key, timeProvider);

        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        Assert.Equal(6, handler.CallCount);

        // Confirm the circuit is genuinely open before advancing time.
        await client.GetTrendingMoviesAsync(1, CancellationToken.None);
        Assert.Equal(6, handler.CallCount);

        timeProvider.UtcNowValue = timeProvider.UtcNowValue.AddMinutes(6);

        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Recovered", result[0].Title);
        Assert.Equal(7, handler.CallCount);
    }

    [Fact]
    public async Task GetTrendingMoviesAsync_SuccessBetweenFailures_ResetsConsecutiveFailureCount()
    {
        var handler = new StubHttpMessageHandler(
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => JsonResponse(new TMDbTrending<TMDbMovie> { Results = [] }),
            () => throw new HttpRequestException("down"),
            () => throw new HttpRequestException("down"),
            () => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = 9, Title = "Still works" }]
            }));

        var client = BuildClient(handler, V3Key);

        await client.GetTrendingMoviesAsync(1, CancellationToken.None); // fails twice
        await client.GetTrendingMoviesAsync(1, CancellationToken.None); // succeeds, resets counter
        await client.GetTrendingMoviesAsync(1, CancellationToken.None); // fails twice again
        var result = await client.GetTrendingMoviesAsync(1, CancellationToken.None); // circuit still closed

        Assert.Single(result);
        Assert.Equal("Still works", result[0].Title);
        Assert.Equal(6, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Multi-page fetching
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTrendingMoviesAsync_MultiplePages_ConcatenatesResults()
    {
        var handler = new StubHttpMessageHandler(
            () => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = 1, Title = "Page 1 Movie" }]
            }),
            () => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = 2, Title = "Page 2 Movie" }]
            }));

        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(2, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Page 1 Movie", result[0].Title);
        Assert.Equal("Page 2 Movie", result[1].Title);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetTrendingMoviesAsync_PageReturnsEmpty_StopsFetchingFurtherPages()
    {
        var handler = new StubHttpMessageHandler(
            () => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = 1, Title = "Only Movie" }]
            }),
            () => JsonResponse(new TMDbTrending<TMDbMovie> { Results = [] }));

        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(5, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetTrendingMoviesAsync_PagesRequestClampedToMax()
    {
        var responders = Enumerable.Range(0, 10)
            .Select(i => (Func<HttpResponseMessage>)(() => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = i, Title = "M" + i }]
            })))
            .ToArray();
        var handler = new StubHttpMessageHandler(responders);

        var client = BuildClient(handler, V3Key);

        var result = await client.GetTrendingMoviesAsync(999, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal(5, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Discover-related endpoints
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMovieGenresAsync_SuccessfulResponse_DeserializesGenres()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new TMDbGenreListResponse
        {
            Genres = [new TMDbGenre { Id = 28, Name = "Action" }]
        }));

        var client = BuildClient(handler, V3Key);

        var result = await client.GetMovieGenresAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(28, result[0].Id);
        Assert.Equal("Action", result[0].Name);
    }

    [Fact]
    public async Task SearchPersonAsync_SuccessfulResponse_DeserializesResults()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new TMDbTrending<TMDbSearchResult>
        {
            Results = [new TMDbSearchResult { Id = 6193, Name = "Leonardo DiCaprio" }]
        }));

        var client = BuildClient(handler, V3Key);

        var result = await client.SearchPersonAsync("Leonardo", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Leonardo DiCaprio", result[0].Name);
    }

    [Fact]
    public async Task DiscoverMoviesAsync_DefaultFilter_FetchesConfiguredPages()
    {
        var handler = new StubHttpMessageHandler(
            () => JsonResponse(new TMDbTrending<TMDbMovie>
            {
                Results = [new TMDbMovie { Id = 1, Title = "Discovered" }]
            }),
            () => JsonResponse(new TMDbTrending<TMDbMovie> { Results = [] }));

        var client = BuildClient(handler, V3Key);
        var filter = new TMDbDiscoverFilter { Pages = 2 };

        var result = await client.DiscoverMoviesAsync(filter, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Discovered", result[0].Title);
        Assert.Equal(2, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Top Rated via discover (Phase 3.4 of TODO_V2.md)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTopRatedMoviesAsync_UsesDiscoverEndpointWithConfiguredVoteCountThreshold()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new TMDbTrending<TMDbMovie> { Results = [] }));
        var client = BuildClient(handler, V3Key);

        await client.GetTopRatedMoviesAsync(1, 500, CancellationToken.None);

        Assert.NotNull(handler.LastRequestUri);
        var uri = handler.LastRequestUri!.ToString();
        Assert.Contains("discover/movie", uri, StringComparison.Ordinal);
        Assert.Contains("sort_by=vote_average.desc", uri, StringComparison.Ordinal);
        Assert.Contains("vote_count.gte=500", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopRatedShowsAsync_UsesDiscoverEndpointWithConfiguredVoteCountThreshold()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new TMDbTrending<TMDbShow> { Results = [] }));
        var client = BuildClient(handler, V3Key);

        await client.GetTopRatedShowsAsync(1, 500, CancellationToken.None);

        Assert.NotNull(handler.LastRequestUri);
        var uri = handler.LastRequestUri!.ToString();
        Assert.Contains("discover/tv", uri, StringComparison.Ordinal);
        Assert.Contains("sort_by=vote_average.desc", uri, StringComparison.Ordinal);
        Assert.Contains("vote_count.gte=500", uri, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // External ids: "no IMDb id" is told apart from a failure (Annexe A "TMDb")
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("movie")]
    [InlineData("tv")]
    public async Task GetExternalIdsAsync_TitleWithoutImdbId_ReturnsEmpty(string kind)
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new TMDbExternalIds { ImdbId = null }));
        var client = BuildClient(handler, V3Key);

        var imdbId = kind == "movie"
            ? await client.GetMovieExternalIdsAsync(1, CancellationToken.None)
            : await client.GetShowExternalIdsAsync(1, CancellationToken.None);

        Assert.Equal(string.Empty, imdbId);
    }

    [Fact]
    public async Task GetMovieExternalIdsAsync_RequestFailed_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = BuildClient(handler, V3Key);

        Assert.Null(await client.GetMovieExternalIdsAsync(1, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TMDbApiClient BuildClient(StubHttpMessageHandler handler, string? apiKey, TimeProvider? timeProvider = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("TMDb")).Returns(httpClient);

        return new TMDbApiClient(
            factoryMock.Object,
            () => new PluginConfiguration { ApiKeys = new ApiKeysConfig { TMDb = apiKey } },
            NullLogger<TMDbApiClient>.Instance,
            timeProvider);
    }

    private static HttpResponseMessage JsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    // Hand-written HttpMessageHandler stub (no Moq.Protected dependency needed): each call to
    // SendAsync dequeues and invokes the next responder, allowing a responder to either return a
    // response or throw (e.g. HttpRequestException) to simulate a network failure.
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public StubHttpMessageHandler(params Func<HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpResponseMessage>>(responses);
        }

        public int CallCount { get; private set; }

        /// <summary>The URI of the most recent request, for tests asserting on query parameters.</summary>
        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            await Task.Yield();
            var responder = _responses.Dequeue();
            return responder();
        }
    }
}
