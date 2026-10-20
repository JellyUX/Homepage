using System.Collections.Concurrent;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Disk-backed TMDb -&gt; IMDb id map in front of an <c>external_ids</c> call (F-07). The mapping of
/// a title does not change, yet every refresh fetched it again for every listed title, one HTTP call
/// each, including at every startup. Found ids are kept on disk. A title TMDb has no IMDb id for is
/// remembered in memory only, for <see cref="MissingIdLifetime"/> and at most
/// <see cref="MaxMissingIds"/> titles (DEC-P06-4), since TMDb may add the id later. A failed fetch
/// is not remembered and is asked again next time.
/// </summary>
public sealed class TMDbExternalIdCache : IDisposable
{
    /// <summary>How long a title without an IMDb id is not asked again (DEC-P06-4).</summary>
    internal static readonly TimeSpan MissingIdLifetime = TimeSpan.FromDays(7);

    /// <summary>The most titles without an IMDb id remembered at once (DEC-P06-4).</summary>
    internal const int MaxMissingIds = 5000;

    private readonly DiskJsonCache<TMDbExternalIdEntry> _disk;
    private readonly string _fileName;
    private readonly Func<int, CancellationToken, Task<string?>> _fetch;
    private readonly TimeProvider _timeProvider;
    private readonly Lazy<ConcurrentDictionary<int, string>> _ids;
    private readonly ConcurrentDictionary<int, DateTimeOffset> _missingUntil = new();
    private int _unsaved;

    /// <summary>
    /// Initializes a new instance of the <see cref="TMDbExternalIdCache"/> class.
    /// </summary>
    /// <param name="directory">The directory the map file lives in.</param>
    /// <param name="fileName">The map file name.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="fetch">
    /// Fetches the IMDb id of a TMDb id when it is not in the map yet: an empty string when TMDb has
    /// none, null when the request failed.
    /// </param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source of the missing-id lifetime. Defaults to <see cref="TimeProvider.System"/>.</param>
    public TMDbExternalIdCache(
        string directory,
        string fileName,
        IFileSystem fileSystem,
        Func<int, CancellationToken, Task<string?>> fetch,
        ILogger logger,
        TimeProvider? timeProvider = null)
    {
        _disk = new DiskJsonCache<TMDbExternalIdEntry>(directory, fileSystem, logger);
        _fileName = fileName;
        _fetch = fetch;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        var now = _timeProvider.GetUtcNow();
        if (_missingUntil.TryGetValue(tmdbId, out var until) && until > now)
        {
            return null;
        }

        var imdbId = await _fetch(tmdbId, cancellationToken).ConfigureAwait(false);
        if (imdbId is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(imdbId))
        {
            RememberMissing(tmdbId, now + MissingIdLifetime);
            return null;
        }

        _missingUntil.TryRemove(tmdbId, out _);
        if (_ids.Value.TryAdd(tmdbId, imdbId))
        {
            Interlocked.Increment(ref _unsaved);
        }

        return imdbId;
    }

    /// <summary>Forgets the titles without an IMDb id, so the next lookup asks TMDb again.</summary>
    public void ClearMissing() => _missingUntil.Clear();

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

    private void RememberMissing(int tmdbId, DateTimeOffset until)
    {
        if (_missingUntil.Count >= MaxMissingIds && !_missingUntil.ContainsKey(tmdbId))
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var expired in _missingUntil.Where(kv => kv.Value <= now))
            {
                _missingUntil.TryRemove(expired.Key, out _);
            }

            if (_missingUntil.Count >= MaxMissingIds)
            {
                return;
            }
        }

        _missingUntil[tmdbId] = until;
    }
}
