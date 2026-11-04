using Jellyfin.Plugin.JuxHomepage.Library;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Reads a page of a deferred section from its stored order (DR-033): the ids are resolved for the user (library
/// access and parental rules applied again), the titles the user played since are left out when the row hides them,
/// and the page is projected with live user data, as the widget would project it. No widget runs and no scoring.
/// </summary>
public sealed class StoredSectionReader
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredSectionReader"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="dtoService">The DTO projection service.</param>
    public StoredSectionReader(ILibraryManager libraryManager, IUserManager userManager, IDtoService dtoService)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _dtoService = dtoService;
    }

    /// <summary>Reads the requested page of a section from its stored ids.</summary>
    /// <param name="widget">The section's widget, which says whether its row hides played titles.</param>
    /// <param name="payload">The request: user, page, row parameters, featured state.</param>
    /// <param name="ids">The stored ids, in order.</param>
    /// <returns>The page, projected on its first read, and the number of stored ids still shown.</returns>
    public (WidgetResult Page, int Visible) Read(IWidget widget, WidgetPayload payload, IReadOnlyList<Guid> ids)
    {
        var user = _userManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return (WidgetResult.Empty, 0);
        }

        var items = widget is IHidesPlayedItems hides && hides.HidesPlayed(payload.ExtraParams)
            ? UserVisibleItems.ResolveUnplayed(_libraryManager, user, ids, out _)
            : UserVisibleItems.Resolve(_libraryManager, user, ids, out _);
        var page = items.Skip(payload.StartIndex).Take(payload.Limit).ToList();
        return (FeaturedItems.Project(_dtoService, page, payload, user), items.Count);
    }
}
