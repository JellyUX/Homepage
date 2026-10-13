using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Displays items from a specific production year chosen by the administrator.
/// Multiple instances can be created, each pinned to a different year.
/// </summary>
public sealed class YearWidget : AdminWidgetBase
{
    private const int MaxScanItems = 5000;

    /// <summary>
    /// Initializes a new instance of the <see cref="YearWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public YearWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.admin.year";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Year";

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    protected override bool ApplyFilter(InternalItemsQuery query, string value)
    {
        if (!int.TryParse(value, out var year))
        {
            return false;
        }

        query.Years = [year];
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deliberately not built on <see cref="AdminWidgetBase.FilterAndProject"/>: years are matched by
    /// case-sensitive ordinal prefix (not substring, case-insensitive) and sorted numerically
    /// descending (not alphabetically ascending), so folding this into the shared helper would change
    /// real behavior, not just remove duplication.
    /// </remarks>
    public override IReadOnlyList<AdminWidgetValue> GetAvailableValues(User user, string? search)
    {
        // Aggregate distinct production years from a bounded item query.
        var result = LibraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true,
            EnableTotalRecordCount = false,
            Limit = MaxScanItems,
            DtoOptions = new DtoOptions { Fields = [], ImageTypeLimit = 0, ImageTypes = [] }
        });

        var years = result.Items
            .Select(item => item.ProductionYear)
            .Where(y => y.HasValue)
            .Select(y => y!.Value)
            .Distinct()
            .OrderByDescending(y => y)
            .Select(y => y.ToString());

        if (!string.IsNullOrEmpty(search))
        {
            years = years.Where(y => y.StartsWith(search, StringComparison.Ordinal));
        }

        return years
            .Select(y => new AdminWidgetValue(y, y))
            .ToList()
            .AsReadOnly();
    }
}
