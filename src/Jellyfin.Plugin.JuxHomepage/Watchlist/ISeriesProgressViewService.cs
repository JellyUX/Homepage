using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Thin service that turns the raw <see cref="ISeriesProgressCacheService"/> entries into a
/// paginated, sorted, card-renderable result for the Series Progress view (TODO_V3.md Phase 6.1) --
/// mirrors the role <see cref="WatchlistService"/> plays for <see cref="IWatchlistService"/>, keeping
/// <see cref="Controllers.JuxHomepageController"/> testable without mocking
/// <see cref="MediaBrowser.Controller.Library.ILibraryManager"/>/<see cref="MediaBrowser.Controller.Dto.IDtoService"/> directly.
/// </summary>
public interface ISeriesProgressViewService
{
    /// <summary>
    /// Returns a sorted, paginated page of the user's in-progress series, each hydrated into a
    /// <see cref="MediaBrowser.Model.Dto.BaseItemDto"/> for card rendering.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="sortBy">"Name" or "LastPlayed" (default); unrecognized values fall back to "LastPlayed".</param>
    /// <param name="sortOrder">"Ascending" or "Descending" (default).</param>
    /// <param name="searchTerm">Name filter (case and accents ignored); null or blank keeps every row.</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of items and the total record count.</returns>
    SeriesProgressResult GetItems(
        Guid userId,
        string? sortBy,
        string? sortOrder,
        string? searchTerm,
        int startIndex,
        int limit,
        CancellationToken cancellationToken);
}
