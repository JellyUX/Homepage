using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Native widget that shows the next unwatched episode for each series the user is watching.
/// </summary>
public sealed class NextUpWidget : NativeWidgetBase
{
    private readonly ITVSeriesManager _tvSeriesManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="NextUpWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="tvSeriesManager">Jellyfin TV series manager.</param>
    public NextUpWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ITVSeriesManager tvSeriesManager)
        : base(userManager, libraryManager, dtoService)
    {
        _tvSeriesManager = tvSeriesManager;
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.native.next-up";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Next Up";

    /// <inheritdoc/>
    public override int DefaultMinItems => 1;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Landscape;

    /// <inheritdoc/>
    public override string? Route => WidgetRoutes.NextUp;

    /// <inheritdoc/>
    public override Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        User? user = UserManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();

        QueryResult<BaseItem> result = _tvSeriesManager.GetNextUp(
            new NextUpQuery
            {
                User = user,
                Limit = payload.Limit,
                StartIndex = payload.StartIndex,
                EnableTotalRecordCount = false
            },
            dtoOptions);

        var dtos = DtoService.GetBaseItemDtos(result.Items, dtoOptions, user);
        return Task.FromResult(new WidgetResult(dtos, dtos.Count));
    }
}
