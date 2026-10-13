using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Displays items matching a specific tag chosen by the administrator.
/// Multiple instances can be created, each pinned to a different tag.
/// </summary>
public sealed class TagWidget : AdminWidgetBase
{
    private const int MaxTagItems = 2000;
    private const int MaxReturnedTags = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public TagWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.admin.tag";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Tag";

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    protected override string? Route => WidgetRoutes.Tag;

    /// <inheritdoc/>
    protected override bool ApplyFilter(InternalItemsQuery query, string value)
    {
        query.Tags = [value];
        return true;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<AdminWidgetValue> GetAvailableValues(User user, string? search)
    {
        // Jellyfin has no GetTags API; aggregate distinct tags from a bounded item query.
        var dtoOptions = new DtoOptions
        {
            Fields = [ItemFields.Tags],
            ImageTypeLimit = 0,
            ImageTypes = []
        };

        var result = LibraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true,
            EnableTotalRecordCount = false,
            Limit = MaxTagItems,
            DtoOptions = dtoOptions
        });

        var allTags = result.Items
            .SelectMany(item => item.Tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return FilterAndProject(allTags, search, MaxReturnedTags);
    }
}
