using MediaBrowser.Controller.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class CountingLibraryManagerTests
{
    [Fact]
    public void Call_ForwardsToInnerSetupAndCountsPerMethod()
    {
        var library = new TestMocks.CountingLibraryManager();
        var id = Guid.NewGuid();
        library.Inner.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([id]);

        var first = library.Object.GetItemIds(new InternalItemsQuery());
        library.Object.GetItemIds(new InternalItemsQuery());
        library.Object.GetItemList(new InternalItemsQuery());

        Assert.Equal([id], first);
        Assert.Equal(2, library.Count("GetItemIds"));
        Assert.Equal(1, library.Count("GetItemList"));
        Assert.Equal(0, library.Count("GetItemById"));
        Assert.Equal(3, library.Total);
    }

    [Fact]
    public void Call_WithoutSetup_ReturnsAnEmptyValue()
    {
        var library = new TestMocks.CountingLibraryManager();

        var items = library.Object.GetItemList(new InternalItemsQuery());
        var result = library.Object.GetItemsResult(new InternalItemsQuery());
        var ids = library.Object.GetItemIds(new InternalItemsQuery());
        var item = library.Object.GetItemById(Guid.NewGuid());

        Assert.NotNull(items);
        Assert.Empty(items);
        Assert.Empty(result.Items);
        Assert.Empty(ids);
        Assert.Null(item);
    }

    [Fact]
    public void Call_InnerThrows_RethrowsTheOriginalException()
    {
        var library = new TestMocks.CountingLibraryManager();
        library.Inner.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Throws(new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => library.Object.GetItemIds(new InternalItemsQuery()));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(1, library.Count("GetItemIds"));
    }

    [Fact]
    public async Task MaxInFlight_TwoCallsHeldTogether_IsTwo()
    {
        var library = new TestMocks.CountingLibraryManager();
        using var bothInside = new Barrier(2);
        library.Inner.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(() =>
            {
                Assert.True(bothInside.SignalAndWait(TimeSpan.FromSeconds(10)));
                return [];
            });

        await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => library.Object.GetItemIds(new InternalItemsQuery()))));

        Assert.Equal(2, library.MaxInFlight);
    }

    [Fact]
    public void MaxInFlight_SequentialBlockedCalls_IsOne()
    {
        var library = new TestMocks.CountingLibraryManager { BlockPerCall = TimeSpan.FromMilliseconds(5) };

        library.Object.GetItemIds(new InternalItemsQuery());
        library.Object.GetItemIds(new InternalItemsQuery());

        Assert.Equal(1, library.MaxInFlight);
        Assert.Equal(2, library.Count("GetItemIds"));
    }

    [Fact]
    public void Reset_ClearsCountsAndPeakButKeepsSetups()
    {
        var library = new TestMocks.CountingLibraryManager();
        var id = Guid.NewGuid();
        library.Inner.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([id]);
        library.Object.GetItemIds(new InternalItemsQuery());

        library.Reset();
        var after = library.Object.GetItemIds(new InternalItemsQuery());

        Assert.Equal([id], after);
        Assert.Equal(1, library.Total);
        Assert.Equal(1, library.MaxInFlight);
    }
}
