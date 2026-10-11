namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Cached home screen layout for a single user session.
/// </summary>
internal sealed class SessionData
{
    /// <summary>Gets or sets when this entry was built (UTC). Its age decides expiry under the TTL.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets when this entry was last accessed (UTC). Used for garbage collection only.</summary>
    public DateTime LastAccessed { get; set; }

    /// <summary>Gets or sets the ordered list of widget descriptors that passed MinItems filtering.</summary>
    public IReadOnlyList<WidgetDescriptor> Descriptors { get; set; } = [];
}
