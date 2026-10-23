using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Native widget that shows recently added movies, ordered by creation date.
/// </summary>
public sealed class RecentlyAddedMoviesWidget : RecentlyAddedWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecentlyAddedMoviesWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="userViewManager">Jellyfin user view manager, which computes the latest media.</param>
    public RecentlyAddedMoviesWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IUserViewManager userViewManager)
        : base(userManager, libraryManager, dtoService, userViewManager)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.native.recently-added-movies";

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    public override string? Route => WidgetRoutes.Movies;

    /// <inheritdoc/>
    protected override BaseItemKind ItemKind => BaseItemKind.Movie;
}
