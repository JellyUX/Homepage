using System.Runtime.CompilerServices;
using Jellyfin.Plugin.JuxHomepage.Widgets;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>Every widget class of the plugin, by widget type, without a DI container.</summary>
internal static class WidgetTypes
{
    private static readonly Lazy<IReadOnlyDictionary<string, Type>> _all = new(() =>
        typeof(IWidget).Assembly.GetTypes()
            .Where(t => typeof(IWidget).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToDictionary(t => Instance(t).WidgetType, t => t));

    /// <summary>Gets the widget classes by widget type.</summary>
    internal static IReadOnlyDictionary<string, Type> All => _all.Value;

    /// <summary>
    /// Returns a widget built without running its constructor: enough for the members that return a
    /// constant or derive from <see cref="IWidget.WidgetType"/>.
    /// </summary>
    /// <param name="type">The widget class.</param>
    /// <returns>The uninitialized widget.</returns>
    internal static IWidget Instance(Type type) => (IWidget)RuntimeHelpers.GetUninitializedObject(type);
}
