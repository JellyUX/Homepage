using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Abstract base class for recently-added widgets.
/// Subclasses declare their target item kind; this class provides the shared query logic
/// that sorts by creation date and delegates pagination to Jellyfin.
/// </summary>
public abstract class RecentlyAddedWidgetBase : NativeWidgetBase
{
    /// <inheritdoc/>
    protected RecentlyAddedWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService)
        : base(userManager, libraryManager, dtoService)
    {
    }

    /// <summary>Gets the Jellyfin item kind this widget targets (Movie or Series).</summary>
    protected abstract BaseItemKind ItemKind { get; }

    /// <inheritdoc/>
    public override Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        User? user = UserManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();

        QueryResult<BaseItem> result = LibraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [ItemKind],
            OrderBy = [(ItemSortBy.DateCreated, SortOrder.Descending)],
            Recursive = true,
            IsMissing = false,
            EnableTotalRecordCount = false,
            StartIndex = payload.StartIndex,
            Limit = payload.Limit,
            DtoOptions = dtoOptions
        });

        var dtos = DtoService.GetBaseItemDtos(result.Items, dtoOptions, user);
        return Task.FromResult(new WidgetResult(dtos, dtos.Count));
    }
}
