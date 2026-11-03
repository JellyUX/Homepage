using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class VersionedUserCacheTests : IDisposable
{
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly VersionedUserCache<(Guid UserId, string Name), string> _cache = new(
        key => key.UserId,
        (_, entry, _) => entry == "garbage",
        TimeSpan.FromHours(1));

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void Set_WithTheVersionReadBeforeAnInvalidation_IsRefused()
    {
        var version = _cache.VersionOf(_alice);
        _cache.Invalidate(_alice);

        var stored = _cache.Set((_alice, "a"), "value", () => version == _cache.VersionOf(_alice));

        Assert.False(stored);
        Assert.False(_cache.TryGet((_alice, "a"), out _));
    }

    [Fact]
    public void Invalidate_DropsOnlyThatUsersEntries()
    {
        _cache.Set((_alice, "a"), "alice", () => true);
        _cache.Set((_bob, "a"), "bob", () => true);
        var bobVersion = _cache.VersionOf(_bob);

        _cache.Invalidate(_alice);

        Assert.False(_cache.TryGet((_alice, "a"), out _));
        Assert.True(_cache.TryGet((_bob, "a"), out var bob));
        Assert.Equal("bob", bob);
        Assert.Equal(bobVersion, _cache.VersionOf(_bob));
    }

    [Fact]
    public void Clear_DropsEveryEntryAndBumpsEveryUsersVersion()
    {
        _cache.Set((_alice, "a"), "alice", () => true);
        var bobVersion = _cache.VersionOf(_bob);

        _cache.Clear();

        Assert.Equal(0, _cache.Count);
        Assert.NotEqual(bobVersion, _cache.VersionOf(_bob));
    }

    [Fact]
    public void Remove_KeepsAnEntryStoredSince()
    {
        _cache.Set((_alice, "a"), "first", () => true);
        _cache.Set((_alice, "a"), "second", () => true);

        _cache.Remove((_alice, "a"), "first");

        Assert.True(_cache.TryGet((_alice, "a"), out var kept));
        Assert.Equal("second", kept);
    }

    [Fact]
    public void Cleanup_DropsTheEntriesItsRuleNames()
    {
        _cache.Set((_alice, "a"), "garbage", () => true);
        _cache.Set((_alice, "b"), "kept", () => true);

        _cache.Cleanup();

        Assert.False(_cache.TryGet((_alice, "a"), out _));
        Assert.True(_cache.TryGet((_alice, "b"), out _));
    }
}
