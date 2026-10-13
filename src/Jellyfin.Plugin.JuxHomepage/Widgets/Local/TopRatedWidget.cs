using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Local;

/// <summary>
/// The library's best rated titles, highest community rating first (TODO_V4.md 9.1, F-61): a local
/// counterpart of the TMDb Top Rated lists, shown as "Top Rated in Library" so the two are never
/// confused. Unrated items are left out.
/// </summary>
public sealed class TopRatedWidget : LocalLibraryWidgetBase
{
    /// <summary>The lowest community rating a title needs to appear in the row.</summary>
    internal const float MinCommunityRating = 0.1f;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopRatedWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="timeProvider">Time source of the current day. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="itemFetcher">Runs the section query in its order; a private one when null (tests).</param>
    public TopRatedWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        TimeProvider? timeProvider = null,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, timeProvider, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.local.top-rated";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Top Rated in Library";

    /// <inheritdoc/>
    protected override string SortBy => WidgetSort.CommunityRating;

    /// <inheritdoc/>
    protected override void ApplyFilter(InternalItemsQuery query)
    {
        query.MinCommunityRating = MinCommunityRating;
    }
}
