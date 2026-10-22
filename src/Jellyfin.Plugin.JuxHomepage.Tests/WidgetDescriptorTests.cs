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
}
