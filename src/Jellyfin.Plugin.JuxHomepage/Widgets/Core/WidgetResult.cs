using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Result returned by <see cref="IWidget.GetItemsAsync"/>.
/// Encapsulates the item page and the total record count independently of Jellyfin's internal
/// QueryResult type, keeping widget implementations decoupled from library internals.
/// </summary>
public sealed class WidgetResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetResult"/> class.
    /// </summary>
    /// <param name="items">The page of items to return.</param>
    /// <param name="totalRecordCount">The total number of matching records before pagination.</param>
    public WidgetResult(IReadOnlyList<BaseItemDto> items, int totalRecordCount)
    {
        Items = items;
        TotalRecordCount = totalRecordCount;
    }

    /// <summary>Gets the page of items for this result.</summary>
    public IReadOnlyList<BaseItemDto> Items { get; }

    /// <summary>
    /// Gets the total number of matching records before pagination, or the page size for widgets
    /// that skip the count query (F-05): no home section reads the full total.
    /// </summary>
    public int TotalRecordCount { get; }

    /// <summary>Gets an empty result with no items and a count of zero.</summary>
    public static WidgetResult Empty { get; } = new WidgetResult([], 0);
}
