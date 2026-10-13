using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <inheritdoc cref="IWatchlistService"/>
public sealed class WatchlistService : IWatchlistService
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistService"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    public WatchlistService(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
    }

    /// <inheritdoc/>
    public WidgetResult GetItems(
        Guid userId,
        string? sortBy,
        string? sortOrder,
        string? includeItemTypes,
        string? searchTerm,
        int startIndex,
        int limit,
        bool includeTotalRecordCount,
        CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return WidgetResult.Empty;
        }

        var dtoOptions = WidgetDtoOptions.Standard();
        var query = new InternalItemsQuery(user)
        {
            IsLiked = true,
            Recursive = true,
            IsMissing = false,
            EnableTotalRecordCount = includeTotalRecordCount,
            IncludeItemTypes = ResolveIncludeItemTypes(includeItemTypes),
            NameContains = NameSearch.ForNameContains(searchTerm),
            OrderBy = [(ResolveSortBy(sortBy), ResolveSortOrder(sortOrder))],
            StartIndex = startIndex,
            Limit = limit,
            DtoOptions = dtoOptions
        };

        var result = _libraryManager.GetItemsResult(query);
        var dtos = _dtoService.GetBaseItemDtos(result.Items, dtoOptions, user);
        return new WidgetResult(dtos, includeTotalRecordCount ? result.TotalRecordCount : dtos.Count);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Guid> GetLikedItemIds(Guid userId, CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return [];
        }

        // Ids only, movies and series only (F-06): every page load calls this, and without a type the
        // liked filter was evaluated on every library row (~1 s on a 3000-film library).
        return _libraryManager.GetItemIds(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsLiked = true,
            Recursive = true,
            IsMissing = false
        });
    }

    /// <summary>
    /// Maps the "includeItemTypes" query parameter to a concrete <see cref="BaseItemKind"/> array.
    /// Unrecognized values fall back to "All" (both movies and series).
    /// </summary>
    private static BaseItemKind[] ResolveIncludeItemTypes(string? includeItemTypes) => includeItemTypes?.ToLowerInvariant() switch
    {
        "movie" => [BaseItemKind.Movie],
        "series" => [BaseItemKind.Series],
        _ => [BaseItemKind.Movie, BaseItemKind.Series]
    };

    /// <summary>
    /// Maps the "sortBy" query parameter to an <see cref="ItemSortBy"/> constant. Unrecognized or
    /// null values fall back to "DateAdded" -- <c>Likes</c> has no timestamp of its own, so
    /// <see cref="ItemSortBy.DateCreated"/> (when the item was added to the library) is the closest
    /// available proxy for "recently added to the Watchlist".
    /// </summary>
    private static ItemSortBy ResolveSortBy(string? sortBy) => sortBy?.ToLowerInvariant() switch
    {
        "name" => ItemSortBy.SortName,
        "releasedate" => ItemSortBy.PremiereDate,
        "communityrating" => ItemSortBy.CommunityRating,
        _ => ItemSortBy.DateCreated
    };

    /// <summary>Maps the "sortOrder" query parameter to a <see cref="SortOrder"/>. Defaults to descending.</summary>
    private static SortOrder ResolveSortOrder(string? sortOrder) =>
        string.Equals(sortOrder, "Ascending", StringComparison.OrdinalIgnoreCase) ? SortOrder.Ascending : SortOrder.Descending;
}
