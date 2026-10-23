using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// HTTP client for the TMDb (The Movie Database) API v3.
/// <para>
/// Supports both TMDb key formats: a 32-character v3 API key (sent as an <c>api_key</c> query
/// parameter) and a v4 read-access token (a JWT-like string starting with "ey", sent as an
/// <c>Authorization: Bearer</c> header). The same regexes used to validate the key in
/// <c>config.html</c> decide which format is in use.
/// </para>
/// <para>
/// Every public method degrades gracefully: a missing key results in a silent skip (debug log
/// only), an invalid key (HTTP 401) is logged as an explicit error, a network failure is
/// retried once before being logged as an error, and a response that fails to parse as the
/// expected JSON shape is logged as an explicit error without a retry (a schema mismatch is not
/// transient). No public method throws, except when the caller cancels its own token.
/// </para>
/// </summary>
public sealed partial class TMDbApiClient : ITMDbApiClient
{
    private const string HttpClientName = "TMDb";

    /// <summary>Hard ceiling on pages fetched per call, regardless of the requested count.</summary>
    private const int MaxPagesPerFetch = 5;

    /// <summary>Consecutive sustained-outage failures before the circuit breaker opens.</summary>
    private const int FailureThreshold = 3;

    /// <summary>How long the circuit breaker stays open before allowing a single probe request.</summary>
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<TMDbApiClient> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _circuitLock = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _circuitOpenedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="TMDbApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create the named "TMDb" HTTP client.</param>
    /// <param name="getConfiguration">
    /// Factory that returns the current plugin configuration, used to read the live API key.
    /// Defaults to <c>Plugin.Instance?.Configuration</c> in production.
    /// </param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">
    /// Time source for the circuit breaker, for testability. Defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    public TMDbApiClient(
        IHttpClientFactory httpClientFactory,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<TMDbApiClient> logger,
        TimeProvider? timeProvider = null)
    {
        _httpClientFactory = httpClientFactory;
        _getConfiguration = getConfiguration;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbMovie>> GetTrendingMoviesAsync(int pages, CancellationToken cancellationToken) =>
        GetPagedAsync<TMDbMovie>("trending/movie/week", pages, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbShow>> GetTrendingShowsAsync(int pages, CancellationToken cancellationToken) =>
        GetPagedAsync<TMDbShow>("trending/tv/week", pages, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbShow>> GetAiringTodayAsync(int pages, CancellationToken cancellationToken) =>
        // "tv/on_the_air" (shows with an episode airing in the next 7 days) is used instead of
        // "tv/airing_today" (only shows whose episode airs today/tomorrow, far too narrow a
        // window to populate a useful section).
        GetPagedAsync<TMDbShow>("tv/on_the_air", pages, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbMovie>> GetTopRatedMoviesAsync(int pages, int voteCountMin, CancellationToken cancellationToken) =>
        GetPagedAsync<TMDbMovie>(
            $"discover/movie?sort_by=vote_average.desc&vote_count.gte={voteCountMin}",
            pages,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbShow>> GetTopRatedShowsAsync(int pages, int voteCountMin, CancellationToken cancellationToken) =>
        GetPagedAsync<TMDbShow>(
            $"discover/tv?sort_by=vote_average.desc&vote_count.gte={voteCountMin}",
            pages,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbMovie>> GetNowPlayingMoviesAsync(
        int pages,
        string? region,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrEmpty(region)
            ? "movie/now_playing"
            : $"movie/now_playing?region={Uri.EscapeDataString(region)}";
        return GetPagedAsync<TMDbMovie>(path, pages, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TMDbCountry>> GetCountriesAsync(CancellationToken cancellationToken)
    {
        var result = await GetAsync<List<TMDbCountry>>("configuration/countries", cancellationToken)
            .ConfigureAwait(false);
        return result ?? [];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TMDbGenre>> GetMovieGenresAsync(CancellationToken cancellationToken)
    {
        var result = await GetAsync<TMDbGenreListResponse>("genre/movie/list", cancellationToken)
            .ConfigureAwait(false);
        return result?.Genres ?? [];
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbSearchResult>> SearchPersonAsync(string query, CancellationToken cancellationToken) =>
        SearchAsync("search/person", query, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbSearchResult>> SearchKeywordAsync(string query, CancellationToken cancellationToken) =>
        SearchAsync("search/keyword", query, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbSearchResult>> SearchCompanyAsync(string query, CancellationToken cancellationToken) =>
        SearchAsync("search/company", query, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<TMDbMovie>> DiscoverMoviesAsync(TMDbDiscoverFilter filter, CancellationToken cancellationToken) =>
        GetPagedAsync<TMDbMovie>(BuildDiscoverMoviePath(filter), filter.Pages, cancellationToken);

    private async Task<IReadOnlyList<TMDbSearchResult>> SearchAsync(
        string endpoint,
        string query,
        CancellationToken cancellationToken)
    {
        var path = $"{endpoint}?query={Uri.EscapeDataString(query)}";
        var result = await GetAsync<TMDbTrending<TMDbSearchResult>>(path, cancellationToken).ConfigureAwait(false);
        return result?.Results ?? [];
    }

    private static string BuildDiscoverMoviePath(TMDbDiscoverFilter filter)
    {
        var query = new List<string>
        {
            $"sort_by={Uri.EscapeDataString(filter.SortBy)}",
            $"vote_count.gte={filter.VoteCountGte}"
        };

        if (filter.GenreIds is { Count: > 0 })
        {
            query.Add($"with_genres={string.Join(',', filter.GenreIds)}");
        }

        if (filter.PersonIds is { Count: > 0 })
        {
            query.Add($"with_people={string.Join(',', filter.PersonIds)}");
        }

        if (filter.KeywordIds is { Count: > 0 })
        {
            query.Add($"with_keywords={string.Join(',', filter.KeywordIds)}");
        }

        if (filter.CompanyIds is { Count: > 0 })
        {
            query.Add($"with_companies={string.Join(',', filter.CompanyIds)}");
        }

        if (filter.PrimaryReleaseYear.HasValue)
        {
            query.Add($"primary_release_year={filter.PrimaryReleaseYear.Value}");
        }

        if (filter.VoteAverageGte.HasValue)
        {
            query.Add($"vote_average.gte={filter.VoteAverageGte.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return $"discover/movie?{string.Join('&', query)}";
    }

    /// <inheritdoc/>
    public async Task<string?> GetMovieExternalIdsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var result = await GetAsync<TMDbExternalIds>($"movie/{tmdbId}/external_ids", cancellationToken)
            .ConfigureAwait(false);
        return result is null ? null : result.ImdbId ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<string?> GetShowExternalIdsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var result = await GetAsync<TMDbExternalIds>($"tv/{tmdbId}/external_ids", cancellationToken)
            .ConfigureAwait(false);
        return result is null ? null : result.ImdbId ?? string.Empty;
    }

    /// <summary>
    /// Fetches and concatenates up to <paramref name="pages"/> pages (clamped to
    /// <see cref="MaxPagesPerFetch"/>) of a standard TMDb paged list response. Stops early if a page
    /// returns no results, which covers both "TMDb ran out of pages" and "the request failed"
    /// (<see cref="GetAsync{T}"/> already logs the failure in the latter case).
    /// </summary>
    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(string basePath, int pages, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var clampedPages = Math.Clamp(pages, 1, MaxPagesPerFetch);

        for (var page = 1; page <= clampedPages; page++)
        {
            var separator = basePath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var pagedPath = $"{basePath}{separator}page={page}";

            var pageResult = await GetAsync<TMDbTrending<T>>(pagedPath, cancellationToken).ConfigureAwait(false);
            if (pageResult is null || pageResult.Results.Count == 0)
            {
                break;
            }

            results.AddRange(pageResult.Results);
        }

        return results;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (IsCircuitOpen(path))
        {
            return null;
        }

        var apiKey = _getConfiguration()?.ApiKeys?.TMDb;
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogDebug("TMDb API key not configured; skipping request to '{Path}'.", path);
            return null;
        }

        var isV4Token = V4TokenPattern().IsMatch(apiKey);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var requestUri = isV4Token
            ? path
            : path + (path.Contains('?', StringComparison.Ordinal) ? '&' : '?') + "api_key=" + Uri.EscapeDataString(apiKey);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                if (isV4Token)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.LogError(
                        "TMDb rejected the configured API key (HTTP 401) for request to '{Path}'.",
                        path);
                    return null;
                }

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
                lock (_circuitLock)
                {
                    _consecutiveFailures = 0;
                }

                return result;
            }
            // A cancelled caller is not a TMDb failure: rethrown, never retried or counted toward the circuit
            // (GetTrendingMoviesAsync_CallerCancels_ThrowsWithoutRetryOrCountingAFailure). HttpClient.Timeout
            // also throws TaskCanceledException, but with the caller's token untouched.
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt == 0)
                {
                    _logger.LogWarning(ex, "TMDb request to '{Path}' failed; retrying once.", path);
                    continue;
                }

                _logger.LogError(ex, "TMDb request to '{Path}' failed after retry.", path);
                RecordSustainedFailure();
                return null;
            }
            catch (JsonException ex)
            {
                // A schema/format problem in the response body is not a transient network issue --
                // retrying won't help, and it must not count toward the circuit breaker (same
                // reasoning already applied to HTTP 401 above: waiting doesn't fix it).
                _logger.LogError(ex, "TMDb response for '{Path}' could not be parsed as JSON.", path);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether the circuit breaker is currently open (skipping requests after sustained
    /// network-level failures). Only <see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/>
    /// count toward opening the circuit -- an invalid API key (HTTP 401) is a distinct, non-transient
    /// problem that waiting will not fix, so it is logged separately and never trips this breaker.
    /// </summary>
    private bool IsCircuitOpen(string path)
    {
        lock (_circuitLock)
        {
            if (_circuitOpenedAt is not { } openedAt)
            {
                return false;
            }

            if (_timeProvider.GetUtcNow() - openedAt < OpenDuration)
            {
                _logger.LogWarning("TMDb circuit breaker open; skipping request to '{Path}'.", path);
                return true;
            }

            // The open window has elapsed: allow one probe request through. If it also fails,
            // RecordSustainedFailure will reopen the circuit from a clean count.
            _circuitOpenedAt = null;
            _consecutiveFailures = 0;
            return false;
        }
    }

    private void RecordSustainedFailure()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= FailureThreshold)
            {
                _circuitOpenedAt = _timeProvider.GetUtcNow();
                _logger.LogWarning(
                    "TMDb circuit breaker opened after {Count} consecutive failures; TMDb requests will be skipped for {Minutes} minute(s).",
                    _consecutiveFailures,
                    OpenDuration.TotalMinutes);
            }
        }
    }

    [GeneratedRegex(@"^ey[\w-]+\.[\w-]+\.[\w-]+$")]
    private static partial Regex V4TokenPattern();
}
