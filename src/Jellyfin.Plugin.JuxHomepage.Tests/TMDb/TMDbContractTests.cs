using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.TMDb;

// TODO_V2.md Phase 15.2: guards against an undetected TMDb schema drift by replaying a realistic,
// complete discover/movie response through the real TMDbApiClient (not a hand-built C# object
// round-tripped through JsonContent.Create, as TMDbApiClientTests.cs's other tests do -- this one
// exercises literal JSON text instead).
public sealed class TMDbContractTests
{
    // Fixture based on TMDb's public discover/movie documentation example response shape
    // (docs.themoviedb.org/reference/discover-movie), not a live-captured response -- no TMDb API
    // key was available to make a real call for this test (see TODO_V2.md Phase 15 plan). Includes,
    // deliberately, every real field TMDb returns, not just the ones TMDbMovie maps ("adult",
    // "original_language", "original_title", "popularity", "video", "vote_count") -- the point of
    // this test is to confirm System.Text.Json quietly ignores the unmapped ones rather than
    // throwing, which is exactly what a future added/renamed field would also need to do safely.
    private const string DiscoverMovieResponseFixture = """
        {
          "page": 1,
          "results": [
            {
              "adult": false,
              "backdrop_path": "/nAxGnGHOsfzufThz20zgmYrjNfk.jpg",
              "genre_ids": [18, 36],
              "id": 872585,
              "original_language": "en",
              "original_title": "Oppenheimer",
              "overview": "The story of J. Robert Oppenheimer's role in the development of the atomic bomb during World War II.",
              "popularity": 88.339,
              "poster_path": "/8Gxv8gSFCU0XGDykEGv7zR1n2ua.jpg",
              "release_date": "2023-07-19",
              "title": "Oppenheimer",
              "video": false,
              "vote_average": 8.1,
              "vote_count": 9386
            }
          ],
          "total_pages": 41649,
          "total_results": 833025
        }
        """;

    [Fact]
    public async Task DiscoverMoviesAsync_RealTMDbResponseShape_DeserializesKnownFieldsAndIgnoresExtras()
    {
        var handler = new StubHttpMessageHandler(DiscoverMovieResponseFixture);
        var client = BuildClient(handler);

        var result = await client.DiscoverMoviesAsync(new TMDbDiscoverFilter(), CancellationToken.None);

        var movie = Assert.Single(result);
        Assert.Equal(872585, movie.Id);
        Assert.Equal("Oppenheimer", movie.Title);
        Assert.Equal(
            "The story of J. Robert Oppenheimer's role in the development of the atomic bomb during World War II.",
            movie.Overview);
        Assert.Equal("/8Gxv8gSFCU0XGDykEGv7zR1n2ua.jpg", movie.PosterPath);
        Assert.Equal("/nAxGnGHOsfzufThz20zgmYrjNfk.jpg", movie.BackdropPath);
        Assert.Equal(8.1, movie.VoteAverage);
        Assert.Equal([18, 36], movie.GenreIds);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TMDbApiClient BuildClient(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("TMDb")).Returns(httpClient);

        return new TMDbApiClient(
            factoryMock.Object,
            () => new PluginConfiguration { ApiKeys = new ApiKeysConfig { TMDb = "abcdef0123456789abcdef0123456789" } },
            NullLogger<TMDbApiClient>.Instance);
    }

    // Hand-written HttpMessageHandler stub, mirroring TMDbApiClientTests.StubHttpMessageHandler, but
    // returning literal JSON text (via StringContent) rather than a re-serialized C# object -- this
    // test is specifically about replaying real response *text*, not a round-tripped fixture.
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StubHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
                }
            };
        }
    }
}
