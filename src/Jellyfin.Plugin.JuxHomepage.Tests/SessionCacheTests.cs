using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class SessionCacheTests : IDisposable
{
    private readonly SessionCache _cache = new();

    [Fact]
    public void Set_ThenTryGet_WithinTtl_ReturnsHit()
    {
        var userId = Guid.NewGuid();
        var descriptors = new List<WidgetDescriptor> { new() { WidgetType = "w1" } };

        _cache.Set(userId, "en", descriptors);
        var hit = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var result, out _);

        Assert.True(hit);
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void TryGet_ExpiredTtl_ReturnsTheEntryMarkedExpired()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);

        // TimeSpan.Zero means any entry - no matter how fresh - is considered expired.
        var hit = _cache.TryGet(userId, "en", TimeSpan.Zero, out var result, out var expired);

        Assert.True(hit);
        Assert.True(expired);
        Assert.Equal("w1", Assert.Single(result!).WidgetType);
    }

    [Fact]
    public void Set_AfterClear_WithVersionReadBefore_IsDropped()
    {
        var userId = Guid.NewGuid();
        var version = _cache.VersionOf(userId);

        _cache.Clear();
        var stored = _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "built-before-clear" }], version);

        Assert.False(stored);
        Assert.False(_cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out _, out _));
    }

    [Fact]
    public void Set_AfterInvalidate_WithVersionReadBefore_IsDropped()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var version = _cache.VersionOf(userId);
        var otherVersion = _cache.VersionOf(other);

        _cache.Invalidate(userId);

        Assert.False(_cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }], version));
        Assert.True(_cache.Set(other, "en", [new WidgetDescriptor { WidgetType = "w1" }], otherVersion));
    }

    [Fact]
    public void Set_WithCurrentVersion_IsStored()
    {
        var userId = Guid.NewGuid();
        _cache.Clear();
        _cache.Invalidate(userId);

        var stored = _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }], _cache.VersionOf(userId));

        Assert.True(stored);
        Assert.True(_cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out _, out var expired));
        Assert.False(expired);
    }

    [Fact]
    public void TryGet_NoEntry_ReturnsMiss()
    {
        var hit = _cache.TryGet(Guid.NewGuid(), "en", TimeSpan.FromMinutes(15), out var result, out _);

        Assert.False(hit);
        Assert.Null(result);
    }

    [Fact]
    public void Invalidate_RemovesEntry_SubsequentTryGetReturnsMiss()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);

        _cache.Invalidate(userId);
        var hit = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Set_OverwritesExistingEntry()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "old" }]);
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "new" }]);

        _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var result, out _);

        Assert.NotNull(result);
        Assert.Equal("new", result![0].WidgetType);
    }

    // -------------------------------------------------------------------------
    // Per-language isolation (regression: each language must get its own cached
    // layout, since translated DisplayName values differ per language).
    // -------------------------------------------------------------------------

    [Fact]
    public void Set_DifferentLanguage_IsolatedFromOtherLanguageEntries()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "fr", [new WidgetDescriptor { WidgetType = "w1", DisplayName = "Bonjour" }]);

        var hitEn = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var resultEn, out _);
        var hitFr = _cache.TryGet(userId, "fr", TimeSpan.FromMinutes(15), out var resultFr, out _);

        Assert.False(hitEn);
        Assert.Null(resultEn);
        Assert.True(hitFr);
        Assert.Equal("Bonjour", resultFr![0].DisplayName);
    }

    [Fact]
    public void Set_NullLanguage_TreatedSameAsEnglish()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, null, [new WidgetDescriptor { WidgetType = "w1" }]);

        var hit = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var result, out _);

        Assert.True(hit);
        Assert.NotNull(result);
    }

    [Fact]
    public void Invalidate_RemovesEntriesForEveryLanguage()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);
        _cache.Set(userId, "fr", [new WidgetDescriptor { WidgetType = "w1" }]);

        _cache.Invalidate(userId);

        Assert.False(_cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out _, out _));
        Assert.False(_cache.TryGet(userId, "fr", TimeSpan.FromMinutes(15), out _, out _));
    }

    // -------------------------------------------------------------------------
    // Background garbage collection (Cleanup(), normally driven by the internal Timer every
    // CleanupIntervalMinutes -- these tests drive it synchronously via the internal test seams so
    // the 60-minute eviction threshold doesn't have to actually elapse).
    // -------------------------------------------------------------------------

    [Fact]
    public void RunCleanupForTesting_EntryOlderThanGcThreshold_IsEvicted()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);
        _cache.SetLastAccessedForTesting(userId, "en", DateTime.UtcNow.AddMinutes(-61));

        _cache.RunCleanupForTesting();

        // A large TTL here proves the entry was physically removed by Cleanup(), not merely
        // considered stale by TryGet's own (much shorter) TTL check.
        var hit = _cache.TryGet(userId, "en", TimeSpan.FromHours(2), out _, out _);
        Assert.False(hit);
    }

    [Fact]
    public void RunCleanupForTesting_EntryWithinGcThreshold_IsNotEvicted()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);
        _cache.SetLastAccessedForTesting(userId, "en", DateTime.UtcNow.AddMinutes(-30));

        _cache.RunCleanupForTesting();

        var hit = _cache.TryGet(userId, "en", TimeSpan.FromHours(2), out var result, out _);
        Assert.True(hit);
        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // Absolute TTL (F-02)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGet_ReadEveryMinute_DoesNotExtendPastTtl()
    {
        var clock = new ManualTimeProvider();
        using var cache = new SessionCache(clock);
        var userId = Guid.NewGuid();
        var ttl = TimeSpan.FromMinutes(15);
        cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);

        var fresh = new List<bool>();
        for (var minute = 1; minute <= 20; minute++)
        {
            clock.UtcNowValue += TimeSpan.FromMinutes(1);
            Assert.True(cache.TryGet(userId, "en", ttl, out _, out var expired));
            fresh.Add(!expired);
        }

        Assert.All(fresh.Take(14), Assert.True);
        Assert.All(fresh.Skip(14), Assert.False);
    }

    public void Dispose() => _cache.Dispose();
}
