using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// KTW-01.a and KTW-02.a: what a featured section reads that a row of cards does not.
/// </summary>
public sealed class FeaturedItemsTests
{
    private static readonly WidgetPayload RowOfCards = new() { FeaturedMinRating = 8 };
    private static readonly WidgetPayload FeaturedAtSeven = new() { Featured = true, FeaturedMinRating = 7 };

    [Fact]
    public void Keeps_FeaturedWithFeaturedMinRating_KeepsOnlyRatedItemsWithABackdrop()
    {
        var rated = MovieWith(8.1f, backdrop: true);
        var low = MovieWith(6.5f, backdrop: true);
        var bare = MovieWith(9f, backdrop: false);
        var unrated = MovieWith(null, backdrop: true);

        Assert.Equal([true, false, false, false], new[] { rated, low, bare, unrated }.Select(m => FeaturedItems.Keeps(m, FeaturedAtSeven)));
        Assert.True(FeaturedItems.Keeps(unrated, new WidgetPayload { Featured = true }));
        Assert.False(FeaturedItems.Keeps(bare, new WidgetPayload { Featured = true }));
    }

    [Fact]
    public void Keeps_RowOfCards_KeepsEveryItem()
    {
        Assert.True(FeaturedItems.Keeps(MovieWith(null, backdrop: false), RowOfCards));
    }

    [Fact]
    public void DtoOptionsFor_FeaturedAddsTheSlideFieldsAndLogo_RowOfCardsKeepsTheStandardOnes()
    {
        var featured = FeaturedItems.DtoOptionsFor(FeaturedAtSeven);
        var standard = FeaturedItems.DtoOptionsFor(RowOfCards);

        Assert.Superset(new HashSet<ItemFields>(WidgetDtoOptions.Standard().Fields), new HashSet<ItemFields>(featured.Fields));
        Assert.Superset(new HashSet<ItemFields> { ItemFields.Overview, ItemFields.Taglines, ItemFields.Genres }, new HashSet<ItemFields>(featured.Fields));
        Assert.Equal([ImageType.Primary, ImageType.Backdrop, ImageType.Logo], featured.ImageTypes);
        Assert.Equal(1, featured.ImageTypeLimit);
        Assert.Equal(WidgetDtoOptions.Standard().Fields, standard.Fields);
    }

    [Fact]
    public void GetSectionItems_FeaturedWatchlist_IsRestrictedLikeAnyFeaturedSection()
    {
        var user = TestMocks.DefaultUser();
        var queries = new List<InternalItemsQuery>();
        var users = new Mock<IUserManager>();
        users.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>())).Callback<InternalItemsQuery>(queries.Add).Returns(new QueryResult<BaseItem>());
        var service = new WatchlistService(users.Object, library.Object, Mock.Of<IDtoService>());

        service.GetSectionItems(new WidgetPayload { UserId = user.Id, Limit = 20, Featured = true, FeaturedMinRating = 7 }, CancellationToken.None);
        service.GetSectionItems(new WidgetPayload { UserId = user.Id, Limit = 20 }, CancellationToken.None);

        Assert.Equal([[ImageType.Backdrop], []], queries.Select(q => q.ImageTypes));
        Assert.Equal([7.0, null], queries.Select(q => (double?)q.MinCommunityRating));
        Assert.All(queries, q => Assert.True(q.IsLiked));
    }

    private static Movie MovieWith(float? rating, bool backdrop) => new()
    {
        Id = Guid.NewGuid(),
        CommunityRating = rating,
        ImageInfos = backdrop ? [new ItemImageInfo { Type = ImageType.Backdrop, Path = "/backdrop.jpg" }] : []
    };
}
