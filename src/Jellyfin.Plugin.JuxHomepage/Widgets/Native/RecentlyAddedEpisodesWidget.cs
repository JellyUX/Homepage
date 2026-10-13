using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Native widget that shows recently added episodes, ordered by creation date.
/// </summary>
public sealed class RecentlyAddedEpisodesWidget : RecentlyAddedWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecentlyAddedEpisodesWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="userViewManager">Jellyfin user view manager, which computes the latest media.</param>
    public RecentlyAddedEpisodesWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IUserViewManager userViewManager)
        : base(userManager, libraryManager, dtoService, userViewManager)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.native.recently-added-episodes";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Recently Added Episodes";

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Landscape;

    /// <inheritdoc/>
    public override string? Route => WidgetRoutes.TvShows;

    /// <inheritdoc/>
    protected override BaseItemKind ItemKind => BaseItemKind.Episode;

    /// <summary>
    /// Gets the cap of one series in the row: at most two of its episodes, so a season added at once
    /// does not fill it (TODO_V4.md 9.3, F-65). Mirrored by the home script's native route.
    /// </summary>
    protected override int? MaxPerSeries => 2;

    /// <inheritdoc/>
    protected override int LimitFactor => 10;
}
