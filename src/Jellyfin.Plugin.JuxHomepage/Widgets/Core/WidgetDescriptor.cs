using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Read-only descriptor returned by <see cref="IWidget.GetDescriptor"/>.
/// Describes a widget instance to the front end without exposing internal implementation details.
/// </summary>
public sealed class WidgetDescriptor
{
    /// <summary>Gets or sets the unique type identifier for this widget.</summary>
    public string WidgetType { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name shown in the UI for this instance.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the widget category.</summary>
    public WidgetCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the view mode identifier.
    /// Use <see cref="WidgetViewMode"/> constants.
    /// </summary>
    public string ViewMode { get; set; } = WidgetViewMode.Landscape;

    /// <summary>
    /// Gets or sets a value indicating whether the section shows as a banner (KTW-01): its row sets
    /// <see cref="WidgetConfig.Featured"/> and the widget can feature. Sent only when true.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Featured { get; set; }

    /// <summary>
    /// Gets or sets whether a row of this widget may be featured: false for a section the client reads
    /// from a native route and for My Media (DEC-P11-4), null (sent as nothing) when it may. Read by the
    /// admin page from Admin/Catalog.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanFeature { get; set; }

    /// <summary>Gets or sets the front-end route for browsing items of this type, if applicable.</summary>
    public string? Route { get; set; }

    /// <summary>
    /// Gets or sets optional additional data that identifies this instance when multiple instances
    /// of the same widget type are present.
    /// </summary>
    public string? AdditionalData { get; set; }

    /// <summary>
    /// Gets or sets the key of the configuration row this section comes from (its instance GUID, or
    /// <c>type#rank</c>, see <see cref="WidgetLayoutResolver.SectionKeys"/>). The client sends it back
    /// when it loads the section, so two rows of one type each read their own settings
    /// (TODO_V4.md 9.3, F-27). Null outside the home screen layout.
    /// </summary>
    public string? SectionKey { get; set; }

    /// <summary>
    /// Gets or sets the display order on the home screen (lower values appear first). The server sends
    /// the sections already in this order, so no client reads it: not sent (Annexe.a).
    /// </summary>
    [JsonIgnore]
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the row the admin page adds for this widget type (QUAL-03.c): set on the
    /// Admin/Catalog descriptors only, not sent elsewhere.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetConfig? DefaultRow { get; set; }

    /// <summary>Gets or sets the minimum number of items required to display this widget.</summary>
    public int MinItems { get; set; }

    /// <summary>
    /// Gets or sets the number of items the client requests for this section: the configured
    /// <see cref="WidgetConfig.MaxItems"/>, at most <see cref="WidgetConfig.MaxItemsCeiling"/>.
    /// </summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>
    /// Gets or sets how many items the native query of a Recently Added section reads per card (1
    /// elsewhere). The client reads the native route with it, so the server probe and the section read
    /// the same items from one value (PERF-03.c).
    /// </summary>
    public int LimitFactor { get; set; } = 1;

    /// <summary>
    /// Gets or sets how many cards one series may take in a native Recently Added section, or null for
    /// no cap. The client applies the same cap as the server probe (PERF-03.c).
    /// </summary>
    public int? MaxPerSeries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client reads this section from a native Jellyfin
    /// route (<c>NATIVE_ROUTES</c> of the client script) instead of the Section endpoint: the layout
    /// probe then only counts <see cref="MinItems"/> items and keeps no page (PERF-03.b). Not sent.
    /// </summary>
    [JsonIgnore]
    public bool ReadNatively { get; set; }

    /// <summary>
    /// Gets or sets an optional visual theme identifier for this instance (e.g. <c>halloween</c>,
    /// <c>christmas</c>, <c>valentines</c>, <c>newyear</c>). Populated generically by
    /// <see cref="WidgetLayoutResolver"/> from a widget instance's <c>ExtraParams["theme"]</c> when
    /// present; not tied to any specific widget type. Null when the widget defines no theme.
    /// </summary>
    public string? Theme { get; set; }
}
