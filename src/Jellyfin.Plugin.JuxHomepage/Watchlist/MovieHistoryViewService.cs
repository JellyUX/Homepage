using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <inheritdoc cref="IMovieHistoryViewService"/>
public sealed class MovieHistoryViewService : IMovieHistoryViewService
{
    private readonly IMovieHistoryCacheService _movieHistoryCache;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly ILogger<MovieHistoryViewService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieHistoryViewService"/> class.
    /// </summary>
    /// <param name="movieHistoryCache">Movie History disk cache.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="logger">Logger.</param>
    public MovieHistoryViewService(
        IMovieHistoryCacheService movieHistoryCache,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ILogger<MovieHistoryViewService> logger)
    {
        _movieHistoryCache = movieHistoryCache;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public WidgetResult GetItems(
        Guid userId,
        string? sortBy,
        string? sortOrder,
        string? searchTerm,
        int startIndex,
        int limit,
        CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return WidgetResult.Empty;
        }

        var entries = NameSearch.Filter(_movieHistoryCache.GetHistory(userId), e => e.Name, NameSearch.Normalize(searchTerm));
        var sorted = Sort(entries, sortBy, sortOrder);

        // The whole history is checked against the user's current rights before paginating, so a
        // library or parental restriction added after the cache was built hides the item and does
        // not leak through the total either.
        var visibleItems = UserVisibleItems.Resolve(
            _libraryManager,
            user,
            sorted.Select(e => e.ItemId).ToList(),
            out var unresolvedCount);

        if (unresolvedCount > 0)
        {
            _logger.LogDebug(
                "{Count} cached Movie History entrie(s) no longer resolve to a local library item (likely removed since the last cache refresh).",
                unresolvedCount);
        }

        var page = visibleItems.Skip(startIndex).Take(limit).ToList();
        if (page.Count == 0)
        {
            return new WidgetResult([], visibleItems.Count);
        }

        var dtos = _dtoService.GetBaseItemDtos(page, WidgetDtoOptions.Standard(), user);
        return new WidgetResult(dtos, visibleItems.Count);
    }

    private static IReadOnlyList<Models.MovieHistoryEntry> Sort(
        IReadOnlyList<Models.MovieHistoryEntry> entries,
        string? sortBy,
        string? sortOrder)
    {
        var descending = !WidgetSort.Matches(sortOrder, WidgetSort.Ascending);

        IOrderedEnumerable<Models.MovieHistoryEntry> ordered = WidgetSort.Matches(sortBy, WidgetSort.SortName)
            ? (descending
                ? entries.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            : (descending
                ? entries.OrderByDescending(e => e.LastPlayedDate ?? DateTime.MinValue)
                : entries.OrderBy(e => e.LastPlayedDate ?? DateTime.MinValue));

        return ordered.ToList();
    }
}
