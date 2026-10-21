using System.Collections.Concurrent;
using System.Text.Json;
using Jellyfin.Plugin.JuxHomepage.IO;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Reads and writes per-user <see cref="UserConfiguration"/> as JSON files on disk.
/// Files are stored under <c>DataPath/Jellyfin.Plugin.JuxHomepage/users/{userId}.json</c>. Deliberately
/// under <c>DataPath</c> rather than <c>PluginConfigurationsPath</c> (which lives inside Jellyfin's own
/// <c>/config/plugins</c> tree, scanned by the core <c>PluginManager</c> for candidate plugin
/// assemblies: a stray DLL there once made Jellyfin disable and wipe the whole configurations tree).
/// All file access is protected by a <see cref="ReaderWriterLockSlim"/> to allow concurrent reads
/// while serializing writes.
/// <para>
/// A user's file is read from disk once: its text, or its absence, is kept in memory and replaced by
/// each successful save (Annexe.a, DEC-P07-8), since only this store writes these files; every request
/// that checks whether JellyUX runs for the user read it again before. The text is deserialized on each
/// read, so no caller shares an instance another one may change. A file edited by hand is read after a
/// restart.
/// </para>
/// </summary>
public sealed class UserConfigurationStore : IUserConfigurationStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _usersDir;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<UserConfigurationStore> _logger;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ConcurrentDictionary<Guid, StoredText> _texts = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserConfigurationStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Provides the application data directory path.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="logger">Logger.</param>
    public UserConfigurationStore(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        ILogger<UserConfigurationStore> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _usersDir = Path.Combine(
            applicationPaths.DataPath,
            "Jellyfin.Plugin.JuxHomepage",
            "users");

        // Same guard as DiskJsonCache (H-07): the store still builds, and each save reports its failure.
        try
        {
            _fileSystem.CreateDirectory(_usersDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot create the user settings directory '{Directory}'; user settings cannot be saved.", _usersDir);
        }
    }

    /// <summary>
    /// Reads the user configuration for the given user.
    /// </summary>
    /// <param name="userId">The Jellyfin user identifier.</param>
    /// <returns>The user configuration, or null if none has been saved yet.</returns>
    public UserConfiguration? GetUserConfiguration(Guid userId)
    {
        var path = GetPath(userId);

        _lock.EnterReadLock();
        try
        {
            // Under the read lock, so a read never overtakes a save in progress.
            var json = _texts.GetOrAdd(userId, _ => new StoredText(_fileSystem.FileExists(path) ? _fileSystem.ReadAllText(path) : null)).Json;
            if (json is null)
            {
                return null;
            }

            var config = JsonSerializer.Deserialize<UserConfiguration>(json);

            // Migrate in memory on read, mirroring PluginConfiguration's own migration timing (see
            // Plugin.cs) -- the on-disk file is only corrected the next time this user's overrides
            // are saved, not immediately.
            if (config is not null)
            {
                Plugin.MigrateUserConfiguration(config);
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read user configuration for {UserId}.", userId);
            return null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Persists the given user configuration to disk.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <returns>False when the file could not be written.</returns>
    public bool SaveUserConfiguration(UserConfiguration config)
    {
        var path = GetPath(config.UserId);
        var tmpPath = path + ".tmp";

        _lock.EnterWriteLock();
        try
        {
            var json = JsonSerializer.Serialize(config, SerializerOptions);

            // Write to a temp file first, then rename into place. File.Move with overwrite is
            // atomic on the same volume (rename() on Linux, MoveFileEx/MOVEFILE_REPLACE_EXISTING on
            // Windows), so a crash or restart mid-write can never leave a partially-written config
            // file on disk -- readers always see either the old file or the fully-written new one.
            // Again on each save: a data path that was not writable at startup may be by now (H-07).
            _fileSystem.CreateDirectory(_usersDir);
            _fileSystem.WriteAllText(tmpPath, json);
            _fileSystem.Move(tmpPath, path, overwrite: true);
            _texts[config.UserId] = new StoredText(json);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user configuration for {UserId}.", config.UserId);
            TryDeleteStrayTempFile(tmpPath);
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _disposed = true;
        }
    }

    private string GetPath(Guid userId) =>
        Path.Combine(_usersDir, $"{userId}.json");

    private void TryDeleteStrayTempFile(string tmpPath)
    {
        try
        {
            if (_fileSystem.FileExists(tmpPath))
            {
                _fileSystem.Delete(tmpPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up stray temp file {TmpPath}.", tmpPath);
        }
    }

    /// <summary>The text of a user's file as last read or saved; null when the user has none.</summary>
    private sealed record StoredText(string? Json);
}
