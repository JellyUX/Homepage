namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Registry that holds all available widget implementations discovered at startup.
/// </summary>
public interface IWidgetRegistry
{
    /// <summary>
    /// Registers a widget implementation.
    /// </summary>
    /// <param name="widget">The widget to register.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a widget with the same <see cref="IWidget.WidgetType"/> is already registered.
    /// </exception>
    void Register(IWidget widget);

    /// <summary>
    /// Returns all registered widgets.
    /// </summary>
    /// <returns>A read-only collection of all registered widgets.</returns>
    IReadOnlyCollection<IWidget> GetAll();

    /// <summary>
    /// Looks up a widget by its type identifier.
    /// </summary>
    /// <param name="widgetType">The widget type identifier (case-sensitive).</param>
    /// <returns>The matching widget, or null if not found.</returns>
    IWidget? GetByType(string widgetType);
}
