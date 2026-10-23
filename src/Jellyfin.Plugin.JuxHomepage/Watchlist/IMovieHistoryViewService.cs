using Jellyfin.Plugin.JuxHomepage.Widgets;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Thin service that turns the raw <see cref="IMovieHistoryCacheService"/> entries into a paginated,
/// sorted, card-renderable result for the Movie History view (TODO_V3.md Phase 6.2) -- mirrors the
/// role <see cref="ISeriesProgressViewService"/> plays for Series Progress. Unlike Series Progress,
/// a movie's watch data is a plain <see cref="MediaBrowser.Model.Dto.BaseItemDto"/> with no extra
/// per-item fields needed (last-played date is already carried by <c>BaseItemDto.UserData</c>), so
/// this reuses <see cref="WidgetResult"/> rather than a bespoke result type.
/// </summary>
public interface IMovieHistoryViewService
{
    /// <summary>
    /// Returns a sorted, paginated page of the user's watched movies, each hydrated into a
    /// <see cref="MediaBrowser.Model.Dto.BaseItemDto"/> for card rendering.
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="sortBy"><see cref="Widgets.WidgetSort.SortName"/> or <see cref="Widgets.WidgetSort.LastPlayed"/> (default); unrecognized values fall back to "LastPlayed".</param>
    /// <param name="sortOrder">"Ascending" or "Descending" (default).</param>
    /// <param name="searchTerm">Name filter (case and accents ignored); null or blank keeps every row.</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of items and the total record count.</returns>
    WidgetResult GetItems(
        Guid userId,
        string? sortBy,
        string? sortOrder,
        string? searchTerm,
        int startIndex,
        int limit,
        CancellationToken cancellationToken);
}
