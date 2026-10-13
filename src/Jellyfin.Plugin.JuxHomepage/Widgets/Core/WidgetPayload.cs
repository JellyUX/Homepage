namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Request payload passed to <see cref="IWidget.GetItemsAsync"/> when fetching widget items.
/// </summary>
public sealed class WidgetPayload
{
    /// <summary>Gets or sets the requesting user's identifier.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets optional additional data that identifies this instance when the widget supports
    /// multiple instances (e.g. a library ID).
    /// </summary>
    public string? AdditionalData { get; set; }

    /// <summary>Gets or sets the zero-based start index for pagination.</summary>
    public int StartIndex { get; set; }

    /// <summary>Gets or sets the maximum number of items to return.</summary>
    public int Limit { get; set; } = 20;

    /// <summary>Gets or sets optional extra parameters for widget-specific filtering.</summary>
    public IReadOnlyDictionary<string, string>? ExtraParams { get; set; }

    /// <summary>Gets or sets the configured card order (a <see cref="WidgetSort"/> value).</summary>
    public string SortBy { get; set; } = WidgetSort.SortName;

    /// <summary>Gets or sets the direction of <see cref="SortBy"/>.</summary>
    public string SortOrder { get; set; } = WidgetSort.Ascending;

    /// <summary>
    /// Gets or sets the section's MaxItems: how many items a <see cref="WidgetSort.Random"/> draw
    /// keeps, whatever page of it this request reads.
    /// </summary>
    public int MaxItems { get; set; } = WidgetConfig.MaxItemsCeiling;
}
