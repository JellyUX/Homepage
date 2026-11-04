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
    public void Keeps_FeaturedWithFeaturedMinRating_KeepsTheItemsRatedAtLeastThat()
    {
        var rated = MovieWith(8.1f, backdrop: true);
        var low = MovieWith(6.5f, backdrop: true);
        var bare = MovieWith(9f, backdrop: false);
        var unrated = MovieWith(null, backdrop: true);

        Assert.Equal([true, false, true, false], new[] { rated, low, bare, unrated }.Select(m => FeaturedItems.Keeps(m, FeaturedAtSeven)));
        Assert.True(FeaturedItems.Keeps(unrated, new WidgetPayload { Featured = true }));
    }

    [Fact]
    public void BackdropsFirst_FeaturedPutsTheBackdropsFirstInOrder_RowOfCardsKeepsItsOrder()
    {
        BaseItem[] items = [MovieWith(1, backdrop: false), MovieWith(2, backdrop: true), MovieWith(3, backdrop: false), MovieWith(4, backdrop: true)];

        Assert.Equal([2f, 4f, 1f, 3f], FeaturedItems.BackdropsFirst(items, FeaturedAtSeven).Select(i => i.CommunityRating!.Value));
        Assert.Same(items, FeaturedItems.BackdropsFirst(items, RowOfCards));
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
        Assert.Superset(new HashSet<ItemFields> { ItemFields.Overview, ItemFields.Taglines }, new HashSet<ItemFields>(featured.Fields));
        Assert.DoesNotContain(ItemFields.Genres, featured.Fields);
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

        Assert.All(queries, q => Assert.Empty(q.ImageTypes));
        Assert.Equal([7.0, null], queries.Select(q => (double?)q.MinCommunityRating));
        Assert.All(queries, q => Assert.True(q.IsLiked));
    }

    private static Movie MovieWith(float? rating, bool backdrop) => new()
    {
        Id = Guid.NewGuid(),
        CommunityRating = rating,
        ImageInfos = backdrop ? [new ItemImageInfo { Type = ImageType.Backdrop, Path = "/backdrop.jpg" }] : []
    };

    // PERF-05: only the titles that can become a slide (the first 8 with a backdrop of the first page) are
    // read with the slide fields; the other titles of a featured section are plain cards.
    [Theory]
    [InlineData(10, 2, 0, 8, 4)]
    [InlineData(3, 5, 0, 3, 5)]
    [InlineData(2, 5, 0, 0, 7)]
    [InlineData(0, 4, 0, 0, 4)]
    [InlineData(10, 2, 20, 0, 12)]
    public void Project_Featured_EnrichesOnlyTheTitlesThatCanBeSlides(int backdrops, int bare, int startIndex, int enriched, int plain)
    {
        var items = FeaturedItems.BackdropsFirst(
            [.. Enumerable.Range(0, bare).Select(i => MovieWith(i, backdrop: false)), .. Enumerable.Range(100, backdrops).Select(i => MovieWith(i, backdrop: true))],
            FeaturedAtSeven);
        var (dto, calls) = RecordingDtoService();

        var result = FeaturedItems.Project(dto.Object, items, new WidgetPayload { Featured = true, StartIndex = startIndex }, TestMocks.DefaultUser(), 40);

        Assert.Equal(items.Select(i => i.Id), result.ItemIds!);
        Assert.Equal(items.Select(i => i.Id), result.Items.Select(d => d.Id));
        Assert.Equal(40, result.TotalRecordCount);
        Assert.Equal(enriched, calls.Where(c => c.Slides).Sum(c => c.Count));
        Assert.Equal(plain, calls.Where(c => !c.Slides).Sum(c => c.Count));
        Assert.DoesNotContain(calls, c => c.Count == 0);
    }

    [Fact]
    public void Project_RowOfCards_ReadsEveryTitleAsACard()
    {
        BaseItem[] items = [MovieWith(1, backdrop: true), MovieWith(2, backdrop: false)];
        var (dto, calls) = RecordingDtoService();

        var result = FeaturedItems.Project(dto.Object, items, RowOfCards, TestMocks.DefaultUser());

        Assert.Equal(2, result.TotalRecordCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal([(2, false)], calls.Select(c => (c.Count, c.Slides)));
    }

    private static (Mock<IDtoService> Dto, List<(int Count, bool Slides)> Calls) RecordingDtoService()
    {
        var calls = new List<(int Count, bool Slides)>();
        var dto = new Mock<IDtoService>();
        dto.StubBaseItemDtos((items, options) =>
        {
            calls.Add((items.Count, options.ImageTypes.Contains(ImageType.Logo)));
            Assert.Equal(options.ImageTypes.Contains(ImageType.Logo) ? WidgetDtoOptions.Featured().Fields : WidgetDtoOptions.Standard().Fields, options.Fields);
            return [.. items.Select(i => new BaseItemDto { Id = i.Id })];
        });
        return (dto, calls);
    }
}
