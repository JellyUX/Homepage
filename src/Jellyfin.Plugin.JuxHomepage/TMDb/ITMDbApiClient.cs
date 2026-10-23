using Jellyfin.Plugin.JuxHomepage.TMDb.Models;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Client for TMDb (The Movie Database) API v3 list and external-ids endpoints.
/// All methods degrade gracefully: a missing API key, an invalid key (HTTP 401), or a network
/// failure (after one retry) results in an empty list or null, never an exception, except
/// <see cref="OperationCanceledException"/> when the caller cancels its own token.
/// </summary>
public interface ITMDbApiClient
{
    /// <summary>Fetches the current weekly trending movies.</summary>
    /// <param name="pages">
    /// Number of TMDb result pages to fetch and concatenate (1 page = up to 20 items). Clamped to
    /// 1-5 by the caller/config; fetching stops early if a page returns no results.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trending movies, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbMovie>> GetTrendingMoviesAsync(int pages, CancellationToken cancellationToken);

    /// <summary>Fetches the current weekly trending TV shows.</summary>
    /// <param name="pages">Number of TMDb result pages to fetch and concatenate (1 page = up to 20 items).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trending shows, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbShow>> GetTrendingShowsAsync(int pages, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches TV shows currently "on the air" (with an episode airing in the next 7 days, per
    /// TMDb's <c>tv/on_the_air</c> endpoint -- broader than the literal "airing today" name).
    /// </summary>
    /// <param name="pages">Number of TMDb result pages to fetch and concatenate (1 page = up to 20 items).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The on-the-air shows, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbShow>> GetAiringTodayAsync(int pages, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches top rated movies via <c>discover/movie</c> (sorted by <c>vote_average.desc</c>,
    /// filtered by <paramref name="voteCountMin"/>) rather than the fixed <c>movie/top_rated</c>
    /// endpoint, so the vote count threshold is admin-configurable (see TODO_V2.md Phase 3.4).
    /// </summary>
    /// <param name="pages">Number of TMDb result pages to fetch and concatenate (1 page = up to 20 items).</param>
    /// <param name="voteCountMin">Minimum vote count (TMDb's <c>vote_count.gte</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The top rated movies, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbMovie>> GetTopRatedMoviesAsync(int pages, int voteCountMin, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches top rated TV shows via <c>discover/tv</c> (sorted by <c>vote_average.desc</c>,
    /// filtered by <paramref name="voteCountMin"/>) rather than the fixed <c>tv/top_rated</c>
    /// endpoint, so the vote count threshold is admin-configurable (see TODO_V2.md Phase 3.4).
    /// </summary>
    /// <param name="pages">Number of TMDb result pages to fetch and concatenate (1 page = up to 20 items).</param>
    /// <param name="voteCountMin">Minimum vote count (TMDb's <c>vote_count.gte</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The top rated shows, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbShow>> GetTopRatedShowsAsync(int pages, int voteCountMin, CancellationToken cancellationToken);

    /// <summary>Fetches movies currently in theatres.</summary>
    /// <param name="pages">Number of TMDb result pages to fetch and concatenate (1 page = up to 20 items).</param>
    /// <param name="region">
    /// Optional ISO 3166-1 region code (e.g. "FR") to scope results to a specific country's theatrical
    /// releases. Null uses TMDb's own default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The now-playing movies, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbMovie>> GetNowPlayingMoviesAsync(int pages, string? region, CancellationToken cancellationToken);

    /// <summary>Fetches the list of ISO 3166-1 countries known to TMDb, for the region dropdown.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The countries, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbCountry>> GetCountriesAsync(CancellationToken cancellationToken);

    /// <summary>Fetches the full list of TMDb movie genres, for the Discover widget's genre picker.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The genres, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbGenre>> GetMovieGenresAsync(CancellationToken cancellationToken);

    /// <summary>Searches TMDb people by name, for the Discover widget's person autocomplete.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching people, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbSearchResult>> SearchPersonAsync(string query, CancellationToken cancellationToken);

    /// <summary>Searches TMDb keywords by name, for the Discover widget's keyword autocomplete.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching keywords, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbSearchResult>> SearchKeywordAsync(string query, CancellationToken cancellationToken);

    /// <summary>Searches TMDb companies by name, for the Discover widget's company autocomplete.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching companies, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbSearchResult>> SearchCompanyAsync(string query, CancellationToken cancellationToken);

    /// <summary>Fetches movies matching a Discover widget instance's configured filter.</summary>
    /// <param name="filter">The filter parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching movies, or an empty list if the key is missing/invalid or the request failed.</returns>
    Task<IReadOnlyList<TMDbMovie>> DiscoverMoviesAsync(TMDbDiscoverFilter filter, CancellationToken cancellationToken);

    /// <summary>Fetches the IMDb identifier for a TMDb movie, used for library cross-referencing.</summary>
    /// <param name="tmdbId">The TMDb movie identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IMDb identifier (e.g. "tt1375666"), an empty string if TMDb has none, or null if the request failed.</returns>
    Task<string?> GetMovieExternalIdsAsync(int tmdbId, CancellationToken cancellationToken);

    /// <summary>Fetches the IMDb identifier for a TMDb TV show, used for library cross-referencing.</summary>
    /// <param name="tmdbId">The TMDb show identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IMDb identifier (e.g. "tt1234567"), an empty string if TMDb has none, or null if the request failed.</returns>
    Task<string?> GetShowExternalIdsAsync(int tmdbId, CancellationToken cancellationToken);
}
