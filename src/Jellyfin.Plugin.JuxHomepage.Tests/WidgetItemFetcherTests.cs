using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Admin;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Configurable section order (TODO_V4.md 8.1, F-53): a random order is drawn once per user, section
/// and day, and only from the items the user may see.
/// </summary>
public sealed class WidgetItemFetcherTests
{
    private readonly Dictionary<Guid, TestMovie> _items = Enumerable.Range(0, 30)
        .Select(i => new TestMovie { Id = Guid.NewGuid(), Name = "Movie " + i })
        .ToDictionary(m => m.Id);

    private readonly Mock<ILibraryManager> _library = new();
    private readonly ManualTimeProvider _clock = new() { UtcNowValue = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero) };
    private readonly List<InternalItemsQuery> _idQueries = [];

    public WidgetItemFetcherTests()
    {
        _library
            .Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_idQueries.Add)
            .Returns(() => _items.Keys.ToList());
        _library
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.ItemIds.Length > 0)))
            .Returns<InternalItemsQuery>(q => q.ItemIds.Select(id => (BaseItem)_items[id]).ToList());
    }

    [Fact]
    public void Random_SameDay_DrawsOnceAndServesEveryPageFromTheSameOrder()
    {
        var fetcher = BuildFetcher();
        var user = TestMocks.DefaultUser();

        var probe = Ids(fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), RandomPayload(limit: 4)));
        var first = Ids(fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), RandomPayload(limit: 20)));
        var reload = Ids(fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), RandomPayload(limit: 20)));

        Assert.Single(_idQueries);
        Assert.Equal(first, reload);
        Assert.Equal(first.Take(4), probe);
        Assert.NotEqual(_items.Keys.Take(20), first);
    }

    [Fact]
    public void Random_NextDay_DrawsAgainInAnotherOrderAndDropsTheOldDraw()
    {
        var fetcher = BuildFetcher();
        var user = TestMocks.DefaultUser();
        var today = Ids(fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), RandomPayload(limit: 20)));

        _clock.UtcNowValue = _clock.UtcNowValue.AddDays(1);
        var tomorrow = Ids(fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), RandomPayload(limit: 20)));

        Assert.Equal(2, _idQueries.Count);
        Assert.NotEqual(today, tomorrow);
        Assert.Equal(1, fetcher.DrawCountForTesting());
    }

    [Fact]
    public void Random_DrawsPerUserAndPerSection_WithTheUsersOwnQuery()
    {
        var fetcher = BuildFetcher();
        var alice = TestMocks.DefaultUser();
        var bob = TestMocks.DefaultUser();

        fetcher.Fetch("jux.admin.genre", alice, new InternalItemsQuery(alice), new DtoOptions(), RandomPayload(limit: 20));
        fetcher.Fetch("jux.admin.genre", bob, new InternalItemsQuery(bob), new DtoOptions(), RandomPayload(limit: 20));
        fetcher.Fetch("jux.admin.genre", alice, new InternalItemsQuery(alice), new DtoOptions(), RandomPayload(limit: 20, value: "Drama"));

        Assert.Equal(3, _idQueries.Count);
        Assert.Same(alice, _idQueries[0].User);
        Assert.Same(bob, _idQueries[1].User);
        Assert.Equal(3, fetcher.DrawCountForTesting());
    }

    [Fact]
    public void Random_KeepsMaxItemsIdsAndNeverReturnsAnItemHiddenFromTheUser()
    {
        var hidden = new TestMovie { Id = Guid.NewGuid(), Name = "Hidden", VisibleToUser = false };
        _items[hidden.Id] = hidden;
        var fetcher = BuildFetcher();
        var user = TestMocks.DefaultUser();

        var all = Ids(fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), RandomPayload(limit: 100, maxItems: 31)));
        var page = Ids(fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), RandomPayload(limit: 100, maxItems: 10)));

        Assert.DoesNotContain(hidden.Id, all);
        Assert.Equal(30, all.Count);
        Assert.True(page.Count <= 10);
        Assert.Null(_idQueries[0].Limit);
    }

    [Fact]
    public void Sorted_SetsTheOrderAndThePageOnTheQuery()
    {
        InternalItemsQuery? seen = null;
        _library
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => seen = q)
            .Returns(new QueryResult<BaseItem>());
        var fetcher = BuildFetcher();
        var user = TestMocks.DefaultUser();
        var payload = new WidgetPayload { UserId = user.Id, SortBy = WidgetSort.DateCreated, SortOrder = WidgetSort.Descending, StartIndex = 20, Limit = 10 };

        fetcher.Fetch("jux.admin.genre", user, new InternalItemsQuery(user), new DtoOptions(), payload);

        Assert.NotNull(seen);
        Assert.Equal([(ItemSortBy.DateCreated, SortOrder.Descending), (ItemSortBy.SortName, SortOrder.Ascending)], seen!.OrderBy);
        Assert.Equal(20, seen.StartIndex);
        Assert.Equal(10, seen.Limit);
        Assert.Empty(_idQueries);
    }

    [Theory]
    [InlineData(null, WidgetSort.SortName)]
    [InlineData("Bogus", WidgetSort.SortName)]
    [InlineData(WidgetSort.Random, WidgetSort.Random)]
    [InlineData(WidgetSort.CommunityRating, WidgetSort.CommunityRating)]
    public void NormalizeSortBy_KeepsKnownValuesOnly(string? stored, string expected) =>
        Assert.Equal(expected, WidgetSort.NormalizeSortBy(stored));

    [Fact]
    public async Task GenreWidget_ReadsTheRowOrderThroughTheFetcher()
    {
        InternalItemsQuery? seen = null;
        _library
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => seen = q)
            .Returns(new QueryResult<BaseItem>());
        var user = TestMocks.DefaultUser();
        var widget = new GenreWidget(TestMocks.UserManagerReturning(user).Object, _library.Object, DtoService().Object, BuildFetcher());

        await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, AdditionalData = "Action", SortBy = WidgetSort.PremiereDate, SortOrder = WidgetSort.Descending },
            CancellationToken.None);

        Assert.Equal(ItemSortBy.PremiereDate, seen!.OrderBy[0].OrderBy);
        Assert.Equal(["Action"], seen.Genres);
    }

    private static WidgetPayload RandomPayload(int limit, int maxItems = 20, string value = "Action") => new()
    {
        AdditionalData = value,
        SortBy = WidgetSort.Random,
        StartIndex = 0,
        Limit = limit,
        MaxItems = maxItems
    };

    private static List<Guid> Ids(WidgetResult result) => result.Items.Select(d => d.Id).ToList();

    private static Mock<IDtoService> DtoService()
    {
        var dtos = new Mock<IDtoService>();
        dtos.Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>((items, _, _, _) => items.Select(i => new BaseItemDto { Id = i.Id }).ToList());
        return dtos;
    }

    private WidgetItemFetcher BuildFetcher() => new(_library.Object, DtoService().Object, _clock);
}
