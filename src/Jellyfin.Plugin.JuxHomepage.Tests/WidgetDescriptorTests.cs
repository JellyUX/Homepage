using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Annexe.a (phase 08): the descriptor sends only what a client reads.
/// </summary>
public class WidgetDescriptorTests
{
    [Fact]
    public void Serialize_LeavesOutTheOrder_AndKeepsWhatTheAdminCatalogReads()
    {
        var descriptor = new WidgetDescriptor { WidgetType = "jux.genre", Order = 5, MinItems = 2, Category = WidgetCategory.Personalized };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(descriptor));

        // The sections arrive already sorted; config.js reads Category and MinItems from Admin/Catalog.
        Assert.False(json.RootElement.TryGetProperty(nameof(WidgetDescriptor.Order), out _));
        Assert.True(json.RootElement.TryGetProperty(nameof(WidgetDescriptor.Category), out _));
        Assert.Equal(2, json.RootElement.GetProperty(nameof(WidgetDescriptor.MinItems)).GetInt32());
    }

    // KTW-01.a: a row of cards sends neither field; a featured section and a widget that cannot feature do.
    [Fact]
    public void Serialize_SendsFeaturedAndCanFeatureOnlyWhenTheyCarrySomething()
    {
        static JsonElement Of(WidgetDescriptor d) => JsonDocument.Parse(JsonSerializer.Serialize(d)).RootElement;

        var plain = Of(new WidgetDescriptor { WidgetType = "jux.genre" });
        var featured = Of(new WidgetDescriptor { WidgetType = "jux.genre", Featured = true });
        var native = Of(new WidgetDescriptor { WidgetType = "jux.native.next-up", CanFeature = false });

        Assert.False(plain.TryGetProperty(nameof(WidgetDescriptor.Featured), out _));
        Assert.False(plain.TryGetProperty(nameof(WidgetDescriptor.CanFeature), out _));
        Assert.True(featured.GetProperty(nameof(WidgetDescriptor.Featured)).GetBoolean());
        Assert.False(native.GetProperty(nameof(WidgetDescriptor.CanFeature)).GetBoolean());
    }
}
