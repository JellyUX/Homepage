using System.Net;
using System.Net.Http.Json;
using Jellyfin.Plugin.JuxHomepage.Rewards;
using Jellyfin.Plugin.JuxHomepage.Rewards.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Rewards;

public sealed class WikidataApiClientTests
{
    // -------------------------------------------------------------------------
    // Successful call / deserialization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAwardWinnersAsync_SuccessfulResponse_DeserializesResults()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(SparqlResponse(
            Row("Q102", "Oppenheimer", "Academy Award for Best Picture", "2024-03-10T00:00:00Z", "tt15398776"))));

        var client = BuildClient(handler);

        var result = await client.GetAwardWinnersAsync(
            new RewardsFilter { CategoryQid = "Q102427" },
            CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(102, result[0].Id);
        Assert.Equal("Q102", result[0].FilmQid);
        Assert.Equal("Oppenheimer", result[0].FilmLabel);
        Assert.Equal("tt15398776", result[0].ImdbId);
        Assert.Equal(2024, result[0].PointInTimeYear);
        Assert.Equal(1, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // No filter configured
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAwardWinnersAsync_NoCeremonyOrCategory_ReturnsEmptyWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(SparqlResponse()));
        var client = BuildClient(handler);

        var result = await client.GetAwardWinnersAsync(new RewardsFilter(), CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Network failure -- no retry (deliberately simpler than TMDbApiClient, see WikidataApiClient's
    // resilience policy doc comment)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAwardWinnersAsync_NetworkFailure_ReturnsEmptyWithoutRetry()
    {
        var handler = new StubHttpMessageHandler(() => throw new HttpRequestException("network down"));
        var client = BuildClient(handler);

        var result = await client.GetAwardWinnersAsync(
            new RewardsFilter { CeremonyQid = "Q19020" },
            CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchEntitiesAsync_CallerCancels_Throws()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(() =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });
        var client = BuildClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchEntitiesAsync("Best Picture", cts.Token));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchEntitiesAsync_HttpClientTimeout_ReturnsEmpty()
    {
        // HttpClient.Timeout surfaces as a TaskCanceledException the caller never asked for: still a failure.
        var handler = new StubHttpMessageHandler(() => throw new TaskCanceledException());
        var client = BuildClient(handler);

        var result = await client.SearchEntitiesAsync("Best Picture", CancellationToken.None);

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // 429 rate limit -- logged and skipped, never retried (see Wikidata 2026 access policy research)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAwardWinnersAsync_RateLimited_ReturnsEmptyWithoutRetry()
    {
        var handler = new StubHttpMessageHandler(() => new HttpResponseMessage((HttpStatusCode)429));
        var client = BuildClient(handler);

        var result = await client.GetAwardWinnersAsync(
            new RewardsFilter { CeremonyQid = "Q19020" },
            CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(1, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Entity search (admin autocomplete)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchEntitiesAsync_SuccessfulResponse_DeserializesResults()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new WikidataSearchResponse
        {
            Search =
            [
                new WikidataSearchEntity { Id = "Q102427", Label = "Academy Award for Best Picture", Description = "annual award" }
            ]
        }));

        var client = BuildClient(handler);

        var result = await client.SearchEntitiesAsync("Best Picture", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Q102427", result[0].Id);
        Assert.Equal("Academy Award for Best Picture", result[0].Label);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchEntitiesAsync_BlankQuery_ReturnsEmptyWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler(() => JsonResponse(new WikidataSearchResponse()));
        var client = BuildClient(handler);

        var result = await client.SearchEntitiesAsync("   ", CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static WikidataApiClient BuildClient(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("Wikidata")).Returns(httpClient);

        return new WikidataApiClient(factoryMock.Object, NullLogger<WikidataApiClient>.Instance);
    }

    private static WikidataSparqlResponse SparqlResponse(params Dictionary<string, WikidataSparqlBinding>[] rows) =>
        new() { Results = new WikidataSparqlResults { Bindings = rows } };

    private static Dictionary<string, WikidataSparqlBinding> Row(
        string filmQid,
        string filmLabel,
        string awardLabel,
        string pointInTime,
        string imdbId) => new()
        {
            ["film"] = new WikidataSparqlBinding { Type = "uri", Value = $"http://www.wikidata.org/entity/{filmQid}" },
            ["filmLabel"] = new WikidataSparqlBinding { Type = "literal", Value = filmLabel },
            ["awardLabel"] = new WikidataSparqlBinding { Type = "literal", Value = awardLabel },
            ["pointInTime"] = new WikidataSparqlBinding { Type = "literal", Value = pointInTime },
            ["imdbId"] = new WikidataSparqlBinding { Type = "literal", Value = imdbId }
        };

    private static HttpResponseMessage JsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    // Hand-written HttpMessageHandler stub (no Moq.Protected dependency), mirroring
    // TMDbApiClientTests.StubHttpMessageHandler: each call to SendAsync dequeues and invokes the next
    // responder, allowing a responder to either return a response or throw (e.g.
    // HttpRequestException) to simulate a network failure.
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public StubHttpMessageHandler(params Func<HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpResponseMessage>>(responses);
        }

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Yield();
            var responder = _responses.Dequeue();
            return responder();
        }
    }
}
