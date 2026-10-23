using Jellyfin.Plugin.JuxHomepage.Rewards.Models;

namespace Jellyfin.Plugin.JuxHomepage.Rewards;

/// <summary>
/// Client for Wikidata's public SPARQL query service and entity search endpoint.
/// Every method degrades gracefully: a network failure, a non-success HTTP status (including 429,
/// see <see cref="WikidataApiClient"/>'s resilience policy), or an empty result set all yield an
/// empty list. No public method throws, except <see cref="OperationCanceledException"/> when the
/// caller cancels its own token.
/// </summary>
public interface IWikidataApiClient
{
    /// <summary>
    /// Fetches films matching the given award filter (ceremony and/or category, optionally scoped to
    /// a single year) that carry a Wikidata IMDb identifier (<c>wdt:P345</c>).
    /// </summary>
    /// <param name="filter">The ceremony/category/year filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching award winners, or an empty list if the request failed or found nothing.</returns>
    Task<IReadOnlyList<RewardsWinner>> GetAwardWinnersAsync(RewardsFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Searches Wikidata entities by label, for the admin UI's Ceremony/Category autocomplete fields.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching entities, or an empty list if the request failed or found nothing.</returns>
    Task<IReadOnlyList<WikidataSearchEntity>> SearchEntitiesAsync(string query, CancellationToken cancellationToken);
}
