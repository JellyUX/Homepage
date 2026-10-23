using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Connected;

/// <summary>
/// Displays all-time top rated movies (from TMDb, filtered by TMDb's own vote_count.gte threshold)
/// that are present in the local library.
/// </summary>
public sealed class TopRatedMoviesWidget : ConnectedWidgetBase<TMDbMovie>
{
    private readonly ITMDbCacheService _cacheService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopRatedMoviesWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="cacheService">TMDb disk cache service.</param>
    /// <param name="logger">Logger.</param>
    public TopRatedMoviesWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ITMDbCacheService cacheService,
        ILogger<TopRatedMoviesWidget> logger)
        : base(userManager, libraryManager, dtoService, logger)
    {
        _cacheService = cacheService;
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.connected.top-rated-movies";

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    protected override IReadOnlyList<TMDbMovie> GetCachedItems(WidgetPayload payload) => _cacheService.GetTopRatedMovies();
}
