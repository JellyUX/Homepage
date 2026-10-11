using Jellyfin.Plugin.JuxHomepage.TMDb.Models;

namespace Jellyfin.Plugin.JuxHomepage.Rewards.Models;

/// <summary>
/// A single film/award-category pairing returned by a Wikidata SPARQL query, implementing
/// <see cref="IExternalCacheItem"/> (TODO_V2.md Phase 7) so it can be persisted by
/// <see cref="Caching.DiskJsonCache{T}"/> and cross-referenced by
/// <see cref="TMDb.LibraryCrossReferencer"/> exactly like <see cref="TMDbMovie"/>/<see cref="TMDbShow"/>.
/// <para>
/// <see cref="Id"/> is the numeric suffix of <see cref="FilmQid"/> (e.g. "Q117085614" -&gt; 117085614)
/// parsed to an <c>int</c>, a deliberate simplification: current Wikidata Q-ids top out around 130
/// million, well under <see cref="int.MaxValue"/> (~2.1 billion). Unlike TMDb, no follow-up HTTP call
/// is needed to resolve <see cref="ImdbId"/> -- Wikidata's <c>wdt:P345</c> (IMDb ID) property is
/// fetched directly in the same SPARQL query that produces this item (see
/// <see cref="Rewards.WikidataApiClient"/>).
/// </para>
/// </summary>
public sealed class RewardsWinner : IExternalCacheItem
{
    /// <inheritdoc/>
    public int Id { get; set; }

    /// <inheritdoc/>
    public Guid? LibraryItemId { get; set; }

    /// <summary>Gets or sets the film's full Wikidata entity id (e.g. "Q117085614").</summary>
    public string FilmQid { get; set; } = string.Empty;

    /// <summary>Gets or sets the film's title, as labeled by Wikidata.</summary>
    public string FilmLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the award category's label, as labeled by Wikidata (e.g. "Academy Award for Best Picture").</summary>
    public string AwardLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the year the award was received, if known.</summary>
    public int? PointInTimeYear { get; set; }

    /// <summary>Gets or sets the film's IMDb identifier (e.g. "tt15398776"), used for library cross-referencing.</summary>
    public string ImdbId { get; set; } = string.Empty;
}
