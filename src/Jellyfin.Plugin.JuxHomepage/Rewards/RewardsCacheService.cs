using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Rewards.Models;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Rewards;

/// <summary>
/// Reads and refreshes the on-disk Rewards data cache under
/// <c>DataPath/Jellyfin.Plugin.JuxHomepage/cache/rewards/</c> (deliberately under <c>DataPath</c>, not
/// <c>PluginConfigurationsPath</c> -- see <see cref="TMDbCacheService"/>'s doc comment for the incident
/// this avoids). Composes the same <see cref="DiskJsonCache{T}"/> and <see cref="LibraryCrossReferencer"/>
/// building blocks TMDb uses (TODO_V2.md Phase 7), one instance-keyed file per Rewards widget row --
/// mirrors <see cref="TMDbCacheService"/>'s Discover Movies per-instance pattern rather than its fixed
/// six-file pattern, since Rewards (like Discover) has no single "the" data set, only admin-configured
/// instances.
/// </summary>
public sealed class RewardsCacheService : IRewardsCacheService, IDisposable
{
    private readonly DiskJsonCache<RewardsWinner> _cache;
    private readonly LibraryCrossReferencer _crossReferencer;
    private readonly IWikidataApiClient _apiClient;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<RewardsCacheService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RewardsCacheService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Provides the application data directory path.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="apiClient">Wikidata API client used to fetch fresh data during a refresh.</param>
    /// <param name="libraryManager">Jellyfin library manager, used to cross-reference cached items.</param>
    /// <param name="getConfiguration">
    /// Factory that returns the current plugin configuration, used to enumerate configured Rewards
    /// instances. Defaults to <c>Plugin.Instance?.Configuration</c> in production.
    /// </param>
    /// <param name="logger">Logger.</param>
    public RewardsCacheService(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        IWikidataApiClient apiClient,
        ILibraryManager libraryManager,
        Func<PluginConfiguration?> getConfiguration,
        ILogger<RewardsCacheService> logger)
    {
        _apiClient = apiClient;
        _getConfiguration = getConfiguration;
        _logger = logger;
        _crossReferencer = new LibraryCrossReferencer(libraryManager, logger);

        var cacheDir = Path.Combine(
            applicationPaths.DataPath,
            "Jellyfin.Plugin.JuxHomepage",
            "cache",
            "rewards");

        _cache = new DiskJsonCache<RewardsWinner>(cacheDir, fileSystem, logger);
    }

    /// <inheritdoc/>
    public IReadOnlyList<RewardsWinner> GetRewards(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out _))
        {
            return [];
        }

        return _cache.Read(GetFileName(instanceId));
    }

    /// <inheritdoc/>
    public async Task RefreshInstanceAsync(string instanceId, RewardsFilter filter, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(instanceId, out _))
        {
            _logger.LogWarning("Refused to refresh Rewards cache: instance id '{InstanceId}' is not a GUID.", instanceId);
            return;
        }

        try
        {
            var items = DeduplicateById(await _apiClient.GetAwardWinnersAsync(filter, cancellationToken).ConfigureAwait(false));

            // Unlike TMDb, the IMDb id is already known directly from the Wikidata SPARQL query (see
            // WikidataApiClient), so the lookup callback is a synchronous dictionary read rather than a
            // second HTTP round-trip. fallbackProvider is explicitly null: there is no
            // MetadataProvider.Wikidata to fall back on, and falling back to MetadataProvider.Tmdb
            // would compare an unrelated identifier (a parsed Wikidata Q-id) against real TMDb ids,
            // risking a spurious match rather than just a missed one.
            var imdbById = items.ToDictionary(i => i.Id, i => (string?)i.ImdbId);
            var matched = await _crossReferencer.CrossReferenceAsync(
                items,
                (id, _) => Task.FromResult(imdbById.GetValueOrDefault(id)),
                _crossReferencer.BuildIndex([BaseItemKind.Movie]),
                cancellationToken,
                fallbackProvider: null).ConfigureAwait(false);

            _cache.WriteUnlessEmpty(GetFileName(instanceId), items);
            LogRefreshOutcome(instanceId, items.Count, matched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Rewards cache '{InstanceId}'.", instanceId);
        }
    }

    /// <inheritdoc/>
    public async Task RefreshAllInstancesAsync(CancellationToken cancellationToken)
    {
        if (!TryAcquireRefreshLock())
        {
            _logger.LogInformation("Rewards refresh already in progress; skipping this request.");
            return;
        }

        await RunRefreshLockedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool TryAcquireRefreshLock() => _refreshGate.Wait(0);

    /// <inheritdoc/>
    public async Task RunRefreshLockedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rows = _getConfiguration()?.Widgets?
                .Where(c => c.WidgetType == RewardsWidgetTypes.Rewards)
                .ToList() ?? [];

            foreach (var row in rows)
            {
                var extra = row.GetExtraParamsDictionary();
                if (!extra.TryGetValue("value", out var instanceId) || string.IsNullOrEmpty(instanceId))
                {
                    continue;
                }

                var filter = RewardsFilter.FromExtraParams(extra, _logger, instanceId);
                await RefreshInstanceAsync(instanceId, filter, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cache.Dispose();
            _refreshGate.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Removes duplicate entries (same Wikidata film Q-id) from a freshly fetched result set, keeping
    /// the first occurrence. A film that won multiple categories within the same ceremony edition
    /// legitimately appears as multiple SPARQL result rows (see
    /// <see cref="WikidataApiClient.GetAwardWinnersAsync"/>'s query comments); without this, the same
    /// local library item would appear multiple times in the widget. Mirrors
    /// <see cref="TMDbCacheService"/>'s own <c>DeduplicateById</c>.
    /// </summary>
    private static IReadOnlyList<RewardsWinner> DeduplicateById(IReadOnlyList<RewardsWinner> items)
    {
        var seenIds = new HashSet<int>();
        var deduplicated = new List<RewardsWinner>(items.Count);
        foreach (var item in items)
        {
            if (seenIds.Add(item.Id))
            {
                deduplicated.Add(item);
            }
        }

        return deduplicated;
    }

    private void LogRefreshOutcome(string instanceId, int itemCount, int matchedCount)
    {
        if (itemCount == 0)
        {
            _logger.LogInformation(
                "Rewards cache '{InstanceId}' refresh returned 0 items -- check for a preceding WikidataApiClient warning (no filter configured, a rate limit, or a network failure). The previous cache, if any, was left untouched.",
                instanceId);
            return;
        }

        _logger.LogInformation(
            "Rewards cache '{InstanceId}' refreshed: {ItemCount} item(s), {MatchedCount} matched to the local library.",
            instanceId,
            itemCount,
            matchedCount);
    }

    private static string GetFileName(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out var validated))
        {
            throw new ArgumentException("Rewards instance id must be a GUID.", nameof(instanceId));
        }

        return $"rewards_{validated:N}.json";
    }
}
