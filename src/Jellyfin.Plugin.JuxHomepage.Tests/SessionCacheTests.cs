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
        var hit = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var result);

        Assert.True(hit);
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void TryGet_ExpiredTtl_ReturnsMiss()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);

        // TimeSpan.Zero means any entry - no matter how fresh - is considered expired.
        var hit = _cache.TryGet(userId, "en", TimeSpan.Zero, out var result);

        Assert.False(hit);
        Assert.Null(result);
    }

    [Fact]
    public void TryGet_NoEntry_ReturnsMiss()
    {
        var hit = _cache.TryGet(Guid.NewGuid(), "en", TimeSpan.FromMinutes(15), out var result);

        Assert.False(hit);
        Assert.Null(result);
    }

    [Fact]
    public void Invalidate_RemovesEntry_SubsequentTryGetReturnsMiss()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);

        _cache.Invalidate(userId);
        var hit = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out _);

        Assert.False(hit);
    }

    [Fact]
    public void Set_OverwritesExistingEntry()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "old" }]);
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "new" }]);

        _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var result);

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

        var hitEn = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var resultEn);
        var hitFr = _cache.TryGet(userId, "fr", TimeSpan.FromMinutes(15), out var resultFr);

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

        var hit = _cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out var result);

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

        Assert.False(_cache.TryGet(userId, "en", TimeSpan.FromMinutes(15), out _));
        Assert.False(_cache.TryGet(userId, "fr", TimeSpan.FromMinutes(15), out _));
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
        var hit = _cache.TryGet(userId, "en", TimeSpan.FromHours(2), out _);
        Assert.False(hit);
    }

    [Fact]
    public void RunCleanupForTesting_EntryWithinGcThreshold_IsNotEvicted()
    {
        var userId = Guid.NewGuid();
        _cache.Set(userId, "en", [new WidgetDescriptor { WidgetType = "w1" }]);
        _cache.SetLastAccessedForTesting(userId, "en", DateTime.UtcNow.AddMinutes(-30));

        _cache.RunCleanupForTesting();

        var hit = _cache.TryGet(userId, "en", TimeSpan.FromHours(2), out var result);
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

        var hits = new List<bool>();
        for (var minute = 1; minute <= 20; minute++)
        {
            clock.UtcNowValue += TimeSpan.FromMinutes(1);
            hits.Add(cache.TryGet(userId, "en", ttl, out _));
        }

        Assert.All(hits.Take(14), Assert.True);
        Assert.All(hits.Skip(14), Assert.False);
    }

    public void Dispose() => _cache.Dispose();
}
