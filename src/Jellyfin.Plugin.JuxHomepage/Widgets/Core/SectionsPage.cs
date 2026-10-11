namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// One page of a user's home screen layout, as returned by <c>GET JuxHomepage/Sections</c>.
/// </summary>
/// <param name="Sections">The widget descriptors of this page.</param>
/// <param name="HasMore">Whether a later page holds more descriptors, so the client never requests
/// a page past the end of the layout.</param>
public sealed record SectionsPage(IReadOnlyList<WidgetDescriptor> Sections, bool HasMore);
