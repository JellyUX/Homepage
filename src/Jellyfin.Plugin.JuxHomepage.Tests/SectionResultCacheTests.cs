using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// PERF-03.b, PERF-06.a: one page per section and user, served within the layout TTL and never after
/// the user's data, the library, the configuration or the user's settings changed (DEC-P07-2).
/// </summary>
public sealed class SectionResultCacheTests : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly ManualTimeProvider _clock = new();
    private readonly SessionCache _sessionCache;
    private readonly SectionResultCache _cache;
    private readonly Guid _user = Guid.NewGuid();

    public SectionResultCacheTests()
    {
        _sessionCache = new SessionCache(_clock);
        _cache = new SectionResultCache(_sessionCache, _clock);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _sessionCache.Dispose();
    }

    [Fact]
    public void StoredPage_IsServedForTheSamePageOnly()
    {
        var id = Section(_user);
        var page = Page();
        Assert.True(_cache.Set(id, 0, 20, page, _cache.StampOf(_user), Ttl));

        Assert.True(_cache.TryGet(id, 0, 20, out var hit));
        Assert.Same(page, hit);
        Assert.False(_cache.TryGet(id, 20, 20, out _));
        Assert.False(_cache.TryGet(id, 0, 10, out _));
        Assert.False(_cache.TryGet(Section(Guid.NewGuid()), 0, 20, out _));
    }

    [Fact]
    public void AnotherPage_ReplacesTheStoredOne_OnePagePerSection()
    {
        var id = Section(_user);
        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _cache.Set(id, 20, 20, Page(), _cache.StampOf(_user), Ttl);

        Assert.Equal(1, _cache.CountForTesting());
        Assert.False(_cache.TryGet(id, 0, 20, out _));
        Assert.True(_cache.TryGet(id, 20, 20, out _));
    }

    [Fact]
    public void EmptyAdditionalDataAndSectionKey_AreTheSameAsNone()
    {
        _cache.Set(new SectionId(_user, "w", string.Empty, string.Empty), 0, 20, Page(), _cache.StampOf(_user), Ttl);

        Assert.True(_cache.TryGet(new SectionId(_user, "w", null, null), 0, 20, out _));
    }

    [Fact]
    public void StoredPage_IsNotServedOnceTheLayoutTtlEnds()
    {
        var id = Section(_user);
        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);

        _clock.UtcNowValue += Ttl - TimeSpan.FromSeconds(1);
        Assert.True(_cache.TryGet(id, 0, 20, out _));
        _clock.UtcNowValue += TimeSpan.FromSeconds(1);
        Assert.False(_cache.TryGet(id, 0, 20, out _));
    }

    [Fact]
    public void StoredPage_IsNotServedAfterAConfigurationChangeOrTheUsersSettingsChange()
    {
        var id = Section(_user);
        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _sessionCache.Clear();
        Assert.False(_cache.TryGet(id, 0, 20, out _));

        _cache.Set(id, 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _sessionCache.Invalidate(_user);
        Assert.False(_cache.TryGet(id, 0, 20, out _));
    }

    [Fact]
    public void Invalidate_DropsThatUsersPagesOnly_Clear_DropsEveryPage()
    {
        var other = Guid.NewGuid();
        _cache.Set(Section(_user), 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), Ttl);

        _cache.Invalidate(_user);
        Assert.False(_cache.TryGet(Section(_user), 0, 20, out _));
        Assert.True(_cache.TryGet(Section(other), 0, 20, out _));

        _cache.Clear();
        Assert.False(_cache.TryGet(Section(other), 0, 20, out _));
    }

    [Fact]
    public void Section_InvalidatedWhileRead_IsNotStored()
    {
        // A page computed before the user's data changed reflects the old state.
        var stamp = _cache.StampOf(_user);
        _cache.Invalidate(_user);

        Assert.False(_cache.Set(Section(_user), 0, 20, Page(), stamp, Ttl));

        stamp = _cache.StampOf(_user);
        _cache.Clear();
        Assert.False(_cache.Set(Section(_user), 0, 20, Page(), stamp, Ttl));

        stamp = _cache.StampOf(_user);
        _sessionCache.Clear();
        Assert.False(_cache.Set(Section(_user), 0, 20, Page(), stamp, Ttl));
        Assert.Equal(0, _cache.CountForTesting());
    }

    [Fact]
    public void Remove_DropsTheFailedPage_NotOneStoredSince()
    {
        var id = Section(_user);
        var failed = Page();
        _cache.Set(id, 0, 20, failed, _cache.StampOf(_user), Ttl);
        var stored = Page();
        _cache.Set(id, 0, 20, stored, _cache.StampOf(_user), Ttl);

        _cache.Remove(id, failed);
        Assert.True(_cache.TryGet(id, 0, 20, out var hit));
        Assert.Same(stored, hit);

        _cache.Remove(id, stored);
        Assert.False(_cache.TryGet(id, 0, 20, out _));
    }

    [Fact]
    public void Cleanup_RemovesExpiredAndOutdatedPages()
    {
        var other = Guid.NewGuid();
        _cache.Set(Section(_user), 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), TimeSpan.FromMinutes(1));
        _cache.Set(Section(Guid.NewGuid()), 0, 20, Page(), _cache.StampOf(Guid.Empty), Ttl);
        _sessionCache.Invalidate(_user);
        _clock.UtcNowValue += TimeSpan.FromMinutes(2);

        _cache.RunCleanupForTesting();

        Assert.Equal(1, _cache.CountForTesting());
    }

    [Fact]
    public async Task Listener_UserDataSave_DropsThatUsersPages_LibraryChange_DropsEveryPage()
    {
        var userData = new Mock<IUserDataManager>();
        var library = new Mock<ILibraryManager>();
        var listener = new SectionCacheInvalidationListener(userData.Object, library.Object, _cache, NullLogger<SectionCacheInvalidationListener>.Instance);
        var other = Guid.NewGuid();
        await listener.StartAsync(CancellationToken.None);

        _cache.Set(Section(_user), 0, 20, Page(), _cache.StampOf(_user), Ttl);
        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), Ttl);
        userData.Raise(m => m.UserDataSaved += null, new UserDataSaveEventArgs { UserId = _user, SaveReason = UserDataSaveReason.PlaybackProgress, Item = new Movie() });
        Assert.False(_cache.TryGet(Section(_user), 0, 20, out _));
        Assert.True(_cache.TryGet(Section(other), 0, 20, out _));

        library.Raise(m => m.ItemAdded += null, library.Object, new ItemChangeEventArgs { Item = new Movie() });
        Assert.False(_cache.TryGet(Section(other), 0, 20, out _));

        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), Ttl);
        library.Raise(m => m.ItemRemoved += null, library.Object, new ItemChangeEventArgs { Item = new Movie() });
        Assert.False(_cache.TryGet(Section(other), 0, 20, out _));

        await listener.StopAsync(CancellationToken.None);
        _cache.Set(Section(other), 0, 20, Page(), _cache.StampOf(other), Ttl);
        library.Raise(m => m.ItemAdded += null, library.Object, new ItemChangeEventArgs { Item = new Movie() });
        Assert.True(_cache.TryGet(Section(other), 0, 20, out _));
    }

    private static SectionId Section(Guid user) => new(user, "jux.test.section", "value", "jux.test.section#1");

    private static WidgetResult Page() => new([new BaseItemDto()], 1);
}
