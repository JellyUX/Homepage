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
    private readonly LikedItemIdsCache? _likedIds;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistService"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="likedIds">Keeps each user's liked ids in memory (PERF-05.a); null queries every time.</param>
    public WatchlistService(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService, LikedItemIdsCache? likedIds = null)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _likedIds = likedIds;
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
        return WidgetResult.Project(_dtoService, result.Items, dtoOptions, user, includeTotalRecordCount ? result.TotalRecordCount : null);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Guid> GetLikedItemIds(Guid userId, CancellationToken cancellationToken)
    {
        // Every page load calls this: the ids are kept in memory between two changes (PERF-05.a).
        if (_likedIds is not null && _likedIds.TryGet(userId, out var cached))
        {
            return cached;
        }

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return [];
        }

        var version = _likedIds?.VersionOf(userId);

        // Ids only, movies and series only (F-06): without a type the liked filter was evaluated on
        // every library row (~1 s on a 3000-film library).
        var ids = _libraryManager.GetItemIds(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsLiked = true,
            Recursive = true,
            IsMissing = false
        });
        if (version is { } readVersion)
        {
            _likedIds!.Set(userId, ids, readVersion);
        }

        return ids;
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
    /// Maps the "sortBy" query parameter, a <see cref="WidgetSort"/> value, to an <see cref="ItemSortBy"/>
    /// constant. Unrecognized or null values fall back to <see cref="WidgetSort.DateCreated"/> --
    /// <c>Likes</c> has no timestamp of its own, so the date the item was added to the library is the
    /// closest available proxy for "recently added to the Watchlist".
    /// </summary>
    private static ItemSortBy ResolveSortBy(string? sortBy) => sortBy switch
    {
        WidgetSort.SortName => ItemSortBy.SortName,
        WidgetSort.PremiereDate => ItemSortBy.PremiereDate,
        WidgetSort.CommunityRating => ItemSortBy.CommunityRating,
        WidgetSort.DateCreated => ItemSortBy.DateCreated,
        _ => ItemSortBy.DateCreated
    };

    /// <summary>Maps the "sortOrder" query parameter to a <see cref="SortOrder"/>. Defaults to descending.</summary>
    private static SortOrder ResolveSortOrder(string? sortOrder) =>
        sortOrder == WidgetSort.Ascending ? SortOrder.Ascending : SortOrder.Descending;
}
