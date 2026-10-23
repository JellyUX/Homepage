using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Native widget that shows the series that received episodes most recently: the latest episodes
/// grouped by series, as the native latest media of a shows library (TODO_V4.md 8.3).
/// </summary>
public sealed class RecentlyAddedShowsWidget : RecentlyAddedWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecentlyAddedShowsWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="userViewManager">Jellyfin user view manager, which computes the latest media.</param>
    public RecentlyAddedShowsWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IUserViewManager userViewManager)
        : base(userManager, libraryManager, dtoService, userViewManager)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.native.recently-added-shows";

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    public override string? Route => WidgetRoutes.TvShows;

    /// <inheritdoc/>
    protected override BaseItemKind ItemKind => BaseItemKind.Episode;

    /// <inheritdoc/>
    protected override bool GroupItems => true;

    /// <inheritdoc/>
    /// <remarks>
    /// The latest media limit counts episodes, not series; filtering the query on Series instead took
    /// over 150 s on a 200-series library (measured on 8096).
    /// </remarks>
    protected override int LimitFactor => 3;
}
