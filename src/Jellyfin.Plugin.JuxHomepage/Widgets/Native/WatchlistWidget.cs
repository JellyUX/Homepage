using Jellyfin.Plugin.JuxHomepage.Watchlist;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Native widget that shows the current user's Watchlist (TODO_V3.md Phase 6.4) on the home screen.
/// Delegates entirely to <see cref="IWatchlistService"/> (already built and tested in Phase 5, the
/// same <c>Likes</c>-based query the Watchlist tab itself uses) rather than duplicating an
/// <c>InternalItemsQuery</c> -- the one behavioral difference from the Watchlist tab is that this
/// widget always uses the default sort/filter (most-recently-added, all types), matching every other
/// native widget's lack of user-facing sort controls.
/// </summary>
public sealed class WatchlistWidget : NativeWidgetBase
{
    private readonly IWatchlistService _watchlistService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="watchlistService">Watchlist query service.</param>
    public WatchlistWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IWatchlistService watchlistService)
        : base(userManager, libraryManager, dtoService)
    {
        _watchlistService = watchlistService;
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.native.watchlist";

    /// <inheritdoc/>
    public override int DefaultMinItems => 1;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    public override string? Route => WidgetRoutes.Watchlist;

    /// <inheritdoc/>
    protected override bool ReadNatively => false;

    /// <inheritdoc/>
    public override Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        var result = _watchlistService.GetItems(
            payload.UserId,
            sortBy: null,
            sortOrder: null,
            includeItemTypes: null,
            searchTerm: null,
            payload.StartIndex,
            payload.Limit,
            includeTotalRecordCount: false,
            cancellationToken);

        return Task.FromResult(result);
    }
}
