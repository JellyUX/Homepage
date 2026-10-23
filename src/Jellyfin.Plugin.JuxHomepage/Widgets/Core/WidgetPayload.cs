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

    /// <summary>Gets or sets a value indicating whether the section shows as a banner (KTW-01).</summary>
    public bool Featured { get; set; }

    /// <summary>
    /// Gets or sets the lowest community rating the banner shows, 0 for none (KTW-02); 0 whenever
    /// <see cref="Featured"/> is false.
    /// </summary>
    public int FeaturedMinRating { get; set; }

    /// <summary>
    /// Sets <see cref="Featured"/> and <see cref="FeaturedMinRating"/> from <paramref name="row"/>, unless
    /// <paramref name="descriptor"/> says the widget cannot feature (DEC-P11-4).
    /// </summary>
    /// <param name="row">The section's configuration row, or null when none matches.</param>
    /// <param name="descriptor">The widget's descriptor; null states nothing, like a null <see cref="WidgetDescriptor.CanFeature"/>.</param>
    /// <returns>This payload.</returns>
    public WidgetPayload WithFeatured(WidgetConfig? row, WidgetDescriptor? descriptor)
    {
        Featured = row is { Featured: true } && descriptor?.CanFeature != false;
        FeaturedMinRating = Featured ? row!.FeaturedMinRating : 0;
        return this;
    }
}
