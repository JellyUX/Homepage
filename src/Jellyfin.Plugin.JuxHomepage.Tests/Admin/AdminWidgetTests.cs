using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Admin;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Admin;

public sealed class AdminWidgetTests
{
    // -------------------------------------------------------------------------
    // Descriptor tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Genre_GetDescriptor_HasExpectedProperties()
    {
        var widget = new GenreWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.admin.genre", d.WidgetType);
        Assert.Equal(WidgetCategory.Admin, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Equal(4, d.MinItems);
        Assert.Equal("genre", d.Route);
    }

    [Fact]
    public void Actor_GetDescriptor_HasExpectedProperties()
    {
        var widget = new ActorWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.admin.actor", d.WidgetType);
        Assert.Equal(WidgetCategory.Admin, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Equal(4, d.MinItems);
    }

    [Fact]
    public void Director_GetDescriptor_HasExpectedProperties()
    {
        var widget = new DirectorWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.admin.director", d.WidgetType);
        Assert.Equal(WidgetCategory.Admin, d.Category);
    }

    [Fact]
    public void Studio_GetDescriptor_HasExpectedProperties()
    {
        var widget = new StudioWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.admin.studio", d.WidgetType);
        Assert.Equal(WidgetCategory.Admin, d.Category);
    }

    [Fact]
    public void Collection_GetDescriptor_HasExpectedProperties()
    {
        var widget = new CollectionWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.admin.collection", d.WidgetType);
        Assert.Equal(WidgetCategory.Admin, d.Category);

        // A new Collection row starts at 4 cards like every library section (TODO_V4.md 9.3, F-57).
        Assert.Equal(4, d.MinItems);
    }

    [Fact]
    public void Tag_GetDescriptor_HasExpectedProperties()
    {
        var widget = new TagWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.admin.tag", d.WidgetType);
        Assert.Equal(WidgetCategory.Admin, d.Category);
    }

    [Fact]
    public void Year_GetDescriptor_HasExpectedProperties()
    {
        var widget = new YearWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.admin.year", d.WidgetType);
        Assert.Equal(WidgetCategory.Admin, d.Category);
    }

    [Fact]
    public void GetDescriptor_SeeAll_PointsToTheNativePageOfTheValue_ExceptForAYear()
    {
        // TODO_V4.md 9.3 (F-56): #/list ignores a year, every other value has a native page.
        var users = new Mock<IUserManager>().Object;
        var library = new Mock<ILibraryManager>().Object;
        var dtos = new Mock<IDtoService>().Object;
        IWidget[] widgets =
        [
            new ActorWidget(users, library, dtos),
            new DirectorWidget(users, library, dtos),
            new StudioWidget(users, library, dtos),
            new TagWidget(users, library, dtos),
            new CollectionWidget(users, library, dtos),
            new YearWidget(users, library, dtos)
        ];

        Assert.Equal(["person", "person", "studio", "tag", "item", null], widgets.Select(w => w.GetDescriptor().Route));
    }

    // -------------------------------------------------------------------------
    // Early-out tests: null user and empty value
    // -------------------------------------------------------------------------

    // GenreWidget does not override GetItemsAsync -- this exercises the null-user check shared,
    // unmodified, by every AdminWidgetBase subclass (Actor/Director/Studio/Collection/Tag/Year).
    // Do not add a per-widget duplicate of this test; it would exercise the exact same code path
    // (see Phase 2 test triage, TODO_V2.md).
    [Fact]
    public async Task Genre_UserNotFound_ReturnsEmpty()
    {
        var widget = new GenreWidget(
            TestMocks.UserManagerReturningNull().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), AdditionalData = "Action" },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    // Same rationale as above: the empty-AdditionalData early-out lives in the shared
    // AdminWidgetBase.GetItemsAsync, not in GenreWidget itself.
    [Fact]
    public async Task Genre_EmptyAdditionalData_ReturnsEmpty()
    {
        var widget = new GenreWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), AdditionalData = null },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    // -------------------------------------------------------------------------
    // Resolve test
    // -------------------------------------------------------------------------

    [Fact]
    public void Genre_Resolve_ReturnsSameInstance()
    {
        var widget = new GenreWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var resolved = widget.Resolve(Guid.NewGuid(), new WidgetInstanceConfig(), 3);

        Assert.Same(widget, resolved);
    }

    // -------------------------------------------------------------------------
    // GetAvailableValues tests (AdminWidgetBase.FilterAndProject, Phase 5.3)
    // -------------------------------------------------------------------------

    // StudioWidget is not covered by a separate test: it shares the exact same
    // FilterAndProject code path as GenreWidget (see Phase 2 test triage, TODO_V2.md).
    [Fact]
    public void Genre_GetAvailableValues_FiltersAndSortsCaseInsensitively()
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(m => m.GetGenres(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<(BaseItem Item, ItemCounts ItemCounts)>(
            [
                (new Genre { Name = "Comedy" }, new ItemCounts()),
                (new Genre { Name = "Action" }, new ItemCounts()),
                (new Genre { Name = "Sci-Fi Comedy" }, new ItemCounts())
            ]));

        var widget = new GenreWidget(
            new Mock<IUserManager>().Object,
            libraryManager.Object,
            new Mock<IDtoService>().Object);

        var values = widget.GetAvailableValues(new User("test", "Default", "Default"), "COM");

        Assert.Equal(2, values.Count);
        Assert.Equal("Comedy", values[0].Value);
        Assert.Equal("Sci-Fi Comedy", values[1].Value);
    }

    [Fact]
    public void Tag_GetAvailableValues_RespectsMaxReturnedTags()
    {
        var libraryManager = new Mock<ILibraryManager>();
        var items = Enumerable.Range(0, 250)
            .Select(i => (BaseItem)new Movie { Tags = [$"tag{i:000}"] })
            .ToList();

        libraryManager
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(items));

        var widget = new TagWidget(
            new Mock<IUserManager>().Object,
            libraryManager.Object,
            new Mock<IDtoService>().Object);

        var values = widget.GetAvailableValues(new User("test", "Default", "Default"), null);

        Assert.Equal(200, values.Count);
    }
}
