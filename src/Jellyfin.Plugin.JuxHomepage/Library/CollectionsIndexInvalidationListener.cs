using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JuxHomepage.Library;

/// <summary>
/// Hosted service that invalidates the collections index whenever a collection is created, gains or
/// loses items, is renamed or is deleted (F-66), so "Included In" reflects the change on the next
/// load instead of after the 24-hour refresh. <see cref="ILibraryManager.ItemUpdated"/> fires for
/// every item update, so only <see cref="BoxSet"/> updates are kept.
/// </summary>
public sealed class CollectionsIndexInvalidationListener : IHostedService
{
    private readonly ICollectionManager _collectionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionsIndexCacheService _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionsIndexInvalidationListener"/> class.
    /// </summary>
    /// <param name="collectionManager">Jellyfin collection manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="index">The collections index to invalidate.</param>
    public CollectionsIndexInvalidationListener(
        ICollectionManager collectionManager,
        ILibraryManager libraryManager,
        ICollectionsIndexCacheService index)
    {
        _collectionManager = collectionManager;
        _libraryManager = libraryManager;
        _index = index;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _collectionManager.CollectionCreated += OnCollectionCreated;
        _collectionManager.ItemsAddedToCollection += OnCollectionModified;
        _collectionManager.ItemsRemovedFromCollection += OnCollectionModified;
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _collectionManager.CollectionCreated -= OnCollectionCreated;
        _collectionManager.ItemsAddedToCollection -= OnCollectionModified;
        _collectionManager.ItemsRemovedFromCollection -= OnCollectionModified;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        return Task.CompletedTask;
    }

    private void OnCollectionCreated(object? sender, CollectionCreatedEventArgs e) => _index.Invalidate();

    private void OnCollectionModified(object? sender, CollectionModifiedEventArgs e) => _index.Invalidate();

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is BoxSet)
        {
            _index.Invalidate();
        }
    }
}
