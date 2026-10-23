using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Displays items belonging to a specific collection (BoxSet) chosen by the administrator.
/// The chosen value is the collection's item GUID (stored in ExtraParams["value"]).
/// Multiple instances can be created, each pinned to a different collection.
/// </summary>
public sealed class CollectionWidget : AdminWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public CollectionWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.admin.collection";

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    protected override string? Route => WidgetRoutes.Item;

    /// <summary>
    /// Returns <see langword="null"/> so all item types within the collection are included.
    /// </summary>
    protected override BaseItemKind[]? IncludeItemTypes => null;

    /// <inheritdoc/>
    protected override bool ApplyFilter(InternalItemsQuery query, string value)
    {
        if (!Guid.TryParse(value, out var collectionId))
        {
            return false;
        }

        if (LibraryManager.GetItemById(collectionId) is not Folder boxSet)
        {
            return false;
        }

        var items = boxSet.GetLinkedChildren(query.User);
        if (items.Count == 0)
        {
            return false;
        }

        query.ItemIds = items.Select(i => i.Id).ToArray();
        return true;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<AdminWidgetValue> GetAvailableValues(User user, string? search)
    {
        var result = LibraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
            EnableTotalRecordCount = false
        });

        return result.Items
            .Where(item => string.IsNullOrEmpty(search)
                || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new AdminWidgetValue(item.Id.ToString(), item.Name))
            .ToList()
            .AsReadOnly();
    }
}
