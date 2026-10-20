using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Native widget that shows in-progress media for the current user.
/// Items are ordered by most recently played, descending.
/// </summary>
public sealed class ContinueWatchingWidget : NativeWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContinueWatchingWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    public ContinueWatchingWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService)
        : base(userManager, libraryManager, dtoService)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.native.continue-watching";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Continue Watching";

    /// <inheritdoc/>
    public override int DefaultMinItems => 1;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Landscape;

    /// <inheritdoc/>
    public override string? Route => null;

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
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            IsResumable = true,
            Recursive = true,
            MediaTypes = [MediaType.Video],
            IsVirtualItem = false,
            CollapseBoxSetItems = false,
            EnableTotalRecordCount = false,
            StartIndex = payload.StartIndex,
            Limit = payload.Limit,
            DtoOptions = dtoOptions
        });

        return Task.FromResult(WidgetResult.Project(DtoService, result.Items, dtoOptions, user));
    }
}
