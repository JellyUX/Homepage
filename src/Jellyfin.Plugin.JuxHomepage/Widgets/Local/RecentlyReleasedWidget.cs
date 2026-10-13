using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Local;

/// <summary>
/// The library's latest releases, newest release date first (TODO_V4.md 9.1, F-61): a local
/// counterpart of the TMDb lists, filled from the library alone. Items dated in the future are left
/// out, so an announced title never tops the row before it is out.
/// </summary>
public sealed class RecentlyReleasedWidget : LocalLibraryWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecentlyReleasedWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="timeProvider">Time source of the current day. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="itemFetcher">Runs the section query in its order; a private one when null (tests).</param>
    public RecentlyReleasedWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        TimeProvider? timeProvider = null,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, timeProvider, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.local.recently-released";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Recently Released";

    /// <inheritdoc/>
    protected override string SortBy => WidgetSort.PremiereDate;

    /// <inheritdoc/>
    protected override void ApplyFilter(InternalItemsQuery query)
    {
        query.MaxPremiereDate = TimeProvider.GetUtcNow().UtcDateTime;
    }
}
