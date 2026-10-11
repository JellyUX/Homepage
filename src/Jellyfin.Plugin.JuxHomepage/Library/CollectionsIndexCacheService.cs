using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
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

    private readonly DiskJsonCache<CollectionMembership> _cache;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CollectionsIndexCacheService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionsIndexCacheService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Provides the application data directory path.</param>
    /// <param name="fileSystem">File system abstraction, for testability.</param>
    /// <param name="libraryManager">Jellyfin library manager, used to enumerate collections and their members.</param>
    /// <param name="logger">Logger.</param>
    public CollectionsIndexCacheService(
        IApplicationPaths applicationPaths,
        IFileSystem fileSystem,
        ILibraryManager libraryManager,
        ILogger<CollectionsIndexCacheService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;

        var cacheDir = Path.Combine(
            applicationPaths.DataPath,
            "Jellyfin.Plugin.JuxHomepage",
            "cache",
            "library");

        _cache = new DiskJsonCache<CollectionMembership>(cacheDir, fileSystem, logger);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CollectionRef> GetCollectionsFor(Guid itemId)
    {
        var entry = _cache.Read(FileName).FirstOrDefault(m => m.ItemId == itemId);
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
    public bool IsStale() => _cache.IsStale(FileName, Ttl);

    /// <inheritdoc/>
    public bool TryAcquireRefreshLock() => _refreshGate.Wait(0);

    /// <inheritdoc/>
    public Task RunRefreshLockedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var index = ComputeIndex(cancellationToken);
            _cache.WriteUnlessEmpty(FileName, index);
            _logger.LogInformation("Collections index refreshed: {Count} item(s) with at least one collection.", index.Count);
        }
        catch (Exception ex)
        {
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
}
