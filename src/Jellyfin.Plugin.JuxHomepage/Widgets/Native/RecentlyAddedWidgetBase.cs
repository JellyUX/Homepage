using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Abstract base class for recently-added widgets.
/// Reads Jellyfin's own "latest media" (<see cref="IUserViewManager.GetLatestItems"/>, the logic of
/// the native <c>Items/Latest</c> route the home script reads), so the layout probe sees the same
/// items as the section: libraries the user excluded from latest media are left out, and played items
/// are hidden when the user asked for it (TODO_V4.md 8.3, F-36).
/// </summary>
public abstract class RecentlyAddedWidgetBase : NativeWidgetBase
{
    private readonly IUserViewManager _userViewManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecentlyAddedWidgetBase"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="userViewManager">Jellyfin user view manager, which computes the latest media.</param>
    protected RecentlyAddedWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IUserViewManager userViewManager)
        : base(userManager, libraryManager, dtoService)
    {
        _userViewManager = userViewManager;
    }

    /// <summary>Gets the item kind the latest media query reads.</summary>
    protected abstract BaseItemKind ItemKind { get; }

    /// <summary>
    /// Gets a value indicating whether items of the same parent are grouped into it (episodes into
    /// their series), as the native latest media of a library does.
    /// </summary>
    protected virtual bool GroupItems => false;

    /// <summary>
    /// Gets how many items the query reads per card: grouped episodes need several per series.
    /// </summary>
    protected virtual int LimitFactor => 1;

    /// <inheritdoc/>
    public override Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        User? user = UserManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();
        var wanted = payload.StartIndex + payload.Limit;

        // Same query and grouping as UserLibraryController.GetLatestMedia (10.11.9): a group of more
        // than one item shows its parent.
        var latest = _userViewManager.GetLatestItems(
            new LatestItemsQuery
            {
                User = user,
                IncludeItemTypes = [ItemKind],
                GroupItems = GroupItems,
                IsPlayed = user.HidePlayedInLatest ? false : null,
                Limit = wanted * LimitFactor
            },
            dtoOptions);

        var items = latest
            .Select(group => group.Item1 is not null && group.Item2.Count > 1 ? group.Item1 : group.Item2[0])
            .Skip(payload.StartIndex)
            .Take(payload.Limit)
            .ToList();

        var dtos = DtoService.GetBaseItemDtos(items, dtoOptions, user);
        return Task.FromResult(new WidgetResult(dtos, dtos.Count));
    }
}
