using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Admin;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Admin;

/// <summary>
/// An Admin widget whose configured value no longer resolves (deleted collection, malformed id,
/// unknown studio...) must return nothing, never fall through to an unfiltered query over the whole
/// library (with a full COUNT on top).
/// </summary>
public sealed class AdminUnresolvedValueTests
{
    private static Mock<ILibraryManager> LibraryReturningEverything()
    {
        var mock = new Mock<ILibraryManager>();
        mock.Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(0, 3000, [new TestMovie { Id = Guid.NewGuid() }]));
        return mock;
    }

    private static async Task AssertEmptyWithoutLibraryQuery(AdminWidgetBase widget, Mock<ILibraryManager> libraryManagerMock, string value)
    {
        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), AdditionalData = value, Limit = 20 },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
        libraryManagerMock.Verify(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task Collection_DeletedCollection_ReturnsEmpty()
    {
        var library = LibraryReturningEverything();
        library.Setup(m => m.GetItemById(It.IsAny<Guid>())).Returns((BaseItem?)null);
        var widget = new CollectionWidget(TestMocks.UserManagerReturning(TestMocks.DefaultUser()).Object, library.Object, TestMocks.DtoServiceReturningEmpty().Object);

        await AssertEmptyWithoutLibraryQuery(widget, library, Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task Collection_NonGuidValue_ReturnsEmpty()
    {
        var library = LibraryReturningEverything();
        var widget = new CollectionWidget(TestMocks.UserManagerReturning(TestMocks.DefaultUser()).Object, library.Object, TestMocks.DtoServiceReturningEmpty().Object);

        await AssertEmptyWithoutLibraryQuery(widget, library, "not-a-guid");
    }

    // Also covers a collection with no member visible to the user: GetLinkedChildren(user) returns
    // the same empty list, and the widget takes the same branch.
    [Fact]
    public async Task Collection_EmptyCollection_ReturnsEmpty()
    {
        var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = "Empty" };
        var library = LibraryReturningEverything();
        library.Setup(m => m.GetItemById(boxSet.Id)).Returns(boxSet);
        BaseItem.LibraryManager = library.Object;
        var widget = new CollectionWidget(TestMocks.UserManagerReturning(TestMocks.DefaultUser()).Object, library.Object, TestMocks.DtoServiceReturningEmpty().Object);

        await AssertEmptyWithoutLibraryQuery(widget, library, boxSet.Id.ToString());
    }

    [Fact]
    public async Task Year_NonNumericValue_ReturnsEmpty()
    {
        var library = LibraryReturningEverything();
        var widget = new YearWidget(TestMocks.UserManagerReturning(TestMocks.DefaultUser()).Object, library.Object, TestMocks.DtoServiceReturningEmpty().Object);

        await AssertEmptyWithoutLibraryQuery(widget, library, "nineteen-ninety");
    }

    [Fact]
    public async Task Studio_UnknownStudio_ReturnsEmpty()
    {
        var library = LibraryReturningEverything();
        library.Setup(m => m.GetStudioId(It.IsAny<string>())).Returns(Guid.Empty);
        var widget = new StudioWidget(TestMocks.UserManagerReturning(TestMocks.DefaultUser()).Object, library.Object, TestMocks.DtoServiceReturningEmpty().Object);

        await AssertEmptyWithoutLibraryQuery(widget, library, "No Such Studio");
    }
}
