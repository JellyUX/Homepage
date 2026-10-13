using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Displays items featuring a specific actor chosen by the administrator.
/// Multiple instances can be created, each pinned to a different actor.
/// </summary>
public sealed class ActorWidget : PersonWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public ActorWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.admin.actor";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Actor";

    /// <inheritdoc/>
    protected override string PersonType => "Actor";
}
