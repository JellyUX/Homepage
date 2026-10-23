using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Native widget that shows the user's visible media libraries (views).
/// Uses <see cref="IUserViewManager.GetUserViews"/> to respect hidden-library preferences,
/// consistent with Jellyfin's own home screen behaviour.
/// </summary>
public sealed class MyMediaWidget : NativeWidgetBase
{
    private readonly IUserViewManager _userViewManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MyMediaWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="userViewManager">Jellyfin user view manager.</param>
    public MyMediaWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IUserViewManager userViewManager)
        : base(userManager, libraryManager, dtoService)
    {
        _userViewManager = userViewManager;
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.native.my-media";

    /// <inheritdoc/>
    public override int DefaultMinItems => 1;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Landscape;

    /// <inheritdoc/>
    public override string? Route => null;

    /// <inheritdoc/>
    protected override bool ReadNatively => false;

    /// <inheritdoc/>
    protected override bool CanFeature => false;

    /// <inheritdoc/>
    public override Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        User? user = UserManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();

        Folder[] folders = _userViewManager.GetUserViews(new UserViewQuery
        {
            User = user,
            IncludeHidden = false
        });

        int total = folders.Length;

        var page = folders.Skip(payload.StartIndex).Take(payload.Limit).ToList();

        return Task.FromResult(WidgetResult.Defer(
            page.Count,
            total,
            () => page.Select(f => DtoService.GetBaseItemDto(f, dtoOptions, user)).ToList().AsReadOnly()));
    }
}
