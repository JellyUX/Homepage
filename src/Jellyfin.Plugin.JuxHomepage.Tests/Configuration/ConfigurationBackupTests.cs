using System.Xml.Serialization;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Configuration;

// FIAB-07: Jellyfin replaces a configuration file it cannot read with a blank one, without a log line.
public sealed class ConfigurationBackupTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-config-backup-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private ConfigurationBackup Backup() => new(Path.Combine(_tempDir, "data"), NullLogger.Instance);

    private static PluginConfiguration Saved() => new()
    {
        SchemaVersion = 11,
        Widgets = [new WidgetConfig { WidgetType = "jux.admin.genre", Enabled = true, Order = 0, ExtraParams = [new WidgetExtraParam { Key = "value", Value = "Drame" }] }],
        ApiKeys = new ApiKeysConfig { TMDb = "key" }
    };

    // What BasePlugin<T>.LoadConfiguration does in Jellyfin 10.11.10: a file it cannot deserialize gives
    // a new default instance, written over the file (audit-v4 raw/reliability-BasePluginOfT-v10.11.10.cs:184-196).
    private static PluginConfiguration LoadLikeJellyfin(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(stream)!;
        }
        catch (InvalidOperationException)
        {
            return new PluginConfiguration();
        }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheConfiguration()
    {
        var backup = Backup();

        Assert.True(backup.Save(Saved()));
        var loaded = backup.Load();

        Assert.NotNull(loaded);
        Assert.Equal("Drame", Assert.Single(loaded.Widgets).GetExtraParamsDictionary()["value"]);
        Assert.Equal("key", loaded.ApiKeys!.TMDb);
    }

    [Fact]
    public void TruncatedConfigurationFile_IsSeenAsReset_AndTheBackupIsOfferedNotRestored()
    {
        var backup = Backup();
        backup.Save(Saved());
        var file = Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage.xml");
        File.WriteAllText(file, "<?xml version=\"1.0\"?><PluginConfiguration><Widgets><WidgetConfig><Widget");

        var loaded = LoadLikeJellyfin(file);

        Assert.True(ConfigurationBackup.LooksReset(loaded.SchemaVersion, loaded));
        Assert.True(backup.MarkResetIfBackupExists());
        Assert.True(backup.IsResetPending);
        Assert.Empty(loaded.Widgets);
    }

    [Fact]
    public void FreshInstall_HasNoBackup_SoNothingIsOffered()
    {
        var backup = Backup();

        Assert.False(backup.MarkResetIfBackupExists());
        Assert.False(backup.IsResetPending);
        Assert.Null(backup.Load());
    }

    [Fact]
    public void ALoadedConfiguration_IsNotSeenAsReset()
    {
        Assert.False(ConfigurationBackup.LooksReset(11, Saved()));
        Assert.False(ConfigurationBackup.LooksReset(11, new PluginConfiguration { SchemaVersion = 11 }));
    }

    [Fact]
    public void Restore_HandsTheMigratedBackupToTheSave_AndASaveClearsThePendingReset()
    {
        var backup = Backup();
        backup.Save(Saved());
        backup.MarkResetIfBackupExists();
        PluginConfiguration? handed = null;

        Assert.True(backup.Restore(c =>
        {
            handed = c;
            backup.Save(c);
        }));

        Assert.Equal("Drame", handed!.Widgets.Single(w => w.WidgetType == "jux.admin.genre").GetExtraParamsDictionary()["value"]);
        Assert.False(backup.IsResetPending);
    }

    [Fact]
    public void CorruptBackup_ReadsAsNone_AndRestoreRefuses()
    {
        var backup = Backup();
        backup.Save(Saved());
        File.WriteAllText(Path.Combine(_tempDir, "data", ConfigurationBackup.FileName), "<PluginConfiguration><Wid");

        Assert.Null(backup.Load());
        Assert.False(backup.Restore(_ => throw new InvalidOperationException("must not be called")));
    }

    [Fact]
    public void Save_IntoADirectoryThatCannotBeCreated_ReturnsFalse()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_tempDir).FullName, "data"), "a file, not a folder");

        Assert.False(Backup().Save(Saved()));
    }
}
