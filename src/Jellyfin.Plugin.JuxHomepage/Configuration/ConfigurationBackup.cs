using System.Xml.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// A copy of the plugin configuration kept in the data folder, outside the plugins tree Jellyfin scans
/// (FIAB-07). Jellyfin replaces a configuration file it cannot read with a blank one and saves it,
/// without a log line: the copy lets the admin page offer the last saved configuration back. It is
/// never restored automatically.
/// <para>
/// Written after each admin save, atomically (temp file then rename). A blank configuration found at
/// startup while a copy exists leaves a marker file, so the offer survives a restart until the admin
/// restores or saves (tests: ConfigurationBackupTests).
/// </para>
/// </summary>
public sealed class ConfigurationBackup
{
    /// <summary>The file name of the copy.</summary>
    public const string FileName = "configuration.backup.xml";

    private const string ResetMarkerName = "configuration.reset";
    private static readonly XmlSerializer Serializer = new(typeof(PluginConfiguration));

    private readonly string _directory;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationBackup"/> class.
    /// </summary>
    /// <param name="directory">The plugin's data folder.</param>
    /// <param name="logger">Logger.</param>
    public ConfigurationBackup(string directory, ILogger logger)
    {
        _directory = directory;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether a blank configuration was found at startup while a copy exists.</summary>
    public bool IsResetPending => File.Exists(MarkerPath) && File.Exists(BackupPath);

    private string BackupPath => Path.Combine(_directory, FileName);

    private string MarkerPath => Path.Combine(_directory, ResetMarkerName);

    /// <summary>
    /// Returns whether a configuration looks like the blank instance Jellyfin creates for a file it
    /// cannot read: never migrated (schema 1) and without a single row, which a migrated configuration
    /// always has.
    /// </summary>
    /// <param name="schemaVersionAsLoaded">The schema version before migration.</param>
    /// <param name="config">The loaded configuration.</param>
    /// <returns>True when the configuration is blank.</returns>
    public static bool LooksReset(int schemaVersionAsLoaded, PluginConfiguration config)
        => schemaVersionAsLoaded < 2 && config.Widgets.Length == 0;

    /// <summary>
    /// Writes the copy of a configuration just saved by the admin, and clears a pending reset: the admin
    /// either restored the copy or chose to keep the configuration they saved.
    /// </summary>
    /// <param name="config">The saved configuration.</param>
    /// <returns>False when the copy could not be written.</returns>
    public bool Save(PluginConfiguration config)
    {
        var tmpPath = BackupPath + ".tmp";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                using (var stream = File.Create(tmpPath))
                {
                    Serializer.Serialize(stream, config);
                }

                File.Move(tmpPath, BackupPath, overwrite: true);
                File.Delete(MarkerPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogError(ex, "Failed to write the configuration copy '{Path}'.", BackupPath);
                return false;
            }
        }
    }

    /// <summary>Reads the copy, or null when there is none or it cannot be read.</summary>
    /// <returns>The copied configuration, or null.</returns>
    public PluginConfiguration? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(BackupPath))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(BackupPath);
                return Serializer.Deserialize(stream) as PluginConfiguration;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "The configuration copy '{Path}' cannot be read.", BackupPath);
                return null;
            }
        }
    }

    /// <summary>
    /// Records a blank configuration found at startup, when a copy exists to offer instead.
    /// </summary>
    /// <returns>True when a copy exists and the reset is now pending.</returns>
    public bool MarkResetIfBackupExists()
    {
        lock (_lock)
        {
            if (!File.Exists(BackupPath))
            {
                return false;
            }

            try
            {
                File.WriteAllText(MarkerPath, string.Empty);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Failed to record the configuration reset in '{Path}'.", MarkerPath);
                return false;
            }
        }
    }

    /// <summary>
    /// Hands the copy, migrated to the current schema, to <paramref name="save"/> (the plugin's
    /// <c>UpdateConfiguration</c>, which validates it and writes a new copy).
    /// </summary>
    /// <param name="save">Saves the restored configuration.</param>
    /// <returns>False when there is no readable copy.</returns>
    public bool Restore(Action<PluginConfiguration> save)
    {
        var config = Load();
        if (config is null)
        {
            return false;
        }

        Plugin.MigrateConfiguration(config);
        save(config);
        return true;
    }
}
