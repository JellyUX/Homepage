using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Jellyfin.Plugin.JuxHomepage.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Caching;

/// <summary>
/// Generic disk-backed JSON cache for a single item type, wrapping items in a
/// <see cref="CacheEntry{T}"/> envelope. Thread-safe via a <see cref="ReaderWriterLockSlim"/>;
/// writes are atomic (temp file then rename), mirroring
/// <see cref="Configuration.UserConfigurationStore"/>'s disk-persistence pattern.
/// <para>
/// Extracted from <see cref="TMDb.TMDbCacheService"/> (TODO_V2.md Phase 7.2) so a future second external
/// data provider (TVDb, Trakt, OMDb) can reuse the same persistence layer without duplicating it.
/// Keyed by an explicit file name rather than a fixed cache-type enum, so a single instance of this
/// class can serve multiple related cache files (e.g. <see cref="TMDb.TMDbCacheService"/> composes one
/// <see cref="DiskJsonCache{T}"/> for movies -- covering both the fixed trending/top-rated/now-playing
/// caches and the per-instance Discover caches -- and one for shows).
/// </para>
/// <para>
/// Reads are memoized per file (F-04): the deserialized items are kept with the file's last-write
/// time, so a repeated read costs one timestamp check instead of reading and parsing the file. A
/// write through this instance replaces the memo; a file changed or deleted behind its back is
/// detected by its timestamp.
/// </para>
/// </summary>
/// <typeparam name="T">The cached item type.</typeparam>
public sealed class DiskJsonCache<T> : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ConcurrentDictionary<string, Memo> _memo = new();
    private readonly JsonSerializerOptions _writeOptions;
    private readonly bool _projected;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskJsonCache{T}"/> class.
    /// </summary>
    /// <param name="directory">The directory cache files are read from and written to.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source of cache timestamps. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="storedProperties">
    /// The JSON names of the item properties written to disk, the ones read back; null writes them all.
    /// The others are left out of the file and of the memo of a write; a file that still holds them
    /// reads as before, unknown properties being ignored (Annexe.b, DEC-P07-11).
    /// </param>
    public DiskJsonCache(string directory, IFileSystem fileSystem, ILogger logger, TimeProvider? timeProvider = null, IReadOnlyCollection<string>? storedProperties = null)
    {
        _directory = directory;
        _fileSystem = fileSystem;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _projected = storedProperties is not null;
        _writeOptions = storedProperties is null ? SerializerOptions : ProjectedOptions(storedProperties);

        // An unwritable DataPath must not fail the construction of every service built on a cache
        // (H-07): the cache reads nothing and each write reports its failure.
        try
        {
            _fileSystem.CreateDirectory(_directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot create the cache directory '{Directory}'; the cache will not be saved to disk.", _directory);
        }
    }

    private static JsonSerializerOptions ProjectedOptions(IReadOnlyCollection<string> storedProperties) => new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    if (info.Type != typeof(T))
                    {
                        return;
                    }

                    foreach (var property in info.Properties.Where(p => !storedProperties.Contains(p.Name)).ToList())
                    {
                        info.Properties.Remove(property);
                    }
                }
            }
        }
    };

    /// <summary>
    /// Reads the cached items from the given file, or an empty list if the file does not exist yet
    /// or fails to parse.
    /// </summary>
    /// <param name="fileName">The cache file name (not a full path).</param>
    /// <returns>The cached items, or an empty list.</returns>
    public IReadOnlyList<T> Read(string fileName)
    {
        var path = Path.Combine(_directory, fileName);

        _lock.EnterReadLock();
        try
        {
            if (!_fileSystem.FileExists(path))
            {
                _memo.TryRemove(fileName, out _);
                return [];
            }

            return ReadAt(fileName, path, _fileSystem.GetLastWriteTimeUtc(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cache file '{FileName}'.", fileName);
            return [];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Reads the cached items of a file whose last-write time the caller has just read with
    /// <see cref="GetLastWriteUtc"/>: no second existence and date check (Annexe.a). An empty list if
    /// the file is gone since or fails to parse.
    /// </summary>
    /// <param name="fileName">The cache file name (not a full path).</param>
    /// <param name="lastWriteUtc">The file's last-write time, as just read.</param>
    /// <returns>The cached items, or an empty list.</returns>
    public IReadOnlyList<T> Read(string fileName, DateTime lastWriteUtc)
    {
        _lock.EnterReadLock();
        try
        {
            return ReadAt(fileName, Path.Combine(_directory, fileName), lastWriteUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cache file '{FileName}'.", fileName);
            return [];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Writes the given items to the given file, unless the list is empty -- a valid cache is never
    /// overwritten by an empty result (e.g. a failed refresh), so the previous data remains available.
    /// For caches fed by an external source (TMDb, Wikidata), where an empty answer means a failure;
    /// caches computed from the local library use <see cref="Write"/>, where empty is a real answer.
    /// </summary>
    /// <param name="fileName">The cache file name (not a full path).</param>
    /// <param name="items">The items to write.</param>
    public void WriteUnlessEmpty(string fileName, IReadOnlyList<T> items)
    {
        if (items.Count > 0)
        {
            Write(fileName, items);
        }
    }

    /// <summary>
    /// Writes the given items to the given file, including an empty list (F-69): a library with no
    /// collection or a user whose history was reset is a valid, cacheable state, and not writing it
    /// left the cache stale forever and an outdated list on display. Written atomically: serialized
    /// to a temp file, then renamed into place, so a crash or restart mid-write can never leave a
    /// partially-written cache file on disk.
    /// </summary>
    /// <param name="fileName">The cache file name (not a full path).</param>
    /// <param name="items">The items to write.</param>
    /// <returns>False when the file could not be written; the previous file, if any, is left as it was.</returns>
    public bool Write(string fileName, IReadOnlyList<T> items)
    {
        var path = Path.Combine(_directory, fileName);
        var tmpPath = path + ".tmp";
        var entry = new CacheEntry<T> { RefreshedAtUtc = _timeProvider.GetUtcNow().UtcDateTime, Items = items };
        var json = JsonSerializer.Serialize(entry, _writeOptions);
        var written = _projected ? JsonSerializer.Deserialize<CacheEntry<T>>(json)?.Items ?? [] : items;

        _lock.EnterWriteLock();
        try
        {
            // Again on each write: a data path that was not writable at startup may be by now (H-07).
            _fileSystem.CreateDirectory(_directory);
            _fileSystem.WriteAllText(tmpPath, json);
            _fileSystem.Move(tmpPath, path, overwrite: true);
            _memo[fileName] = new Memo(_fileSystem.GetLastWriteTimeUtc(path), written);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write cache file '{FileName}'.", fileName);
            TryDeleteStrayTempFile(tmpPath);
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Returns whether the given cache file is missing or older than <paramref name="maxAge"/>.
    /// </summary>
    /// <param name="fileName">The cache file name (not a full path).</param>
    /// <param name="maxAge">The maximum age before the cache is considered stale.</param>
    /// <returns>True if the cache is absent or stale; otherwise false.</returns>
    public bool IsStale(string fileName, TimeSpan maxAge)
    {
        var path = Path.Combine(_directory, fileName);
        if (!_fileSystem.FileExists(path))
        {
            return true;
        }

        return IsOlderThan(_fileSystem.GetLastWriteTimeUtc(path), maxAge);
    }

    /// <summary>Returns whether a file written at <paramref name="lastWriteUtc"/> is older than <paramref name="maxAge"/>.</summary>
    /// <param name="lastWriteUtc">The file's last-write time.</param>
    /// <param name="maxAge">The maximum age before the cache is considered stale.</param>
    /// <returns>True if the file is stale.</returns>
    public bool IsOlderThan(DateTime lastWriteUtc, TimeSpan maxAge) =>
        _timeProvider.GetUtcNow().UtcDateTime - lastWriteUtc >= maxAge;

    /// <summary>
    /// Returns the UTC timestamp of the last write to the given cache file, for display in the admin UI.
    /// </summary>
    /// <param name="fileName">The cache file name (not a full path).</param>
    /// <returns>The last-write timestamp, or null if the file does not exist.</returns>
    public DateTime? GetLastWriteUtc(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        return _fileSystem.FileExists(path) ? _fileSystem.GetLastWriteTimeUtc(path) : null;
    }

    /// <summary>Serves the memo of a file still at <paramref name="lastWrite"/>, or reads and memoizes it.</summary>
    private IReadOnlyList<T> ReadAt(string fileName, string path, DateTime lastWrite)
    {
        if (_memo.TryGetValue(fileName, out var memo) && memo.LastWriteUtc == lastWrite)
        {
            return memo.Items;
        }

        var json = _fileSystem.ReadAllText(path);
        var items = JsonSerializer.Deserialize<CacheEntry<T>>(json)?.Items ?? [];
        _memo[fileName] = new Memo(lastWrite, items);
        return items;
    }

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

    /// <summary>The deserialized items of one file, valid while the file keeps this last-write time.</summary>
    private sealed record Memo(DateTime LastWriteUtc, IReadOnlyList<T> Items);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _disposed = true;
        }
    }
}
