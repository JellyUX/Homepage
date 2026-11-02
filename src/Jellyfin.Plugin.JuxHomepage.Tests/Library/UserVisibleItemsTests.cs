using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Library;

/// <summary>
/// PERF-03 (DR-007): a long cached list is checked against the user's rights with a lean read, then only
/// the page is read again, the way a single read did before, to be projected.
/// </summary>
public sealed class UserVisibleItemsTests
{
    private readonly User _user = new("test", "Default", "Default");

    [Fact]
    public void VisibleIds_ReadsLean_AndKeepsTheVisibleIdsInInputOrder()
    {
        var visibleA = new TestMovie { Id = Guid.NewGuid() };
        var hidden = new TestMovie { Id = Guid.NewGuid(), VisibleToUser = false };
        var visibleB = new TestMovie { Id = Guid.NewGuid() };
        var removed = Guid.NewGuid();
        var queries = new List<InternalItemsQuery>();
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns([visibleA, hidden, visibleB]);

        var ids = UserVisibleItems.VisibleIds(library.Object, _user, [visibleB.Id, removed, hidden.Id, visibleA.Id, visibleB.Id], out var unresolved);

        Assert.Equal([visibleB.Id, visibleA.Id, visibleB.Id], ids);
        Assert.Equal(1, unresolved);
        var query = Assert.Single(queries);
        Assert.Equal(4, query.ItemIds.Length);
        Assert.Empty(query.DtoOptions.Fields);
        Assert.False(query.DtoOptions.EnableImages);
        Assert.False(query.DtoOptions.EnableUserData);
    }

    [Fact]
    public void LoadPage_ReadsThePageAsASingleReadDid_InPageOrder()
    {
        var first = new TestMovie { Id = Guid.NewGuid() };
        var second = new TestMovie { Id = Guid.NewGuid() };
        var queries = new List<InternalItemsQuery>();
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns([second, first]);

        var page = UserVisibleItems.LoadPage(library.Object, [first.Id, Guid.NewGuid(), second.Id]);

        Assert.Equal([first, second], page);
        var query = Assert.Single(queries);
        var single = new InternalItemsQuery();
        Assert.Equal(single.DtoOptions.Fields, query.DtoOptions.Fields);
        Assert.Equal(single.DtoOptions.EnableImages, query.DtoOptions.EnableImages);
        Assert.Equal(single.DtoOptions.EnableUserData, query.DtoOptions.EnableUserData);
    }

    [Fact]
    public void LoadPage_EmptyPage_DoesNotReadTheLibrary()
    {
        var library = new Mock<ILibraryManager>(MockBehavior.Strict);

        Assert.Empty(UserVisibleItems.LoadPage(library.Object, []));
    }
}
