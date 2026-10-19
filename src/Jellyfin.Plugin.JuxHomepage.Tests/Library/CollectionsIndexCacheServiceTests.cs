using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Library;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Library;

[Collection(BaseItemStaticCollection.Name)]
public sealed class CollectionsIndexCacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-collections-index-tests-" + Guid.NewGuid());

    private CollectionsIndexCacheService BuildService(ILibraryManager libraryManager, TimeSpan? invalidationDelay = null)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new CollectionsIndexCacheService(
            applicationPathsMock.Object,
            new FileSystem(),
            libraryManager,
            NullLogger<CollectionsIndexCacheService>.Instance,
            invalidationDelay);
    }

    [Fact]
    public async Task RefreshAsync_NoCollection_IndexIsNoLongerStale()
    {
        // F-69: without any collection, the empty index is written, so IncludedIn stops relaunching
        // ComputeIndex on every detail page.
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([]);
        var service = BuildService(libraryManagerMock.Object);

        await service.RefreshAsync(CancellationToken.None);

        Assert.False(service.IsStale());
    }

    [Fact]
    public async Task Invalidate_MarksStaleThenRefreshesInTheBackground()
    {
        // F-66: a collection change refreshes the index shortly after, without waiting 24 hours.
        var computations = 0;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback(() => Interlocked.Increment(ref computations))
            .Returns([]);
        var service = BuildService(libraryManagerMock.Object, TimeSpan.FromMilliseconds(50));
        await service.RefreshAsync(CancellationToken.None);

        service.Invalidate();
        service.Invalidate();
        Assert.True(service.IsStale());

        await Task.Delay(500);
        Assert.Equal(2, computations);
        Assert.False(service.IsStale());
    }

    [Fact]
    public async Task RefreshAsync_MovieBelongingToOneCollection_CachesReverseIndex()
    {
        var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = "Trilogy" };
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Part One" };
        boxSet.LinkedChildren = [new LinkedChild { ItemId = movie.Id }];

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.BoxSet))))
            .Returns([boxSet]);
        libraryManagerMock
            .Setup(m => m.GetItemById(movie.Id))
            .Returns(movie);

        BaseItem.LibraryManager = libraryManagerMock.Object;

        var service = BuildService(libraryManagerMock.Object);

        var refreshed = await service.RefreshAsync(CancellationToken.None);
        Assert.True(refreshed);

        var collections = service.GetCollectionsFor(movie.Id);

        var collectionRef = Assert.Single(collections);
        Assert.Equal(boxSet.Id, collectionRef.CollectionId);
        Assert.Equal("Trilogy", collectionRef.CollectionName);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheDiskRefusesTheIndex_ServesItFromMemoryAndIsNotStaleForOneTtl()
    {
        // FIAB-03 sister (R11): a refused write used to leave the index stale, so every IncludedIn
        // request relaunched ComputeIndex and answered nothing.
        var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = "Trilogy" };
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Part One" };
        boxSet.LinkedChildren = [new LinkedChild { ItemId = movie.Id }];
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.BoxSet))))
            .Returns([boxSet]);
        libraryManagerMock.Setup(m => m.GetItemById(movie.Id)).Returns(movie);
        BaseItem.LibraryManager = libraryManagerMock.Object;
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);
        var clock = new ManualTimeProvider { UtcNowValue = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero) };
        using var service = new CollectionsIndexCacheService(
            applicationPathsMock.Object,
            new FailingFileSystem { FailWrites = true },
            libraryManagerMock.Object,
            NullLogger<CollectionsIndexCacheService>.Instance,
            timeProvider: clock);

        await service.RefreshAsync(CancellationToken.None);

        Assert.False(service.IsStale());
        Assert.Equal(boxSet.Id, Assert.Single(service.GetCollectionsFor(movie.Id)).CollectionId);
        clock.UtcNowValue += TimeSpan.FromDays(1);
        Assert.True(service.IsStale());
    }

    [Fact]
    public void GetCollectionsFor_ItemNotInAnyCollection_ReturnsEmpty()
    {
        var service = BuildService(new Mock<ILibraryManager>().Object);

        Assert.Empty(service.GetCollectionsFor(Guid.NewGuid()));
    }

    [Fact]
    public void IsStale_NoRefreshYet_ReturnsTrue()
    {
        var service = BuildService(new Mock<ILibraryManager>().Object);

        Assert.True(service.IsStale());
    }

    [Fact]
    public async Task Invalidate_DuringARefresh_LeavesTheIndexStale()
    {
        // Failure path "refresh racing an update": a collection change while the index is being
        // computed must not be lost when that computation is written.
        var computing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(() =>
            {
                computing.TrySetResult();
                release.Task.Wait();
                return [];
            });
        var service = BuildService(libraryManagerMock.Object, Timeout.InfiniteTimeSpan);

        var refresh = Task.Run(() => service.RefreshAsync(CancellationToken.None));
        await computing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        service.Invalidate();
        release.SetResult();

        Assert.True(await refresh);
        Assert.True(service.IsStale());
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentCalls_OnlyOneActuallyRuns()
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.BoxSet))))
            .Returns(() =>
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return [];
            });

        var service = BuildService(libraryManagerMock.Object);

        var firstCallTask = Task.Run(() => service.RefreshAsync(CancellationToken.None));

        var reachedFetch = started.Wait(TimeSpan.FromSeconds(5));
        Assert.True(reachedFetch, "First refresh did not reach the BoxSet-enumeration step in time.");

        var secondAcquired = service.TryAcquireRefreshLock();
        Assert.False(secondAcquired, "A second concurrent refresh should not acquire the lock.");

        release.Set();
        var firstResult = await firstCallTask;
        Assert.True(firstResult);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetCollectionsFor_User_HidesInvisibleCollectionsAndInvisibleItems()
    {
        var user = TestMocks.DefaultUser();
        var visibleMovie = new TestMovie { Id = Guid.NewGuid(), Name = "Visible" };
        var hiddenMovie = new TestMovie { Id = Guid.NewGuid(), Name = "Hidden", VisibleToUser = false };
        var visibleSet = new TestBoxSet { Id = Guid.NewGuid(), Name = "Visible set" };
        var hiddenSet = new TestBoxSet { Id = Guid.NewGuid(), Name = "Hidden set", VisibleToUser = false };
        visibleSet.LinkedChildren = [new LinkedChild { ItemId = visibleMovie.Id }, new LinkedChild { ItemId = hiddenMovie.Id }];
        hiddenSet.LinkedChildren = [new LinkedChild { ItemId = visibleMovie.Id }];

        var byId = new Dictionary<Guid, BaseItem>
        {
            [visibleMovie.Id] = visibleMovie,
            [hiddenMovie.Id] = hiddenMovie,
            [visibleSet.Id] = visibleSet,
            [hiddenSet.Id] = hiddenSet
        };

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Contains(BaseItemKind.BoxSet))))
            .Returns([visibleSet, hiddenSet]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.ItemIds.Length > 0)))
            .Returns<InternalItemsQuery>(q => q.ItemIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList());
        libraryManagerMock.Setup(m => m.GetItemById(It.IsAny<Guid>())).Returns<Guid>(id => byId.GetValueOrDefault(id));

        BaseItem.LibraryManager = libraryManagerMock.Object;

        var service = BuildService(libraryManagerMock.Object);
        await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, service.GetCollectionsFor(visibleMovie.Id).Count);
        Assert.Equal(visibleSet.Id, Assert.Single(service.GetCollectionsFor(visibleMovie.Id, user)).CollectionId);
        Assert.Empty(service.GetCollectionsFor(hiddenMovie.Id, user));
    }
}
