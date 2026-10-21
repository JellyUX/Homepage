using Jellyfin.Plugin.JuxHomepage.Configuration;
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
/// PERF-05.a, DEC-P07-7: each user's liked ids stay in memory, follow every like and unlike of a movie
/// or a series and every item removed from the library, and are read again after the layout TTL or
/// the daily task.
/// </summary>
public sealed class LikedItemIdsCacheTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly LikedItemIdsCache _cache;
    private readonly Guid _user = Guid.NewGuid();

    public LikedItemIdsCacheTests()
    {
        _cache = new LikedItemIdsCache(() => new PluginConfiguration { Cache = new CacheConfig { SessionTtlMinutes = 15 } }, _clock);
    }

    [Fact]
    public void StoredIds_AreServedUntilTheLayoutTtlEnds()
    {
        var movie = Guid.NewGuid();
        Assert.True(_cache.Set(_user, [movie], _cache.VersionOf(_user)));

        _clock.UtcNowValue += TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1);
        Assert.True(_cache.TryGet(_user, out var ids));
        Assert.Equal([movie], ids);
        _clock.UtcNowValue += TimeSpan.FromSeconds(1);
        Assert.False(_cache.TryGet(_user, out _));
        Assert.False(_cache.TryGet(Guid.NewGuid(), out _));
    }

    [Fact]
    public void LikedIds_ChangedWhileRead_AreNotStored()
    {
        // Ids queried before a like, an item removal or the daily task reflect the old state.
        var version = _cache.VersionOf(_user);
        _cache.Apply(_user, Guid.NewGuid(), liked: true);
        Assert.False(_cache.Set(_user, [], version));

        version = _cache.VersionOf(_user);
        _cache.Remove(Guid.NewGuid());
        Assert.False(_cache.Set(_user, [], version));

        version = _cache.VersionOf(_user);
        _cache.Clear();
        Assert.False(_cache.Set(_user, [], version));
        Assert.False(_cache.TryGet(_user, out _));
    }

    [Fact]
    public void Clear_DropsEveryUsersIds()
    {
        var other = Guid.NewGuid();
        _cache.Set(_user, [Guid.NewGuid()], _cache.VersionOf(_user));
        _cache.Set(other, [Guid.NewGuid()], _cache.VersionOf(other));

        _cache.Clear();

        Assert.False(_cache.TryGet(_user, out _));
        Assert.False(_cache.TryGet(other, out _));
    }

    [Fact]
    public async Task Listener_LikeAndUnlikeOfAMovieOrSeries_AreAppliedWithoutAQuery()
    {
        var (userData, library, listener) = await StartListener();
        var kept = Guid.NewGuid();
        _cache.Set(_user, [kept], _cache.VersionOf(_user));
        var movie = new Movie { Id = Guid.NewGuid() };
        var series = new Series { Id = Guid.NewGuid() };

        Save(userData, movie, likes: true);
        Save(userData, series, likes: true);
        Assert.True(_cache.TryGet(_user, out var liked));
        Assert.Equal(new HashSet<Guid> { kept, movie.Id, series.Id }, liked.ToHashSet());

        Save(userData, movie, likes: false);
        Save(userData, series, likes: null);
        Assert.True(_cache.TryGet(_user, out var unliked));
        Assert.Equal([kept], unliked);

        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Listener_EpisodeOrOtherUsersSave_LeavesTheIdsAlone()
    {
        var (userData, _, listener) = await StartListener();
        _cache.Set(_user, [], _cache.VersionOf(_user));

        Save(userData, new Episode { Id = Guid.NewGuid() }, likes: true);
        Save(userData, new Movie { Id = Guid.NewGuid() }, likes: true, userId: Guid.NewGuid());

        Assert.True(_cache.TryGet(_user, out var ids));
        Assert.Empty(ids);
        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Listener_ItemRemoved_LeavesEveryUsersIds()
    {
        var (_, library, listener) = await StartListener();
        var other = Guid.NewGuid();
        var removed = new Movie { Id = Guid.NewGuid() };
        var kept = Guid.NewGuid();
        _cache.Set(_user, [removed.Id, kept], _cache.VersionOf(_user));
        _cache.Set(other, [removed.Id], _cache.VersionOf(other));

        library.Raise(m => m.ItemRemoved += null, library.Object, new ItemChangeEventArgs { Item = removed });

        Assert.True(_cache.TryGet(_user, out var mine));
        Assert.Equal([kept], mine);
        Assert.True(_cache.TryGet(other, out var theirs));
        Assert.Empty(theirs);
        await listener.StopAsync(CancellationToken.None);
    }

    private async Task<(Mock<IUserDataManager> UserData, Mock<ILibraryManager> Library, LikedItemIdsUpdateListener Listener)> StartListener()
    {
        var userData = new Mock<IUserDataManager>();
        var library = new Mock<ILibraryManager>();
        var listener = new LikedItemIdsUpdateListener(userData.Object, library.Object, _cache, NullLogger<LikedItemIdsUpdateListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        return (userData, library, listener);
    }

    private void Save(Mock<IUserDataManager> userData, BaseItem item, bool? likes, Guid? userId = null) =>
        userData.Raise(m => m.UserDataSaved += null, new UserDataSaveEventArgs
        {
            UserId = userId ?? _user,
            Item = item,
            SaveReason = UserDataSaveReason.UpdateUserRating,
            UserData = new UserItemData { Key = "k", Likes = likes }
        });
}
