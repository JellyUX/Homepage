using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Connected;

public sealed class SeasonalWidgetTests
{
    private static readonly User TestUser = new("test", "Default", "Default");

    private static SeasonalWidget BuildWidget(
        out Mock<ILibraryManager> libraryManagerMock,
        IReadOnlyList<BaseItem>? items = null,
        TimeProvider? timeProvider = null)
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(TestUser);

        libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(items ?? []));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(s => s.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem>(
                (src, _, _, _) => src.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList());

        return new SeasonalWidget(userManagerMock.Object, libraryManagerMock.Object, dtoServiceMock.Object, timeProvider);
    }

    private static WidgetPayload BuildPayload(
        string? startDate,
        string? endDate,
        string? genre = null,
        string? tag = null)
    {
        var extra = new Dictionary<string, string>();
        if (startDate is not null)
        {
            extra["startDate"] = startDate;
        }

        if (endDate is not null)
        {
            extra["endDate"] = endDate;
        }

        if (genre is not null)
        {
            extra["genre"] = genre;
        }

        if (tag is not null)
        {
            extra["tag"] = tag;
        }

        return new WidgetPayload { UserId = TestUser.Id, Limit = 20, ExtraParams = extra };
    }

    // -------------------------------------------------------------------------
    // Descriptor
    // -------------------------------------------------------------------------

    [Fact]
    public void GetDescriptor_HasExpectedProperties()
    {
        var widget = BuildWidget(out _);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.connected.seasonal", d.WidgetType);
        Assert.Equal(WidgetCategory.Connected, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Equal(4, d.MinItems);
    }

    // -------------------------------------------------------------------------
    // Date window (in-season / out-of-season / wraparound / invalid)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetItemsAsync_MissingDates_ReturnsEmpty()
    {
        var widget = BuildWidget(out var libraryManagerMock, items: [new Movie { Name = "A" }]);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = TestUser.Id, Limit = 20, ExtraParams = new Dictionary<string, string>() },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        libraryManagerMock.Verify(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task GetItemsAsync_InvalidDateFormat_ReturnsEmpty()
    {
        var widget = BuildWidget(out _, items: [new Movie { Name = "A" }]);

        var result = await widget.GetItemsAsync(
            BuildPayload("October 1st", "10-31"),
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public async Task GetItemsAsync_TodayOutsideNonWrappingWindow_ReturnsEmpty()
    {
        // A short, fixed 3-day window (days 10-12) in a month roughly six months away from today's
        // month -- far enough from today's own month that it can never accidentally include today,
        // and entirely within one calendar month so it can never accidentally wrap across the year
        // boundary (which would exercise the wraparound branch tested separately below instead).
        var windowMonth = ((DateTime.Now.Month + 5) % 12) + 1;

        var widget = BuildWidget(out var libraryManagerMock, items: [new Movie { Name = "A" }]);

        var result = await widget.GetItemsAsync(
            BuildPayload($"{windowMonth:D2}-10", $"{windowMonth:D2}-12"),
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        libraryManagerMock.Verify(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task GetItemsAsync_TodayInsideNonWrappingWindow_QueriesLibrary()
    {
        var today = DateTime.Now;
        var start = today.AddDays(-1);
        var end = today.AddDays(1);

        var widget = BuildWidget(out var libraryManagerMock, items: [new Movie { Name = "A" }]);

        var result = await widget.GetItemsAsync(
            BuildPayload($"{start.Month:D2}-{start.Day:D2}", $"{end.Month:D2}-{end.Day:D2}"),
            CancellationToken.None);

        Assert.Equal(1, result.TotalRecordCount);
        libraryManagerMock.Verify(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    [Fact]
    public async Task GetItemsAsync_TodayInsideWrappingWindow_QueriesLibrary()
    {
        // A window that wraps across the new year (start > end, e.g. Dec 26 to Jan 6) but always
        // covers "today" regardless of when the test runs, without depending on today actually being
        // near a year boundary: a window starting exactly today and ending exactly yesterday has
        // start > end in (Month, Day) tuple terms whenever the month doesn't change between the two
        // (the common case) as well as when it does (a real year/month wrap) -- either way, "today"
        // trivially satisfies "t >= start" since t == start.
        var today = DateTime.Now;
        var start = today;
        var end = today.AddDays(-1);

        var widget = BuildWidget(out var libraryManagerMock, items: [new Movie { Name = "A" }]);

        var result = await widget.GetItemsAsync(
            BuildPayload($"{start.Month:D2}-{start.Day:D2}", $"{end.Month:D2}-{end.Day:D2}"),
            CancellationToken.None);

        Assert.Equal(1, result.TotalRecordCount);
        libraryManagerMock.Verify(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Genre / tag filters
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetItemsAsync_GenreProvided_AppliesGenreFilter()
    {
        var today = DateTime.Now;
        var start = today.AddDays(-1);
        var end = today.AddDays(1);

        InternalItemsQuery? capturedQuery = null;
        var widget = BuildWidget(out var libraryManagerMock, items: [new Movie { Name = "A" }]);
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([new Movie { Name = "A" }]));

        await widget.GetItemsAsync(
            BuildPayload($"{start.Month:D2}-{start.Day:D2}", $"{end.Month:D2}-{end.Day:D2}", genre: "Horror"),
            CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal(["Horror"], capturedQuery!.Genres);
    }

    [Fact]
    public async Task GetItemsAsync_TagProvided_AppliesTagFilter()
    {
        var today = DateTime.Now;
        var start = today.AddDays(-1);
        var end = today.AddDays(1);

        InternalItemsQuery? capturedQuery = null;
        var widget = BuildWidget(out var libraryManagerMock, items: [new Movie { Name = "A" }]);
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([new Movie { Name = "A" }]));

        await widget.GetItemsAsync(
            BuildPayload($"{start.Month:D2}-{start.Day:D2}", $"{end.Month:D2}-{end.Day:D2}", tag: "spooky"),
            CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal(["spooky"], capturedQuery!.Tags);
    }

    [Fact]
    public async Task GetItemsAsync_UnknownUser_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var widget = new SeasonalWidget(
            userManagerMock.Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object);

        var today = DateTime.Now;
        var start = today.AddDays(-1);
        var end = today.AddDays(1);

        var result = await widget.GetItemsAsync(
            BuildPayload($"{start.Month:D2}-{start.Day:D2}", $"{end.Month:D2}-{end.Day:D2}"),
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
    }

    // -------------------------------------------------------------------------
    // Time source and MM-dd parsing (TODO_V4.md 5.5, F-23)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2027-01-03", 1)]
    [InlineData("2026-12-26", 1)]
    [InlineData("2027-02-01", 0)]
    public async Task GetItemsAsync_YearWrappingWindow_FollowsTheInjectedClock(string today, int expected)
    {
        var clock = new ManualTimeProvider { UtcNowValue = DateTimeOffset.Parse(today + "T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture) };
        var widget = BuildWidget(out _, [new Movie { Id = Guid.NewGuid(), Name = "Fireworks" }], clock);

        var result = await widget.GetItemsAsync(BuildPayload("12-26", "01-06"), CancellationToken.None);

        Assert.Equal(expected, result.Items.Count);
    }

    [Theory]
    [InlineData("02-29", true)]
    [InlineData("10-31", true)]
    [InlineData("02-30", false)]
    [InlineData("13-01", false)]
    [InlineData("1-5", false)]
    [InlineData("", false)]
    public void TryParseMonthDay_AcceptsEveryRealDayOfAnyYear(string value, bool expected)
    {
        Assert.Equal(expected, SeasonalWidget.TryParseMonthDay(value, out _));
    }
}
