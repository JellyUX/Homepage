using System.IO;
using System.Xml.Serialization;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class PluginValidationTests
{
    [Fact]
    public void ValidateConfiguration_ClampsCacheValues()
    {
        var config = new PluginConfiguration
        {
            Cache = new CacheConfig { SessionTtlMinutes = 0, TMDbRefreshIntervalHours = 0 }
        };

        Plugin.ValidateConfiguration(config);

        Assert.Equal(1, config.Cache.SessionTtlMinutes);
        Assert.Equal(1, config.Cache.TMDbRefreshIntervalHours);
    }

    [Fact]
    public void ValidateConfiguration_ClampsWidgetItemAndInstanceRanges()
    {
        var config = new PluginConfiguration
        {
            Widgets =
            [
                new WidgetConfig
                {
                    WidgetType = "jux.admin.genre",
                    MinItems = -5,
                    MaxItems = 0,
                    MinInstances = 0,
                    MaxInstances = 0
                }
            ]
        };

        Plugin.ValidateConfiguration(config);

        var widget = config.Widgets[0];
        Assert.Equal(0, widget.MinItems);
        Assert.Equal(1, widget.MaxItems);
        Assert.True(widget.MinItems <= widget.MaxItems);
        Assert.Equal(1, widget.MinInstances);
        Assert.Equal(1, widget.MaxInstances);
        Assert.True(widget.MinInstances <= widget.MaxInstances);
    }

    [Fact]
    public void ValidateConfiguration_ClampsMinItemsAboveMaxItemsDownToMaxItems()
    {
        var config = new PluginConfiguration
        {
            Widgets = [new WidgetConfig { WidgetType = "jux.admin.genre", MinItems = 50, MaxItems = 10 }]
        };

        Plugin.ValidateConfiguration(config);

        Assert.Equal(10, config.Widgets[0].MinItems);
        Assert.Equal(10, config.Widgets[0].MaxItems);
    }

    [Fact]
    public void ValidateConfiguration_TrimsApiKeyAndNullsBlankValue()
    {
        var config = new PluginConfiguration { ApiKeys = new ApiKeysConfig { TMDb = "  abc123  " } };
        Plugin.ValidateConfiguration(config);
        Assert.Equal("abc123", config.ApiKeys.TMDb);

        var blankConfig = new PluginConfiguration { ApiKeys = new ApiKeysConfig { TMDb = "   " } };
        Plugin.ValidateConfiguration(blankConfig);
        Assert.Null(blankConfig.ApiKeys.TMDb);
    }

    [Fact]
    public void ValidateConfiguration_ClampsTMDbListPageCountsAndNormalizesRegion()
    {
        var config = new PluginConfiguration
        {
            TMDbLists = new TMDbListsConfig
            {
                TrendingMoviesPages = 99,
                TrendingShowsPages = 0,
                AiringTodayPages = 99,
                TopRatedMoviesPages = 0,
                TopRatedShowsPages = 99,
                NowPlayingMoviesPages = 0,
                NowPlayingRegion = "  fr  "
            }
        };

        Plugin.ValidateConfiguration(config);

        Assert.Equal(5, config.TMDbLists.TrendingMoviesPages);
        Assert.Equal(1, config.TMDbLists.TrendingShowsPages);
        Assert.Equal(5, config.TMDbLists.AiringTodayPages);
        Assert.Equal(1, config.TMDbLists.TopRatedMoviesPages);
        Assert.Equal(5, config.TMDbLists.TopRatedShowsPages);
        Assert.Equal(1, config.TMDbLists.NowPlayingMoviesPages);
        Assert.Equal("FR", config.TMDbLists.NowPlayingRegion);
    }

    [Fact]
    public void MigrateConfiguration_NoOpForCurrentSchemaVersion_DoesNotThrow()
    {
        var config = new PluginConfiguration { SchemaVersion = 8 };

        var exception = Record.Exception(() => Plugin.MigrateConfiguration(config));

        Assert.Null(exception);
        Assert.Equal(8, config.SchemaVersion);
        Assert.Empty(config.Widgets);
    }

    [Fact]
    public void MigrateConfiguration_PersonalizedRowWithMaxInstancesGreaterThanOne_ExplodesIntoIndependentRows()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 1,
            Widgets =
            [
                new WidgetConfig
                {
                    WidgetType = "jux.personalized.favorite-genre",
                    CustomDisplayName = "My Genres",
                    Order = 20,
                    MaxInstances = 3,
                    ExtraParams = [new WidgetExtraParam { Key = "excludeWatched", Value = "false" }]
                }
            ]
        };

        Plugin.MigrateConfiguration(config);

        // Starting below the V1->V2 (fan-out), V2->V3 (append Watchlist widget), V5->V6 (append
        // seasonal presets), and V6->V7 (append Recently Added Episodes) thresholds, all four
        // migrations apply in order -- the fan-out first (3 rows), then the Watchlist widget row (4),
        // then the four seasonal preset rows (8), then the Recently Added Episodes row (9 total).
        Assert.Equal(8, config.SchemaVersion);
        Assert.Equal(9, config.Widgets.Length);

        Assert.Equal("jux.personalized.favorite-genre", config.Widgets[0].WidgetType);
        Assert.Equal("My Genres", config.Widgets[0].CustomDisplayName);
        Assert.Equal(20, config.Widgets[0].Order);
        Assert.Equal(1, config.Widgets[0].MaxInstances);
        Assert.Single(config.Widgets[0].ExtraParams);

        Assert.Equal("jux.personalized.favorite-genre", config.Widgets[1].WidgetType);
        Assert.Null(config.Widgets[1].CustomDisplayName);
        Assert.Equal(30, config.Widgets[1].Order);
        Assert.Equal(1, config.Widgets[1].MaxInstances);
        Assert.Empty(config.Widgets[1].ExtraParams);

        Assert.Equal("jux.personalized.favorite-genre", config.Widgets[2].WidgetType);
        Assert.Null(config.Widgets[2].CustomDisplayName);
        Assert.Equal(40, config.Widgets[2].Order);
        Assert.Equal(1, config.Widgets[2].MaxInstances);

        Assert.Equal("jux.native.watchlist", config.Widgets[3].WidgetType);

        Assert.Equal(4, config.Widgets.Count(w => w.WidgetType == "jux.connected.seasonal"));
        Assert.Contains(config.Widgets, w => w.WidgetType == "jux.native.recently-added-episodes");
    }

    [Fact]
    public void MigrateConfiguration_NonPersonalizedRowWithMaxInstancesGreaterThanOne_LeftUnchanged()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 1,
            Widgets = [new WidgetConfig { WidgetType = "jux.admin.genre", MaxInstances = 5 }]
        };

        Plugin.MigrateConfiguration(config);

        Assert.Equal(8, config.SchemaVersion);

        // The original row passes through the V1->V2 fan-out untouched (not Personalized), then the
        // V2->V3 migration appends the new native Watchlist widget row, then V5->V6 appends the four
        // seasonal preset rows, then V6->V7 appends the Recently Added Episodes row.
        Assert.Equal(7, config.Widgets.Length);
        var widget = config.Widgets.Single(w => w.WidgetType == "jux.admin.genre");
        Assert.Equal(5, widget.MaxInstances);
        Assert.Contains(config.Widgets, w => w.WidgetType == "jux.native.watchlist");
        Assert.Equal(4, config.Widgets.Count(w => w.WidgetType == "jux.connected.seasonal"));
        Assert.Contains(config.Widgets, w => w.WidgetType == "jux.native.recently-added-episodes");
    }

    [Fact]
    public void MigrateConfiguration_SchemaVersionTwo_AppendsWatchlistWidgetRow()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 2,
            Widgets = [new WidgetConfig { WidgetType = "jux.native.continue-watching", Order = 0 }]
        };

        Plugin.MigrateConfiguration(config);

        Assert.Equal(8, config.SchemaVersion);
        Assert.Equal(7, config.Widgets.Length);
        var watchlistWidget = config.Widgets.Single(w => w.WidgetType == "jux.native.watchlist");
        Assert.True(watchlistWidget.Enabled);
        Assert.Equal(50, watchlistWidget.Order);
        Assert.Equal(4, config.Widgets.Count(w => w.WidgetType == "jux.connected.seasonal"));
        Assert.Contains(config.Widgets, w => w.WidgetType == "jux.native.recently-added-episodes");
    }

    [Fact]
    public void MigrateConfiguration_WatchlistWidgetRowAlreadyPresent_DoesNotDuplicateIt()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 2,
            Widgets =
            [
                new WidgetConfig { WidgetType = "jux.native.continue-watching", Order = 0 },
                new WidgetConfig { WidgetType = "jux.native.watchlist", Order = 99, Enabled = false }
            ]
        };

        Plugin.MigrateConfiguration(config);

        Assert.Equal(8, config.SchemaVersion);
        Assert.Equal(7, config.Widgets.Length);
        var watchlistWidget = config.Widgets.Single(w => w.WidgetType == "jux.native.watchlist");

        // Untouched -- the migration must not overwrite an existing row (e.g. one the admin has
        // already customized), only append one when the type is entirely absent.
        Assert.Equal(99, watchlistWidget.Order);
        Assert.False(watchlistWidget.Enabled);
        Assert.Equal(4, config.Widgets.Count(w => w.WidgetType == "jux.connected.seasonal"));
        Assert.Contains(config.Widgets, w => w.WidgetType == "jux.native.recently-added-episodes");
    }

    [Fact]
    public void MigrateConfiguration_SeasonalPresetsAlreadyPresent_DoesNotDuplicateThem()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 6,
            Widgets = [.. Widgets.Connected.SeasonalWidgetDefaults.Build()]
        };

        Plugin.MigrateConfiguration(config);

        // Starting at SchemaVersion 6, the seasonal presets are already present (no duplication), but
        // the V6->V7 migration still fires and appends the new Recently Added Episodes row.
        Assert.Equal(8, config.SchemaVersion);
        Assert.Equal(5, config.Widgets.Length);
        Assert.Equal(4, config.Widgets.Count(w => w.WidgetType == "jux.connected.seasonal"));
        Assert.Contains(config.Widgets, w => w.WidgetType == "jux.native.recently-added-episodes");
    }

    [Fact]
    public void MigrateConfiguration_SchemaVersionFive_AppendsSeasonalPresetRows()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 5,
            Widgets = [new WidgetConfig { WidgetType = "jux.native.continue-watching", Order = 0 }]
        };

        Plugin.MigrateConfiguration(config);

        Assert.Equal(8, config.SchemaVersion);
        Assert.Equal(6, config.Widgets.Length);
        Assert.Equal(4, config.Widgets.Count(w => w.WidgetType == "jux.connected.seasonal"));
        Assert.All(
            config.Widgets.Where(w => w.WidgetType == "jux.connected.seasonal"),
            w => Assert.False(w.Enabled));
        Assert.Contains(config.Widgets, w => w.WidgetType == "jux.native.recently-added-episodes");
    }

    [Fact]
    public void MigrateConfiguration_SchemaVersionSix_AppendsRecentlyAddedEpisodesWidgetRow()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 6,
            Widgets = [new WidgetConfig { WidgetType = "jux.native.continue-watching", Order = 0 }]
        };

        Plugin.MigrateConfiguration(config);

        Assert.Equal(8, config.SchemaVersion);
        Assert.Equal(2, config.Widgets.Length);
        var episodesWidget = config.Widgets.Single(w => w.WidgetType == "jux.native.recently-added-episodes");
        Assert.True(episodesWidget.Enabled);
        Assert.Equal(35, episodesWidget.Order);
    }

    [Fact]
    public void MigrateConfiguration_RecentlyAddedEpisodesWidgetRowAlreadyPresent_DoesNotDuplicateIt()
    {
        var config = new PluginConfiguration
        {
            SchemaVersion = 6,
            Widgets =
            [
                new WidgetConfig { WidgetType = "jux.native.continue-watching", Order = 0 },
                new WidgetConfig { WidgetType = "jux.native.recently-added-episodes", Order = 99, Enabled = false }
            ]
        };

        Plugin.MigrateConfiguration(config);

        Assert.Equal(8, config.SchemaVersion);
        Assert.Equal(2, config.Widgets.Length);
        var episodesWidget = config.Widgets.Single(w => w.WidgetType == "jux.native.recently-added-episodes");

        // Untouched -- the migration must not overwrite an existing row, only append one when the
        // type is entirely absent.
        Assert.Equal(99, episodesWidget.Order);
        Assert.False(episodesWidget.Enabled);
    }

    [Fact]
    public void LoadChain_FreshInstall_SeedsAllSevenNativeWidgetsEnabled()
    {
        // Same order as the Plugin constructor: a fresh install starts from the default instance.
        var config = new PluginConfiguration();

        Plugin.MigrateConfiguration(config);
        Plugin.ValidateConfiguration(config);

        var nativeTypes = Widgets.Native.NativeWidgetDefaults.Build().Select(w => w.WidgetType).ToArray();
        Assert.Equal(7, nativeTypes.Length);
        foreach (var type in nativeTypes)
        {
            var row = Assert.Single(config.Widgets, w => w.WidgetType == type);
            Assert.True(row.Enabled, type);
        }

        Assert.Equal(
            Widgets.Connected.SeasonalWidgetDefaults.Build().Length,
            config.Widgets.Count(w => w.WidgetType == "jux.connected.seasonal"));
    }

    [Fact]
    public void MigrateConfiguration_V7WithNilModules_EnablesEveryModule()
    {
        var config = new PluginConfiguration { SchemaVersion = 7, Modules = null! };

        Plugin.MigrateConfiguration(config);

        Assert.Equal(8, config.SchemaVersion);
        Assert.True(config.Modules.Tabs);
        Assert.True(config.Modules.CardEnhancements);
        Assert.True(config.Modules.SeriesFlatten);
        Assert.True(config.Modules.Collections);
        Assert.True(config.Modules.Breadcrumb);
    }

    [Fact]
    public void Deserialize_V7XmlWithoutModulesElement_EnablesEveryModule()
    {
        const string Xml = "<?xml version=\"1.0\"?><PluginConfiguration><Enabled>true</Enabled><SchemaVersion>7</SchemaVersion></PluginConfiguration>";

        var config = (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(new StringReader(Xml))!;
        Plugin.MigrateConfiguration(config);

        Assert.True(config.Modules.Tabs);
        Assert.True(config.Modules.CardEnhancements);
        Assert.True(config.Modules.SeriesFlatten);
        Assert.True(config.Modules.Collections);
        Assert.True(config.Modules.Breadcrumb);
    }

    [Fact]
    public void Deserialize_PartialModulesElement_KeepsTheDefaultsOfMissingSwitches()
    {
        const string Xml = "<?xml version=\"1.0\"?><PluginConfiguration><SchemaVersion>8</SchemaVersion><Modules><Tabs>false</Tabs></Modules></PluginConfiguration>";

        var config = (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(new StringReader(Xml))!;
        Plugin.ValidateConfiguration(config);

        Assert.False(config.Modules.Tabs);
        Assert.True(config.Modules.Collections);
    }

    [Fact]
    public void ValidateConfiguration_CapsMaxItemsAtTheCeiling()
    {
        var config = new PluginConfiguration
        {
            Widgets = [new WidgetConfig { WidgetType = "jux.admin.genre", MinItems = 4, MaxItems = 100000 }]
        };

        Plugin.ValidateConfiguration(config);

        Assert.Equal(WidgetConfig.MaxItemsCeiling, config.Widgets[0].MaxItems);
    }

    [Fact]
    public void ValidateConfiguration_DropsUnparseableSeasonalDatesOnly()
    {
        var config = new PluginConfiguration
        {
            Widgets =
            [
                new WidgetConfig
                {
                    WidgetType = "jux.connected.seasonal",
                    ExtraParams =
                    [
                        new WidgetExtraParam { Key = "value", Value = "preset-1" },
                        new WidgetExtraParam { Key = "startDate", Value = "13-40" },
                        new WidgetExtraParam { Key = "endDate", Value = "02-29" }
                    ]
                }
            ]
        };

        Plugin.ValidateConfiguration(config);

        Assert.Equal(["value", "endDate"], config.Widgets[0].ExtraParams.Select(p => p.Key));
    }
}
