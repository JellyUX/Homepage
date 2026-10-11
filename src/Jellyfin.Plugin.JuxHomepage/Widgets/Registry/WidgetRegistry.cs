using System.Collections.Concurrent;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Thread-safe registry holding all available widget implementations.
/// Populated at startup via DI factory in <see cref="Jellyfin.Plugin.JuxHomepage.PluginServiceRegistrator"/>.
/// </summary>
public sealed class WidgetRegistry : IWidgetRegistry
{
    private readonly ConcurrentDictionary<string, IWidget> _widgets =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Register(IWidget widget)
    {
        if (!_widgets.TryAdd(widget.WidgetType, widget))
        {
            throw new InvalidOperationException(
                $"A widget with type '{widget.WidgetType}' is already registered.");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IWidget> GetAll() =>
        _widgets.Values.ToList().AsReadOnly();

    /// <inheritdoc/>
    public IWidget? GetByType(string widgetType)
    {
        _widgets.TryGetValue(widgetType, out var widget);
        return widget;
    }
}
