using Jellyfin.Plugin.JuxHomepage.Library;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Library;

public sealed class CollectionsIndexInvalidationListenerTests
{
    [Fact]
    public async Task ItemUpdated_InvalidatesForCollectionsOnly()
    {
        var collectionManagerMock = new Mock<ICollectionManager>();
        var libraryManagerMock = new Mock<ILibraryManager>();
        var indexMock = new Mock<ICollectionsIndexCacheService>();
        var listener = new CollectionsIndexInvalidationListener(collectionManagerMock.Object, libraryManagerMock.Object, indexMock.Object);
        await listener.StartAsync(CancellationToken.None);

        libraryManagerMock.Raise(m => m.ItemUpdated += null, libraryManagerMock.Object, new ItemChangeEventArgs { Item = new Movie() });
        indexMock.Verify(i => i.Invalidate(), Times.Never);

        libraryManagerMock.Raise(m => m.ItemUpdated += null, libraryManagerMock.Object, new ItemChangeEventArgs { Item = new BoxSet() });
        indexMock.Verify(i => i.Invalidate(), Times.Once);

        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ItemsAddedToCollection_Invalidates()
    {
        var collectionManagerMock = new Mock<ICollectionManager>();
        var indexMock = new Mock<ICollectionsIndexCacheService>();
        var listener = new CollectionsIndexInvalidationListener(collectionManagerMock.Object, new Mock<ILibraryManager>().Object, indexMock.Object);
        await listener.StartAsync(CancellationToken.None);

        collectionManagerMock.Raise(m => m.ItemsAddedToCollection += null, new CollectionModifiedEventArgs(new BoxSet(), []));

        indexMock.Verify(i => i.Invalidate(), Times.Once);
        await listener.StopAsync(CancellationToken.None);
    }
}
