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
    [InlineData("jux.seasonal.halloween", "10-01", "10-31", "halloween", "tag", "halloween")]
    [InlineData("jux.seasonal.halloween-horror", "10-01", "10-31", "halloween", "genre", "Horror|Horreur")]
    [InlineData("jux.seasonal.halloween-thrillers", "10-01", "10-31", "halloween", "genre", "Thriller")]
    [InlineData("jux.seasonal.christmas", "12-01", "12-25", "christmas", "tag", "christmas")]
    [InlineData("jux.seasonal.valentines", "02-07", "02-14", "valentines", "tag", "valentine's day")]
    [InlineData("jux.seasonal.valentines-romance", "02-07", "02-14", "valentines", "genre", "Romance")]
    [InlineData("jux.seasonal.newyear", "12-26", "01-06", "newyear", "tag", "new year's eve")]
    public void Build_PresetHasExpectedDatesThemeAndOneFilter(
        string nameKey,
        string expectedStart,
        string expectedEnd,
        string expectedTheme,
        string filterKey,
        string filterValue)
    {
        var preset = SeasonalWidgetDefaults.Build().Single(p => p.ExtraParams.Any(e => e.Key == "nameKey" && e.Value == nameKey));

        // Named by a translated key, never by an English name (TODO_V4.md 9.4, F-64).
        Assert.Null(preset.CustomDisplayName);

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
