namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Contract that all JellyUX Homepage widgets must implement.
/// Each widget type is registered once in <see cref="IWidgetRegistry"/>; each configured row is
/// resolved to at most one instance per user session via <see cref="Resolve"/>.
/// </summary>
public interface IWidget
{
    /// <summary>
    /// Gets the unique type identifier for this widget (e.g. "continue-watching").
    /// Must be stable across restarts as it is persisted in user configurations.
    /// </summary>
    string WidgetType { get; }

    /// <summary>Gets the default display name shown in the UI when no custom name is configured.</summary>
    string DefaultDisplayName { get; }

    /// <summary>Gets the category that classifies this widget's data source.</summary>
    WidgetCategory Category { get; }

    /// <summary>Gets the default minimum number of items required to display this widget.</summary>
    int DefaultMinItems { get; }

    /// <summary>
    /// Resolves this widget's single instance for a given user and configuration row.
    /// Single-instance widgets (Native/Admin/Connected) return <c>this</c> unconditionally.
    /// Personalized widgets return null when <paramref name="rank"/> has no scored value for this
    /// user (see <see cref="Personalized.PersonalizedWidgetBase.Resolve"/>).
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="config">The resolved per-instance configuration.</param>
    /// <param name="rank">
    /// The 1-indexed rank of this row among rows sharing this widget's type (see
    /// <see cref="Jellyfin.Plugin.JuxHomepage.Widgets.WidgetLayoutResolver.BuildDescriptors(Guid, string?, CancellationToken)"/>).
    /// Ignored by every category except Personalized.
    /// </param>
    /// <returns>The resolved widget instance, or null if this row has nothing to show.</returns>
    IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank);

    /// <summary>
    /// Asynchronously fetches items for this widget instance.
    /// </summary>
    /// <param name="payload">The request payload containing user context and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="WidgetResult"/> containing the item page and total record count.</returns>
    Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a read-only descriptor for this widget instance, used by the front end to render
    /// the home screen layout.
    /// </summary>
    /// <returns>A <see cref="WidgetDescriptor"/> describing this instance.</returns>
    WidgetDescriptor GetDescriptor();
}
