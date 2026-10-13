using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Configuration;

/// <summary>
/// Per-user configuration files have their own schema version (initial value 8, above the plugin
/// constant 7 that earlier versions stamped on them). Each test starts from one on-disk state.
/// </summary>
public sealed class UserConfigurationMigrationTests
{
    private const string PersonalizedType = "jux.personalized.favorite-genre";

    private static UserConfiguration FromJson(string json) => JsonSerializer.Deserialize<UserConfiguration>(json)!;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void NullOverrides_BecomeEmptyAtEveryTier(int schemaVersion)
    {
        var config = FromJson($$"""{"WidgetOverrides":null,"SchemaVersion":{{schemaVersion}}}""");

        Plugin.MigrateUserConfiguration(config);

        Assert.NotNull(config.WidgetOverrides);
        Assert.Empty(config.WidgetOverrides);
        Assert.Equal(10, config.SchemaVersion);
    }

    [Fact]
    public void FromTierOne_ExplodesPersonalizedFanOut()
    {
        var config = new UserConfiguration
        {
            SchemaVersion = 1,
            WidgetOverrides = [new WidgetConfig { WidgetType = PersonalizedType, MaxInstances = 3 }]
        };

        Plugin.MigrateUserConfiguration(config);

        Assert.Equal(3, config.WidgetOverrides.Count(w => w.WidgetType == PersonalizedType));
        Assert.Equal(10, config.SchemaVersion);
    }

    [Fact]
    public void FileStampedSevenByThePluginConstant_IsNotMigratedAgain()
    {
        // Earlier versions stamped user files with the plugin constant (7): the V1-to-V2 tier already
        // ran for them and must not run a second time.
        var config = new UserConfiguration
        {
            SchemaVersion = 7,
            WidgetOverrides = [new WidgetConfig { WidgetType = PersonalizedType, MaxInstances = 3 }]
        };

        Plugin.MigrateUserConfiguration(config);

        Assert.Single(config.WidgetOverrides);
        Assert.Equal(10, config.SchemaVersion);
    }

    [Fact]
    public void FromTierEight_GivesEveryOverrideAKnownSort()
    {
        var row = new WidgetConfig { WidgetType = PersonalizedType, SortBy = "Bogus" };
        var config = new UserConfiguration { SchemaVersion = 8, WidgetOverrides = [row] };

        Plugin.MigrateUserConfiguration(config);

        Assert.Equal(WidgetSort.SortName, row.SortBy);
        Assert.Equal(10, config.SchemaVersion);
    }

    [Fact]
    public void FromTierNine_NullHiddenSectionsBecomeEmpty()
    {
        var config = FromJson("""{"HiddenSections":null,"SchemaVersion":9}""");

        Plugin.MigrateUserConfiguration(config);

        Assert.NotNull(config.HiddenSections);
        Assert.Empty(config.HiddenSections);
        Assert.Equal(10, config.SchemaVersion);
    }

    [Fact]
    public void AtCurrentVersion_IsLeftUnchanged()
    {
        var row = new WidgetConfig { WidgetType = PersonalizedType, MaxInstances = 3 };
        var config = new UserConfiguration { SchemaVersion = 10, WidgetOverrides = [row] };

        Plugin.MigrateUserConfiguration(config);

        Assert.Same(row, Assert.Single(config.WidgetOverrides));
        Assert.Equal(10, config.SchemaVersion);
    }
}
