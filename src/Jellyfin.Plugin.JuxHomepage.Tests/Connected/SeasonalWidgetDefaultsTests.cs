using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Connected;

public sealed class SeasonalWidgetDefaultsTests
{
    [Fact]
    public void Build_ReturnsSevenDisabledPresets()
    {
        var presets = SeasonalWidgetDefaults.Build();

        Assert.Equal(7, presets.Length);
        Assert.All(presets, p =>
        {
            Assert.Equal("jux.connected.seasonal", p.WidgetType);
            Assert.False(p.Enabled);
        });
    }

    [Fact]
    public void Build_EachPresetHasAUniqueFixedValueGuid()
    {
        var presets = SeasonalWidgetDefaults.Build();

        var ids = presets
            .Select(p => p.ExtraParams.Single(e => e.Key == "value").Value)
            .ToList();

        Assert.Equal(7, ids.Distinct().Count());

        // Fixed, not freshly generated each call -- required for the Plugin.cs migration
        // (AppendSeasonalPresetsIfMissing) to stay idempotent across repeated calls.
        var idsSecondCall = SeasonalWidgetDefaults.Build()
            .Select(p => p.ExtraParams.Single(e => e.Key == "value").Value)
            .ToList();
        Assert.Equal(ids, idsSecondCall);
    }

    [Theory]
    [InlineData("Halloween", "10-01", "10-31", "halloween", "tag", "halloween")]
    [InlineData("Halloween Horror", "10-01", "10-31", "halloween", "genre", "Horror|Horreur")]
    [InlineData("Halloween Thrillers", "10-01", "10-31", "halloween", "genre", "Thriller")]
    [InlineData("Christmas", "12-01", "12-25", "christmas", "tag", "christmas")]
    [InlineData("Valentine's Day", "02-07", "02-14", "valentines", "tag", "valentine's day")]
    [InlineData("Valentine's Romance", "02-07", "02-14", "valentines", "genre", "Romance")]
    [InlineData("New Year", "12-26", "01-06", "newyear", "tag", "new year's eve")]
    public void Build_PresetHasExpectedDatesThemeAndOneFilter(
        string displayName,
        string expectedStart,
        string expectedEnd,
        string expectedTheme,
        string filterKey,
        string filterValue)
    {
        var preset = SeasonalWidgetDefaults.Build().Single(p => p.CustomDisplayName == displayName);

        Assert.Equal(expectedStart, preset.ExtraParams.Single(e => e.Key == "startDate").Value);
        Assert.Equal(expectedEnd, preset.ExtraParams.Single(e => e.Key == "endDate").Value);
        Assert.Equal(expectedTheme, preset.ExtraParams.Single(e => e.Key == "theme").Value);
        var filter = Assert.Single(preset.ExtraParams, e => e.Key == "genre" || e.Key == "tag");
        Assert.Equal((filterKey, filterValue), (filter.Key, filter.Value));
    }

    [Fact]
    public void AddedInSchema11_AreTheThreeNewPresets()
    {
        var ids = SeasonalWidgetDefaults.Build().Select(p => Guid.Parse(p.ExtraParams.Single(e => e.Key == "value").Value)).ToList();

        Assert.Equal(ids.Skip(1).Take(2).Append(ids[5]), SeasonalWidgetDefaults.AddedInSchema11);
    }
}
