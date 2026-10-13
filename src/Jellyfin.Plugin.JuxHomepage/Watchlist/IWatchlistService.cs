using Jellyfin.Plugin.JuxHomepage.Widgets;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Queries a user's Watchlist (every item with <c>UserData.Likes == true</c>), filtered/sorted
/// server-side. TODO_V3.md Phase 5.1. Thin service layer between
/// <see cref="Controllers.JuxHomepageController"/> and Jellyfin's library/DTO services, mirroring
/// <see cref="WidgetService"/>'s role -- keeps the controller testable without mocking
/// <c>ILibraryManager</c>/<c>IDtoService</c> directly.
/// </summary>
public interface IWatchlistService
{
    /// <summary>
    /// Returns a filtered, sorted, paginated page of the user's Watchlist items.
    /// </summary>
    /// <param name="userId">The user whose Watchlist to read.</param>
    /// <param name="sortBy">
    /// One of "Name", "DateAdded", "ReleaseDate", "CommunityRating" (case-insensitive). Unrecognized
    /// or null values fall back to "DateAdded".
    /// </param>
    /// <param name="sortOrder">"Ascending" or "Descending" (case-insensitive). Defaults to "Descending".</param>
    /// <param name="includeItemTypes">
    /// One of "Movie", "Series", "All" (case-insensitive, default "All").
    /// </param>
    /// <param name="searchTerm">Name filter (case and accents ignored); null or blank keeps every row.</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="includeTotalRecordCount">
    /// Whether to run the extra count query. When false, the result reports the page size as its
    /// total (F-05): the home section and its MinItems probe never need the full count.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of items and the total matching record count.</returns>
    WidgetResult GetItems(
        Guid userId,
        string? sortBy,
        string? sortOrder,
        string? includeItemTypes,
        string? searchTerm,
        int startIndex,
        int limit,
        bool includeTotalRecordCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every item id currently in the user's Watchlist, with no image/metadata fields
    /// requested -- a lightweight payload for <c>jux-card-hooks.js</c> to pre-load once per page so
    /// each card can immediately show the correct toggle icon (TODO_V3.md Phase 5.2).
    /// </summary>
    /// <param name="userId">The user whose Watchlist item ids to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every item id currently liked by this user.</returns>
    IReadOnlyList<Guid> GetLikedItemIds(Guid userId, CancellationToken cancellationToken);
}
