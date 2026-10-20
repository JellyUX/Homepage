namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Per-widget configuration stored in the plugin configuration (global) or user overrides.
/// </summary>
public sealed class WidgetConfig
{
    /// <summary>Gets or sets the widget type identifier this configuration applies to.</summary>
    public string WidgetType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a custom display name.
    /// When null, the widget's <see cref="IWidget.DefaultDisplayName"/> is used.
    /// </summary>
    public string? CustomDisplayName { get; set; }

    /// <summary>Gets or sets a value indicating whether this widget is visible on the home screen.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users may override this widget's settings.</summary>
    public bool AllowUserOverride { get; set; } = true;

    /// <summary>Gets or sets the display order on the home screen (lower values appear first).</summary>
    public int Order { get; set; }

    /// <summary>Gets or sets the minimum number of items required to display the widget.</summary>
    public int MinItems { get; set; } = 4;

    /// <summary>
    /// The highest <see cref="MaxItems"/> and the highest item count any request returns: a
    /// <c>limit</c> of 100000 would otherwise return the whole library (TODO_V4.md 5.5, F-21).
    /// </summary>
    public const int MaxItemsCeiling = 100;

    /// <summary>Gets or sets the maximum number of items to return per request.</summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>
    /// Gets or sets the view mode identifier.
    /// Use <see cref="WidgetViewMode"/> constants (Portrait, Landscape, Square).
    /// </summary>
    public string ViewMode { get; set; } = WidgetViewMode.Landscape;

    /// <summary>
    /// Gets or sets the card order of a library section (a <see cref="WidgetSort"/> value). Read by
    /// the Admin, Personalized (except Because You Watched) and Seasonal widgets only: the others keep
    /// their own order (recency, TMDb ranking, daily rotation).
    /// </summary>
    public string SortBy { get; set; } = WidgetSort.SortName;

    /// <summary>
    /// Gets or sets the direction of <see cref="SortBy"/> (<see cref="WidgetSort.Ascending"/> or
    /// <see cref="WidgetSort.Descending"/>), ignored by <see cref="WidgetSort.Random"/>.
    /// </summary>
    public string SortOrder { get; set; } = WidgetSort.Ascending;

    /// <summary>
    /// Gets or sets the minimum number of instances to create for this widget type.
    /// Retained for schema stability but no longer read by the widget engine as of TODO_V2.md
    /// Phase 8: every row is now assigned a rank by <see cref="WidgetLayoutResolver"/>
    /// instead (see <see cref="MaxInstances"/>).
    /// </summary>
    public int MinInstances { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum number of instances to create for this widget type. Historically the
    /// fan-out count passed to <see cref="IWidget.Resolve"/> for Personalized widgets; as of
    /// TODO_V2.md Phase 8, every category's row is instead assigned a 1-indexed rank by
    /// <see cref="WidgetLayoutResolver.BuildDescriptors(Guid, string?, CancellationToken)"/>, so this field is no longer read by
    /// the widget engine for any category. Retained on the model for schema stability.
    /// </summary>
    public int MaxInstances { get; set; } = 1;

    /// <summary>Gets or sets optional extra parameters for widget-specific configuration.</summary>
    public WidgetExtraParam[] ExtraParams { get; set; } = [];

    /// <summary>
    /// The <see cref="ExtraParams"/> key of a preset row's name: a translation key, shown in the
    /// user's language when <see cref="CustomDisplayName"/> is empty (TODO_V4.md 9.4, F-64).
    /// </summary>
    public const string NameKeyParam = "nameKey";
}
