using Jellyfin.Plugin.JuxHomepage.Inject;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

public sealed class CoexistenceWarningsTests
{
    private static readonly Guid CustomTabsId = Guid.Parse("fbacd0b6-fd46-4a05-b0a4-2045d6a135b0");

    private static readonly Guid FileTransformationId = Guid.Parse("5e87cc92-571a-4d8d-8d98-d2d4147f9f90");

    private static LocalPlugin Plugin(Guid id, PluginStatus status, string version = "1.0.0.0") =>
        new("/config/plugins/x", true, new PluginManifest { Id = id, Name = "x", Version = version, Status = status });

    [Theory]
    [InlineData("2.2.1.0", true)]
    [InlineData("3.0.0.0", false)]
    [InlineData("3.1.0.0", false)]
    public void Build_FileTransformationBefore3_WarnsOutdated(string version, bool warns)
    {
        var warnings = CoexistenceWarnings.Build(true, [Plugin(FileTransformationId, PluginStatus.Active, version)], LoadSectionsPatchState.Patched, true, false);

        Assert.Equal(warns, warnings.Contains(CoexistenceWarnings.FileTransformationOutdated));
    }

    [Theory]
    [InlineData("3adf1f1f-1541-4e47-b9e3-34d2d2968af6", CoexistenceWarnings.JellyUXDisabled)]
    [InlineData("5e87cc92-571a-4d8d-8d98-d2d4147f9f90", CoexistenceWarnings.FileTransformationDisabled)]
    public void Build_JellyUXOrFileTransformationDisabled_WarnsOfTheDeletionAtRestart(string id, string code)
    {
        Assert.Equal([code], CoexistenceWarnings.Build(true, [Plugin(Guid.Parse(id), PluginStatus.Disabled, "3.0.0.0")], LoadSectionsPatchState.Patched, true, false));
        Assert.Empty(CoexistenceWarnings.Build(true, [Plugin(Guid.Parse(id), PluginStatus.Active, "3.0.0.0")], LoadSectionsPatchState.Patched, true, false));
    }

    // What the dashboard's Disable leaves until the restart (PluginManager.ProcessAlternative, 10.11.10):
    // Restart in memory, Disabled in meta.json; Enable leaves Restart in memory too, Active on disk.
    [Theory]
    [InlineData("3adf1f1f-1541-4e47-b9e3-34d2d2968af6", CoexistenceWarnings.JellyUXDisabled)]
    [InlineData("5e87cc92-571a-4d8d-8d98-d2d4147f9f90", CoexistenceWarnings.FileTransformationDisabled)]
    public void Build_JustDisabled_RestartInMemoryAndDisabledOnDisk_WarnsOfTheDeletionAtRestart(string id, string code)
    {
        var plugin = Plugin(Guid.Parse(id), PluginStatus.Restart, "3.0.0.0");

        Assert.Equal([code], CoexistenceWarnings.Build(true, [plugin], LoadSectionsPatchState.Patched, true, false, new HashSet<Guid> { plugin.Id }));
        Assert.Empty(CoexistenceWarnings.Build(true, [plugin], LoadSectionsPatchState.Patched, true, false, new HashSet<Guid>()));
    }

    [Fact]
    public void Build_CustomTabsJustDisabled_DoesNotWarnThatItIsInstalled()
    {
        var plugin = Plugin(CustomTabsId, PluginStatus.Restart);

        Assert.Empty(CoexistenceWarnings.Build(true, [plugin], LoadSectionsPatchState.Patched, true, false, new HashSet<Guid> { CustomTabsId }));
    }

    [Theory]
    [InlineData("{\"guid\":\"x\",\"status\":\"Disabled\",\"autoUpdate\":true}", true)]
    [InlineData("{\"guid\":\"x\",\"status\":\"Active\"}", false)]
    [InlineData("{\"guid\":", false)]
    public void ManifestOnDiskSaysDisabled_ReadsTheStatusOfMetaJson(string json, bool disabled)
    {
        var dir = Path.Combine(Path.GetTempPath(), "jux-meta-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "meta.json"), json);

            Assert.Equal(disabled, CoexistenceWarnings.ManifestOnDiskSaysDisabled(dir, new Jellyfin.Plugin.JuxHomepage.IO.FileSystem()));
            Assert.False(CoexistenceWarnings.ManifestOnDiskSaysDisabled(Path.Combine(dir, "missing"), new Jellyfin.Plugin.JuxHomepage.IO.FileSystem()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Build_ConfigurationReset_ComesFirst()
    {
        var warnings = CoexistenceWarnings.Build(false, [], LoadSectionsPatchState.Unknown, null, true);

        Assert.Equal([CoexistenceWarnings.ConfigurationReset, CoexistenceWarnings.FileTransformationMissing], warnings);
    }

    [Theory]
    [InlineData(LoadSectionsPatchState.HookNotFound, CoexistenceWarnings.HomeHookNotFound)]
    [InlineData(LoadSectionsPatchState.NoChunk, CoexistenceWarnings.HomeChunkMissing)]
    public void Build_HomeChunkNotPatched_Warns(LoadSectionsPatchState state, string code)
    {
        var warnings = CoexistenceWarnings.Build(true, [], state, true, false);

        Assert.Equal([code], warnings);
    }

    [Fact]
    public void Build_TabsAnchorMissing_Warns_AndAnUncheckedStateDoesNot()
    {
        Assert.Equal([CoexistenceWarnings.TabsAnchorMissing], CoexistenceWarnings.Build(true, [], LoadSectionsPatchState.Patched, false, false));
        Assert.Empty(CoexistenceWarnings.Build(true, [], LoadSectionsPatchState.Unknown, null, false));
    }

    [Fact]
    public void Build_NothingWrong_ReturnsNoWarning()
    {
        var warnings = CoexistenceWarnings.Build(true, [Plugin(Guid.NewGuid(), PluginStatus.Active)], LoadSectionsPatchState.Patched, true, false);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_FileTransformationMissing_Warns()
    {
        var warnings = CoexistenceWarnings.Build(false, [], LoadSectionsPatchState.Patched, true, false);

        Assert.Equal([CoexistenceWarnings.FileTransformationMissing], warnings);
    }

    [Theory]
    [InlineData(PluginStatus.Active)]
    [InlineData(PluginStatus.Restart)]
    public void Build_CustomTabsInstalled_Warns(PluginStatus status)
    {
        var warnings = CoexistenceWarnings.Build(true, [Plugin(CustomTabsId, status)], LoadSectionsPatchState.Patched, true, false);

        Assert.Equal([CoexistenceWarnings.CustomTabsInstalled], warnings);
    }

    [Theory]
    [InlineData(PluginStatus.Disabled)]
    [InlineData(PluginStatus.Deleted)]
    [InlineData(PluginStatus.Malfunctioned)]
    public void Build_CustomTabsNotRunning_DoesNotWarn(PluginStatus status)
    {
        var warnings = CoexistenceWarnings.Build(true, [Plugin(CustomTabsId, status)], LoadSectionsPatchState.Patched, true, false);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_BothProblems_ListsFileTransformationFirst()
    {
        var warnings = CoexistenceWarnings.Build(false, [Plugin(CustomTabsId, PluginStatus.Active)], LoadSectionsPatchState.Patched, true, false);

        Assert.Equal([CoexistenceWarnings.FileTransformationMissing, CoexistenceWarnings.CustomTabsInstalled], warnings);
    }
}
