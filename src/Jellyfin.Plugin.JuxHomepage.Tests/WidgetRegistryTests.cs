using Jellyfin.Plugin.JuxHomepage.Widgets;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public class WidgetRegistryTests
{
    [Fact]
    public void Register_Widget_CanBeRetrievedByType()
    {
        var registry = new WidgetRegistry();
        var widget = MakeWidget("my-widget");

        registry.Register(widget);

        Assert.Same(widget, registry.GetByType("my-widget"));
    }

    [Fact]
    public void Register_DuplicateType_ThrowsInvalidOperationException()
    {
        var registry = new WidgetRegistry();

        registry.Register(MakeWidget("dupe"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(MakeWidget("dupe")));
    }

    [Fact]
    public void GetAll_ReturnsAllRegisteredWidgets()
    {
        var registry = new WidgetRegistry();
        var w1 = MakeWidget("alpha");
        var w2 = MakeWidget("beta");

        registry.Register(w1);
        registry.Register(w2);

        var all = registry.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(w1, all);
        Assert.Contains(w2, all);
    }

    [Fact]
    public void GetByType_UnknownType_ReturnsNull()
    {
        var registry = new WidgetRegistry();

        Assert.Null(registry.GetByType("ghost"));
    }

    [Fact]
    public void GetAll_EmptyRegistry_ReturnsEmptyCollection()
    {
        var registry = new WidgetRegistry();

        Assert.Empty(registry.GetAll());
    }

    private static IWidget MakeWidget(string widgetType)
    {
        var mock = new Mock<IWidget>();
        mock.Setup(w => w.WidgetType).Returns(widgetType);
        return mock.Object;
    }
}
