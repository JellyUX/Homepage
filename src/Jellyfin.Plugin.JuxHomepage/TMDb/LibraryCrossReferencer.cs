using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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
    /// <summary>Maximum number of IMDb ID lookups running concurrently.</summary>
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
    /// Reads the library's items of the given kinds once, for <see cref="CrossReferenceAsync"/>. One
    /// index can serve every list of the same kinds in a refresh (DEC-P06-1).
    /// </summary>
    /// <param name="includeItemTypes">The Jellyfin item kinds to match against.</param>
    /// <returns>The index of the library's provider ids.</returns>
    public LibraryProviderIndex BuildIndex(BaseItemKind[] includeItemTypes)
    {
        var index = LibraryProviderIndex.Build(_libraryManager, includeItemTypes);
        _logger.LogDebug("Indexed {Count} library item(s) of {Kinds} by provider id.", index.ItemCount, string.Join(", ", includeItemTypes));
        return index;
    }

    /// <summary>
    /// Sets <see cref="IExternalCacheItem.LibraryItemId"/> on each item by looking it up in
    /// <paramref name="index"/>, first by the provider's own id (no network call), then by IMDb ID
    /// (fetched via <paramref name="getExternalImdbId"/>) for items not found that way. Matching is
    /// in memory (PERF-04: one library read per index, not one query per item,
    /// <c>CrossReference_400Items_UsesAtMostTwoLibraryQueries</c>); only the IMDb ID lookups, which can
    /// be HTTP calls, run with bounded concurrency
    /// (<c>CrossReferenceAsync_ManyItems_RunsWithBoundedConcurrency</c>).
    /// </summary>
    /// <typeparam name="T">The external cache item type.</typeparam>
    /// <param name="items">The items to cross-reference, mutated in place.</param>
    /// <param name="getExternalImdbId">Fetches the IMDb ID for a given provider id, if known.</param>
    /// <param name="index">The library items to match against (<see cref="BuildIndex"/>).</param>
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
        LibraryProviderIndex index,
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
                    ? index.Find(provider, item.Id.ToString(CultureInfo.InvariantCulture))
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

                    match = index.Find(MetadataProvider.Imdb, imdbId);
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
}
