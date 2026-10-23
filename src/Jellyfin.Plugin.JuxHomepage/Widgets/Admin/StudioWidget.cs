using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Displays items from a specific studio chosen by the administrator.
/// Multiple instances can be created, each pinned to a different studio.
/// </summary>
public sealed class StudioWidget : AdminWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StudioWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public StudioWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.admin.studio";

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Landscape;

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    protected override string? Route => WidgetRoutes.Studio;

    /// <inheritdoc/>
    protected override bool ApplyFilter(InternalItemsQuery query, string value)
    {
        var studioId = LibraryManager.GetStudioId(value);
        if (studioId == Guid.Empty)
        {
            return false;
        }

        query.StudioIds = [studioId];
        return true;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<AdminWidgetValue> GetAvailableValues(User user, string? search)
    {
        var result = LibraryManager.GetStudios(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true
        });

        return FilterAndProject(result.Items.Select(x => x.Item1.Name), search);
    }
}
