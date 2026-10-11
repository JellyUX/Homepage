using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jellyfin.Plugin.JuxHomepage.Rewards.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Rewards;

/// <summary>
/// HTTP client for Wikidata's public SPARQL query service (<c>query.wikidata.org/sparql</c>) and its
/// entity search endpoint (<c>www.wikidata.org/w/api.php?action=wbsearchentities</c>).
/// <para>
/// <b>2026 access policy (TODO_V2.md Phase 14 research, verified live against the real service)</b>:
/// no API key or OAuth is required for this occasional, weekly, low-volume use case. Wikimedia does,
/// however, now strictly enforce a compliant <c>User-Agent</c> header (contact info included) --
/// without one, requests are capped at 10/minute ("Unidentified" tier); with one, 200/minute, far more
/// than this plugin ever needs. The User-Agent is set once, at HTTP client registration
/// (<see cref="PluginServiceRegistrator"/>), not per request.
/// </para>
/// <para>
/// <b>Resilience policy -- deliberately simpler than <see cref="TMDb.TMDbApiClient"/>'s retry-once +
/// circuit-breaker</b>: a single attempt per call, no retry, no circuit breaker. Wikidata's own
/// guidance is that a client which does not back off after an HTTP 429 risks a longer-term ban (HTTP
/// 403 on all subsequent requests); given this plugin's very low call volume (a handful of queries per
/// weekly refresh, see <see cref="Tasks.RewardsWeeklyRefreshTask"/>), the safest and simplest policy is
/// to log and move on, letting the next scheduled refresh retry naturally rather than hammering the
/// service. A failed refresh never overwrites the existing cache (see <see cref="RewardsCacheService"/>
/// / <see cref="Caching.DiskJsonCache{T}.WriteUnlessEmpty"/>).
/// </para>
/// </summary>
public sealed class WikidataApiClient : IWikidataApiClient
{
    private const string HttpClientName = "Wikidata";
    private const string SparqlEndpoint = "https://query.wikidata.org/sparql";
    private const string SearchEndpoint = "https://www.wikidata.org/w/api.php";

    /// <summary>Wikidata Q-id for the "film" class, used to exclude person entities co-tagged with the same award (see TODO_V2.md Phase 14 research).</summary>
    private const string FilmQid = "Q11424";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WikidataApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikidataApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create the named "Wikidata" HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public WikidataApiClient(IHttpClientFactory httpClientFactory, ILogger<WikidataApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RewardsWinner>> GetAwardWinnersAsync(RewardsFilter filter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(filter.CeremonyQid) && string.IsNullOrEmpty(filter.CategoryQid))
        {
            _logger.LogDebug("Rewards filter has neither a ceremony nor a category configured; skipping query.");
            return [];
        }

        var query = BuildAwardWinnersQuery(filter);
        var response = await ExecuteSparqlAsync(query, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return [];
        }

        var winners = new List<RewardsWinner>(response.Results.Bindings.Count);
        foreach (var row in response.Results.Bindings)
        {
            var winner = MapRow(row);
            if (winner is not null)
            {
                winners.Add(winner);
            }
        }

        return winners;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WikidataSearchEntity>> SearchEntitiesAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var uri = $"{SearchEndpoint}?action=wbsearchentities&search={Uri.EscapeDataString(query)}&language=en&format=json";
        var response = await ExecuteGetAsync<WikidataSearchResponse>(uri, "application/json", cancellationToken).ConfigureAwait(false);
        return response?.Search ?? [];
    }

    /// <summary>
    /// Builds the SPARQL query text for <see cref="GetAwardWinnersAsync"/>, following the pattern
    /// validated live against <c>query.wikidata.org/sparql</c> during TODO_V2.md Phase 14 research.
    /// <para>
    /// The mandatory <c>?film wdt:P31 wd:Q11424</c> ("instance of" film) triple excludes person
    /// entities: Wikidata records an "award received" (<c>P166</c>) statement on both the winning film
    /// AND on associated producers/directors/actors, so a naive query without this filter returns a mix
    /// of films and people. The same filter conveniently also isolates every film-attached award
    /// category (acting, directing, etc. are frequently also recorded directly on the film item) when
    /// <see cref="RewardsFilter.CeremonyQid"/> alone is used to select an entire ceremony edition.
    /// </para>
    /// <para>
    /// <c>?film wdt:P345 ?imdbId</c> (IMDb ID) is a mandatory (non-optional) triple: an item without a
    /// known IMDb ID can never be cross-referenced against the local library, so it is filtered out at
    /// the source rather than fetched and discarded later.
    /// </para>
    /// </summary>
    private static string BuildAwardWinnersQuery(RewardsFilter filter)
    {
        var extraTriples = new List<string>();

        if (!string.IsNullOrEmpty(filter.CategoryQid))
        {
            extraTriples.Add($"FILTER(?award = wd:{filter.CategoryQid})");
        }

        if (!string.IsNullOrEmpty(filter.CeremonyQid))
        {
            extraTriples.Add($"?award wdt:P31 wd:{filter.CeremonyQid} .");
        }

        if (filter.Year.HasValue)
        {
            extraTriples.Add($"FILTER(YEAR(?pointInTime) = {filter.Year.Value.ToString(CultureInfo.InvariantCulture)})");
        }

        var extra = string.Join('\n', extraTriples);

        return $$"""
            SELECT DISTINCT ?film ?filmLabel ?awardLabel ?pointInTime ?imdbId WHERE {
              ?film wdt:P31 wd:{{FilmQid}} .
              ?film p:P166 ?statement .
              ?statement ps:P166 ?award .
              ?film wdt:P345 ?imdbId .
              OPTIONAL { ?statement pq:P585 ?pointInTime . }
              {{extra}}
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            LIMIT 500
            """;
    }

    private static RewardsWinner? MapRow(IReadOnlyDictionary<string, WikidataSparqlBinding> row)
    {
        if (!row.TryGetValue("film", out var filmBinding) || !row.TryGetValue("imdbId", out var imdbBinding))
        {
            return null;
        }

        var filmQid = ExtractQid(filmBinding.Value);
        if (!TryParseQidNumber(filmQid, out var numericId))
        {
            return null;
        }

        var winner = new RewardsWinner
        {
            Id = numericId,
            FilmQid = filmQid,
            ImdbId = imdbBinding.Value,
            FilmLabel = row.TryGetValue("filmLabel", out var filmLabel) ? filmLabel.Value : filmQid,
            AwardLabel = row.TryGetValue("awardLabel", out var awardLabel) ? awardLabel.Value : string.Empty
        };

        if (row.TryGetValue("pointInTime", out var pointInTime)
            && DateTimeOffset.TryParse(pointInTime.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            winner.PointInTimeYear = parsed.Year;
        }

        return winner;
    }

    private static string ExtractQid(string entityUri)
    {
        var lastSlash = entityUri.LastIndexOf('/');
        return lastSlash >= 0 ? entityUri[(lastSlash + 1)..] : entityUri;
    }

    private static bool TryParseQidNumber(string qid, out int value)
    {
        var numericPart = qid.StartsWith('Q') ? qid[1..] : qid;
        return int.TryParse(numericPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private Task<WikidataSparqlResponse?> ExecuteSparqlAsync(string query, CancellationToken cancellationToken)
    {
        var uri = $"{SparqlEndpoint}?format=json&query={Uri.EscapeDataString(query)}";
        return ExecuteGetAsync<WikidataSparqlResponse>(uri, "application/sparql-results+json", cancellationToken);
    }

    private async Task<T?> ExecuteGetAsync<T>(string uri, string acceptHeader, CancellationToken cancellationToken)
        where T : class
    {
        // The entire body -- including CreateClient, which re-runs the AddHttpClient configuration
        // delegate (User-Agent header parsing) on every call -- is inside this try/catch. This class
        // is documented as never throwing from a public method; narrowing the catch to only
        // HttpRequestException/TaskCanceledException (as TMDbApiClient does) would let any other
        // failure (header parsing, an unexpected JSON shape, etc.) escape uncaught, surface as a bare
        // 500 from the controller, and be silently swallowed by config.html's own error handling --
        // exactly the "nothing happens, no visible error" failure mode this must never produce.
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptHeader));

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfterSeconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                _logger.LogWarning(
                    "Wikidata rate-limited this request (HTTP 429{RetryAfter}); not retrying, the next scheduled refresh will try again.",
                    retryAfterSeconds.HasValue ? $" -- Retry-After: {retryAfterSeconds.Value}s" : string.Empty);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "Wikidata request to '{Uri}' failed with HTTP {StatusCode}: {Body}",
                    uri,
                    (int)response.StatusCode,
                    body.Length > 500 ? body[..500] : body);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wikidata request to '{Uri}' failed; not retrying (see WikidataApiClient resilience policy).", uri);
            return null;
        }
    }
}
