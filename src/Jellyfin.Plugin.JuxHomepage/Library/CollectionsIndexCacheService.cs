using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Library.Models;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Library;

/// <summary>
/// Reads and refreshes the on-disk Collections reverse index under
/// <c>DataPath/Jellyfin.Plugin.JuxHomepage/cache/library/collections-index.json</c> -- a single
/// global file (not per-user), since collection membership does not depend on which user is asking.
/// Reuses <see cref="DiskJsonCache{T}"/> (TODO_V2.md Phase 7.2), same TTL/refresh-lock shape as
/// <see cref="TMDbCacheService"/>.
/// </summary>
public sealed class CollectionsIndexCacheService : ICollectionsIndexCacheService, IDisposable
{
    private const string FileName = "collections-index.json";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly TimeSpan DefaultInvalidationDelay = TimeSpan.FromSeconds(2);

    private readonly DiskJsonCache<CollectionMembership> _cache;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CollectionsIndexCacheService> _logger;
    private readonly TimeProvider _timeProvider;
    private volatile Unsaved? _unsaved;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Timer _invalidationTimer;
    private readonly TimeSpan _invalidationDelay;
    private volatile bool _invalidated;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionsIndexCacheService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Provides the application data directory path.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="libraryManager">Jellyfin library manager, used to enumerate collections and their members.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="invalidationDelay">
    /// Delay between the last <see cref="Invalidate"/> call and the background refresh. Defaults to 2 seconds.
    /// </param>
    /// <param name="timeProvider">Time source of the cache and of an index kept in memory. Defaults to <see cref="TimeProvider.System"/>.</param>
    public CollectionsIndexCacheService(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        ILibraryManager libraryManager,
        ILogger<CollectionsIndexCacheService> logger,
        TimeSpan? invalidationDelay = null,
        TimeProvider? timeProvider = null)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _invalidationDelay = invalidationDelay ?? DefaultInvalidationDelay;
        _invalidationTimer = new Timer(_ => _ = RefreshAsync(CancellationToken.None), null, Timeout.Infinite, Timeout.Infinite);

        var cacheDir = Path.Combine(
            applicationPaths.DataPath,
            "Jellyfin.Plugin.JuxHomepage",
            "cache",
            "library");

        _cache = new DiskJsonCache<CollectionMembership>(cacheDir, fileSystem, logger, _timeProvider);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CollectionRef> GetCollectionsFor(Guid itemId)
    {
        var entry = Index().FirstOrDefault(m => m.ItemId == itemId);
        return entry?.Collections ?? [];
    }

    /// <inheritdoc/>
    public IReadOnlyList<CollectionRef> GetCollectionsFor(Guid itemId, User user)
    {
        var collections = GetCollectionsFor(itemId);
        if (collections.Count == 0)
        {
            return collections;
        }

        // The index is global, so the rights check happens here, on read: one batched lookup covers
        // the requested item and every collection it belongs to.
        var visibleIds = UserVisibleItems
            .Resolve(_libraryManager, user, [itemId, .. collections.Select(c => c.CollectionId)], out _)
            .Select(i => i.Id)
            .ToHashSet();

        if (!visibleIds.Contains(itemId))
        {
            return [];
        }

        return collections.Where(c => visibleIds.Contains(c.CollectionId)).ToList();
    }

    /// <inheritdoc/>
    public bool IsStale() => _invalidated || (UnsavedIndex() is null && _cache.IsStale(FileName, Ttl));

    /// <inheritdoc/>
    public void Invalidate()
    {
        _invalidated = true;
        _invalidationTimer.Change(_invalidationDelay, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc/>
    public bool TryAcquireRefreshLock() => _refreshGate.Wait(0);

    /// <inheritdoc/>
    public Task RunRefreshLockedAsync(CancellationToken cancellationToken)
    {
        // Cleared before computing, so a collection change during the computation marks the index
        // stale again instead of being lost.
        _invalidated = false;
        try
        {
            var index = ComputeIndex(cancellationToken);

            // An index the disk refuses is served from memory for one time-to-live, as
            // PerUserComputedCache does (FIAB-03): otherwise each IncludedIn request recomputed it (test:
            // CollectionsIndexCacheServiceTests "RefreshAsync_WhenTheDiskRefusesTheIndex...").
            _unsaved = _cache.Write(FileName, index) ? null : new Unsaved(_timeProvider.GetUtcNow(), index);
            _logger.LogInformation("Collections index refreshed: {Count} item(s) with at least one collection.", index.Count);
        }
        catch (Exception ex)
        {
            _invalidated = true;
            _logger.LogError(ex, "Failed to refresh collections index.");
        }
        finally
        {
            _refreshGate.Release();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!TryAcquireRefreshLock())
        {
            _logger.LogInformation("Collections index refresh already in progress; skipping this request.");
            return false;
        }

        await RunRefreshLockedAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _invalidationTimer.Dispose();
            _cache.Dispose();
            _refreshGate.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Builds the reverse index by enumerating every BoxSet in the library and, for each, its member
    /// items via <c>Folder.GetLinkedChildren</c> -- BoxSet membership is a many-to-many
    /// association (an item's real physical parent is its own library folder, not the BoxSet), so
    /// <see cref="InternalItemsQuery.AncestorIds"/> (which follows physical/virtual folder ancestry)
    /// never matches a BoxSet's members -- confirmed live on jellyux-test: the original
    /// AncestorIds-based query found all BoxSets correctly but always resolved 0 members for every one
    /// of them. <c>GetLinkedChildren</c> is the same API <c>Widgets/Admin/CollectionWidget.cs</c> (an
    /// already-working feature) uses for the same reason; <c>null</c> is passed for the user since this
    /// index is deliberately global/not user-scoped.
    /// </summary>
    private IReadOnlyList<CollectionMembership> Index() => UnsavedIndex() ?? _cache.Read(FileName);

    private IReadOnlyList<CollectionMembership>? UnsavedIndex()
    {
        var unsaved = _unsaved;
        return unsaved is not null && _timeProvider.GetUtcNow() - unsaved.ComputedAt < Ttl ? unsaved.Index : null;
    }

    private IReadOnlyList<CollectionMembership> ComputeIndex(CancellationToken cancellationToken)
    {
        var boxSets = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
            DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions { Fields = [] }
        });

        var byItem = new Dictionary<Guid, List<CollectionRef>>();

        foreach (var boxSet in boxSets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (boxSet is not Folder boxSetFolder)
            {
                continue;
            }

            var members = boxSetFolder.GetLinkedChildren(null).Where(m => m is Movie or Series);

            var collectionRef = new CollectionRef { CollectionId = boxSet.Id, CollectionName = boxSet.Name };

            foreach (var member in members)
            {
                if (!byItem.TryGetValue(member.Id, out var collections))
                {
                    collections = [];
                    byItem[member.Id] = collections;
                }

                collections.Add(collectionRef);
            }
        }

        return byItem
            .Select(kv => new CollectionMembership { ItemId = kv.Key, Collections = kv.Value })
            .ToList();
    }

    /// <summary>An index the disk refused, with the time it was computed.</summary>
    private sealed record Unsaved(DateTimeOffset ComputedAt, IReadOnlyList<CollectionMembership> Index);
}
