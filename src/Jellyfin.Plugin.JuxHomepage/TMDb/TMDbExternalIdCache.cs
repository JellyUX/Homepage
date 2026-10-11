using System.Collections.Concurrent;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Disk-backed TMDb -&gt; IMDb id map in front of an <c>external_ids</c> call (F-07). The mapping of
/// a title does not change, yet every refresh fetched it again for every listed title, one HTTP call
/// each, including at every startup. Only found ids are kept: an empty answer can be a network
/// failure and is asked again next time.
/// </summary>
public sealed class TMDbExternalIdCache : IDisposable
{
    private readonly DiskJsonCache<TMDbExternalIdEntry> _disk;
    private readonly string _fileName;
    private readonly Func<int, CancellationToken, Task<string?>> _fetch;
    private readonly Lazy<ConcurrentDictionary<int, string>> _ids;
    private int _unsaved;

    /// <summary>
    /// Initializes a new instance of the <see cref="TMDbExternalIdCache"/> class.
    /// </summary>
    /// <param name="directory">The directory the map file lives in.</param>
    /// <param name="fileName">The map file name.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="fetch">Fetches the IMDb id of a TMDb id when it is not in the map yet.</param>
    /// <param name="logger">Logger.</param>
    public TMDbExternalIdCache(
        string directory,
        string fileName,
        IFileSystem fileSystem,
        Func<int, CancellationToken, Task<string?>> fetch,
        ILogger logger)
    {
        _disk = new DiskJsonCache<TMDbExternalIdEntry>(directory, fileSystem, logger);
        _fileName = fileName;
        _fetch = fetch;
        _ids = new Lazy<ConcurrentDictionary<int, string>>(
            () => new ConcurrentDictionary<int, string>(
                _disk.Read(_fileName).Select(e => KeyValuePair.Create(e.TmdbId, e.ImdbId))));
    }

    /// <summary>Returns the IMDb id of a TMDb id, from the map or else from TMDb.</summary>
    /// <param name="tmdbId">The TMDb id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IMDb id, or null if TMDb has none or could not be reached.</returns>
    public async Task<string?> GetImdbIdAsync(int tmdbId, CancellationToken cancellationToken)
    {
        if (_ids.Value.TryGetValue(tmdbId, out var known))
        {
            return known;
        }

        var imdbId = await _fetch(tmdbId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(imdbId) && _ids.Value.TryAdd(tmdbId, imdbId))
        {
            Interlocked.Increment(ref _unsaved);
        }

        return imdbId;
    }

    /// <summary>Writes the map to disk if ids were added since the last save.</summary>
    public void Save()
    {
        if (Interlocked.Exchange(ref _unsaved, 0) == 0)
        {
            return;
        }

        _disk.Write(
            _fileName,
            _ids.Value.Select(kv => new TMDbExternalIdEntry { TmdbId = kv.Key, ImdbId = kv.Value }).ToList());
    }

    /// <inheritdoc/>
    public void Dispose() => _disk.Dispose();
}
