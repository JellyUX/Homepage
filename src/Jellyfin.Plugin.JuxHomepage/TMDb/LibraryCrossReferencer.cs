using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Cross-references external cache items (<see cref="IExternalCacheItem"/>) against the local
/// Jellyfin library, by IMDb ID falling back to the provider's own ID, setting
/// <see cref="IExternalCacheItem.LibraryItemId"/> on each matched item.
/// <para>
/// Extracted from <see cref="TMDbCacheService"/> (TODO_V2.md Phase 7.3) so a future second external
/// data provider can reuse the same cross-referencing logic without duplicating it. Depends only on
/// <see cref="ILibraryManager"/> and an external-id fetcher function -- no TMDb-specific coupling.
/// </para>
/// </summary>
public sealed class LibraryCrossReferencer
{
    /// <summary>Maximum number of items cross-referenced concurrently.</summary>
    private const int MaxConcurrency = 5;

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryCrossReferencer"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="logger">Logger.</param>
    public LibraryCrossReferencer(ILibraryManager libraryManager, ILogger logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Sets <see cref="IExternalCacheItem.LibraryItemId"/> on each item by looking it up in the local
    /// library, first by the provider's own id (no network call), then by IMDb ID (fetched via
    /// <paramref name="getExternalImdbId"/>) for items not found that way. Items are
    /// processed with bounded concurrency (both the external IMDb ID HTTP lookup and the local
    /// library query per item) -- <see cref="ILibraryManager"/> does not support matching against a
    /// list of provider id values in a single query (<c>InternalItemsQuery.HasAnyProviderId</c> is a
    /// dictionary, one exact value per provider key), so <see cref="FindLibraryMatchId"/> itself cannot
    /// be batched across items; parallelizing the per-item pipeline is the available alternative.
    /// </summary>
    /// <typeparam name="T">The external cache item type.</typeparam>
    /// <param name="items">The items to cross-reference, mutated in place.</param>
    /// <param name="getExternalImdbId">Fetches the IMDb ID for a given provider id, if known.</param>
    /// <param name="includeItemTypes">The Jellyfin item kinds to search within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="fallbackProvider">
    /// The <see cref="MetadataProvider"/> whose id matches <typeparamref name="T"/>'s own
    /// <see cref="IExternalCacheItem.Id"/>, tried first, before any IMDb lookup. Defaults
    /// to <see cref="MetadataProvider.Tmdb"/> for the existing TMDb-backed callers. Pass <c>null</c>
    /// for a provider with no corresponding <see cref="MetadataProvider"/> entry (e.g. Wikidata, see
    /// TODO_V2.md Phase 14) -- falling back to <see cref="MetadataProvider.Tmdb"/> in that case would
    /// compare an unrelated identifier (a parsed Wikidata Q-id) against real TMDb ids, risking a
    /// spurious match rather than just a missed one.
    /// </param>
    /// <returns>The number of items that were matched to a local library item.</returns>
    public async Task<int> CrossReferenceAsync<T>(
        IReadOnlyList<T> items,
        Func<int, CancellationToken, Task<string?>> getExternalImdbId,
        BaseItemKind[] includeItemTypes,
        CancellationToken cancellationToken,
        MetadataProvider? fallbackProvider = MetadataProvider.Tmdb)
        where T : IExternalCacheItem
    {
        using var semaphore = new SemaphoreSlim(MaxConcurrency);

        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Local library first (F-07): an item carrying the provider's own id is matched
                // without any HTTP call; the IMDb lookup only runs for the others.
                var match = fallbackProvider is { } provider
                    ? FindLibraryMatchId(provider, item.Id.ToString(CultureInfo.InvariantCulture), includeItemTypes)
                    : null;

                if (match is null)
                {
                    string? imdbId = null;
                    try
                    {
                        imdbId = await getExternalImdbId(item.Id, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch external IDs for TMDb id {TmdbId}.", item.Id);
                    }

                    match = string.IsNullOrEmpty(imdbId)
                        ? null
                        : FindLibraryMatchId(MetadataProvider.Imdb, imdbId, includeItemTypes);
                }

                item.LibraryItemId = match;
                return match is not null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var matched = results.Count(wasMatched => wasMatched);

        return matched;
    }

    private Guid? FindLibraryMatchId(MetadataProvider provider, string value, BaseItemKind[] includeItemTypes)
    {
        var result = _libraryManager.GetItemIds(new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string> { [provider.ToString()] = value },
            IncludeItemTypes = includeItemTypes,
            Recursive = true,
            Limit = 1
        });

        return result.Count > 0 ? result[0] : null;
    }
}
