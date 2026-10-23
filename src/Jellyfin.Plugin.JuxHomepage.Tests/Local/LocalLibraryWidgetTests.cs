using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Local;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Local;

public sealed class LocalLibraryWidgetTests
{
    private static readonly User TestUser = new("test", "Default", "Default");
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);

    private readonly List<InternalItemsQuery> _queries = [];

    private (Mock<IUserManager> Users, Mock<ILibraryManager> Library, Mock<IDtoService> Dtos) BuildMocks()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(TestUser);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_queries.Add)
            .Returns(new QueryResult<BaseItem>([new Movie { Name = "A" }]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(s => s.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((src, _, _, _) => src.Select(i => new BaseItemDto { Name = i.Name }).ToList());

        return (userManagerMock, libraryManagerMock, dtoServiceMock);
    }

    private LocalLibraryWidgetBase BuildReleased()
    {
        var (users, library, dtos) = BuildMocks();
        return new RecentlyReleasedWidget(users.Object, library.Object, dtos.Object, new ManualTimeProvider { UtcNowValue = Now });
    }

    private LocalLibraryWidgetBase BuildTopRated()
    {
        var (users, library, dtos) = BuildMocks();
        return new TopRatedWidget(users.Object, library.Object, dtos.Object, new ManualTimeProvider { UtcNowValue = Now });
    }

    private static WidgetPayload Payload(string? itemTypes = null) => new()
    {
        UserId = TestUser.Id,
        Limit = 20,
        // A sort saved on the row never applies: the order belongs to the widget.
        SortBy = WidgetSort.SortName,
        SortOrder = WidgetSort.Ascending,
        ExtraParams = itemTypes is null ? null : new Dictionary<string, string> { [LocalLibraryWidgetBase.ItemTypesParam] = itemTypes }
    };

    [Fact]
    public async Task RecentlyReleased_ReadsOnePageNewestReleaseFirst_WithoutFutureItemsNorTotal()
    {
        var result = await BuildReleased().GetItemsAsync(Payload("Movie"), CancellationToken.None);

        var query = Assert.Single(_queries);
        Assert.Single(result.Items);
        Assert.Equal([BaseItemKind.Movie], query.IncludeItemTypes);
        Assert.Equal(Now.UtcDateTime, query.MaxPremiereDate);
        Assert.False(query.EnableTotalRecordCount);
        Assert.Equal((ItemSortBy.PremiereDate, SortOrder.Descending), query.OrderBy[0]);
        Assert.Equal(20, query.Limit);
    }

    [Fact]
    public async Task TopRated_ReadsOnePageBestRatedFirst_WithoutUnratedItems()
    {
        await BuildTopRated().GetItemsAsync(Payload("Series"), CancellationToken.None);

        var query = Assert.Single(_queries);
        Assert.Equal([BaseItemKind.Series], query.IncludeItemTypes);
        Assert.Equal(TopRatedWidget.MinCommunityRating, query.MinCommunityRating);
        Assert.False(query.EnableTotalRecordCount);
        Assert.Equal((ItemSortBy.CommunityRating, SortOrder.Descending), query.OrderBy[0]);
    }

    // KTW-01.a, KTW-02.a: a featured Top Rated reads rich items at or above the row's minimum rating, every
    // title kept (the backdrops go first); Top Rated keeps its own floor when the row sets none.
    [Theory]
    [InlineData(0, TopRatedWidget.MinCommunityRating)]
    [InlineData(7, 7.0)]
    public async Task TopRated_FeaturedWithFeaturedMinRating_ReadsRichItemsAboveTheMinimum(int minRating, double expectedFloor)
    {
        var payload = Payload("Movie,Series");
        payload.Featured = true;
        payload.FeaturedMinRating = minRating;

        await BuildTopRated().GetItemsAsync(payload, CancellationToken.None);

        var query = Assert.Single(_queries);
        Assert.Empty(query.ImageTypes);
        Assert.Equal(expectedFloor, query.MinCommunityRating!.Value, 3);
        Assert.Contains(ItemFields.Overview, query.DtoOptions.Fields);
        Assert.Contains(ImageType.Logo, query.DtoOptions.ImageTypes);
        Assert.Equal((ItemSortBy.CommunityRating, SortOrder.Descending), query.OrderBy[0]);
    }

    // KTW-02.a, D-07: a row of cards is read exactly as before, whatever minimum rating it stores.
    [Fact]
    public async Task TopRated_RowOfCardsWithAStoredFeaturedMinRating_IsReadUnchanged()
    {
        var payload = Payload("Movie,Series");
        payload.FeaturedMinRating = 8;

        await BuildTopRated().GetItemsAsync(payload, CancellationToken.None);

        var query = Assert.Single(_queries);
        Assert.Empty(query.ImageTypes);
        Assert.Equal(TopRatedWidget.MinCommunityRating, query.MinCommunityRating);
        Assert.Equal(WidgetDtoOptions.Standard().Fields, query.DtoOptions.Fields);
        Assert.Equal(WidgetDtoOptions.Standard().ImageTypes, query.DtoOptions.ImageTypes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Movie,Series")]
    [InlineData("Episode")]
    public void ParseItemTypes_FallsBackToMoviesAndSeries(string? itemTypes)
    {
        var extra = itemTypes is null ? null : new Dictionary<string, string> { [LocalLibraryWidgetBase.ItemTypesParam] = itemTypes };

        Assert.Equal([BaseItemKind.Movie, BaseItemKind.Series], LocalLibraryWidgetBase.ParseItemTypes(extra));
    }

    [Fact]
    public void Descriptors_AreLocalAdminSectionsOfFourCards()
    {
        foreach (var widget in new[] { BuildReleased(), BuildTopRated() })
        {
            var d = widget.GetDescriptor();
            Assert.StartsWith("jux.local.", d.WidgetType, StringComparison.Ordinal);
            Assert.Equal(WidgetCategory.Admin, d.Category);
            Assert.Equal(4, d.MinItems);
        }
    }
}
