using Jellyfin.Plugin.JuxHomepage.Watchlist;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Watchlist;

/// <summary>
/// Progress, History and Statistics tabs current after each watch (TODO_V4.md 8.2, F-54).
/// </summary>
public sealed class TabCachesUpdateListenerTests
{
    private readonly Mock<ISeriesProgressCacheService> _progress = new();
    private readonly Mock<IMovieHistoryCacheService> _history = new();
    private readonly TabCachesUpdateListener _listener;

    public TabCachesUpdateListenerTests()
    {
        _listener = new TabCachesUpdateListener(
            new Mock<IUserDataManager>().Object,
            _progress.Object,
            _history.Object,
            NullLogger<TabCachesUpdateListener>.Instance,
            TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task EpisodesOfOneSeriesMarkedPlayed_BecomeOneUpdateOfThatSeries()
    {
        var userId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();

        var applied = _listener.Record(Save(userId, new Episode { Id = Guid.NewGuid(), SeriesId = seriesId }, UserDataSaveReason.TogglePlayed));
        Assert.Null(_listener.Record(Save(userId, new Episode { Id = Guid.NewGuid(), SeriesId = seriesId }, UserDataSaveReason.TogglePlayed)));
        Assert.Null(_listener.Record(Save(userId, new Season { Id = Guid.NewGuid(), SeriesId = seriesId }, UserDataSaveReason.TogglePlayed)));
        await applied!;

        _progress.Verify(p => p.UpdateSeries(userId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { seriesId }))), Times.Once);
    }

    [Fact]
    public async Task FinishedMovie_UpdatesHistory()
    {
        var userId = Guid.NewGuid();
        var movie = new Movie { Id = Guid.NewGuid() };

        await _listener.Record(Save(userId, movie, UserDataSaveReason.PlaybackFinished))!;

        _history.Verify(h => h.UpdateMovies(userId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { movie.Id }))), Times.Once);
    }

    [Theory]
    [InlineData(UserDataSaveReason.PlaybackProgress)]
    [InlineData(UserDataSaveReason.UpdateUserRating)]
    public void OtherSaves_AreIgnored(UserDataSaveReason reason) =>
        Assert.Null(_listener.Record(Save(Guid.NewGuid(), new Movie { Id = Guid.NewGuid() }, reason)));

    private static UserDataSaveEventArgs Save(Guid userId, BaseItem item, UserDataSaveReason reason) => new()
    {
        UserId = userId,
        Item = item,
        SaveReason = reason,
        UserData = new UserItemData { Key = "k" }
    };
}
