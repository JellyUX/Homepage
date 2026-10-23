using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Displays items directed by a specific director chosen by the administrator.
/// Multiple instances can be created, each pinned to a different director.
/// </summary>
public sealed class DirectorWidget : PersonWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectorWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public DirectorWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.admin.director";

    /// <inheritdoc/>
    protected override string PersonType => "Director";
}
