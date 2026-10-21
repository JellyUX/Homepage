using Jellyfin.Plugin.JuxHomepage.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Caching;

public sealed class PerUserComputedCacheTests : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-peruser-tests-" + Guid.NewGuid());
    private readonly FailingFileSystem _fileSystem = new();
    private readonly ManualTimeProvider _clock = new() { UtcNowValue = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero) };
    private readonly Guid _userId = Guid.NewGuid();
    private int _computes;

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private PerUserComputedCache<string> BuildCache() => new(
        new DiskJsonCache<string>(_tempDir, _fileSystem, NullLogger.Instance, _clock),
        Ttl,
        _ =>
        {
            _computes++;
            return ["watched-" + _computes];
        },
        NullLogger.Instance,
        _clock);

    [Fact]
    public void Get_WhenTheDiskRefusesTheWrite_ServesTheComputedListAndComputesOncePerTtl()
    {
        _fileSystem.FailWrites = true;
        var cache = BuildCache();

        Assert.Equal(["watched-1"], cache.Get(_userId));
        Assert.Equal(["watched-1"], cache.Get(_userId));
        Assert.Equal(1, _computes);

        _clock.UtcNowValue += Ttl;
        Assert.Equal(["watched-2"], cache.Get(_userId));
        Assert.Equal(2, _computes);
    }

    [Fact]
    public void Get_OfAFreshFileAlreadyRead_ChecksTheFileOnce()
    {
        // Annexe.a: the tabs read their user's file on each request; one existence and date check
        // decides missing or stale and serves the memo (6 checks before).
        var cache = BuildCache();
        cache.Get(_userId);
        _fileSystem.Reads = 0;

        Assert.Equal(["watched-1"], cache.Get(_userId));
        Assert.Equal(2, _fileSystem.Reads);
        Assert.Equal(1, _computes);
    }

    [Fact]
    public void Get_AfterTheDiskAcceptsWritesAgain_ReadsTheFileItWrote()
    {
        _fileSystem.FailWrites = true;
        var cache = BuildCache();
        cache.Get(_userId);

        _fileSystem.FailWrites = false;
        _clock.UtcNowValue += Ttl;

        Assert.Equal(["watched-2"], cache.Get(_userId));
        Assert.Equal(["watched-2"], cache.Get(_userId));
        Assert.Equal(2, _computes);
    }

    [Fact]
    public async Task Refresh_RacingAnUpdate_DoesNotOverwriteTheWatchTheUpdateAdded()
    {
        var computeStarted = new TaskCompletionSource();
        var releaseCompute = new TaskCompletionSource();
        var calls = 0;
        var cache = new PerUserComputedCache<string>(
            new DiskJsonCache<string>(_tempDir, _fileSystem, NullLogger.Instance, _clock),
            Ttl,
            _ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return ["watched-1"];
                }

                computeStarted.SetResult();
                releaseCompute.Task.Wait();
                return ["recomputed-before-the-watch"];
            },
            NullLogger.Instance,
            _clock);
        cache.Get(_userId);

        var refresh = Task.Run(() => cache.Refresh(_userId));
        await computeStarted.Task;
        cache.Update(_userId, current => [.. current, "just-watched"]);
        releaseCompute.SetResult();
        await refresh;

        Assert.Contains("just-watched", cache.Get(_userId));
    }

    [Fact]
    public void Update_WhenTheDiskRefusesTheWrite_KeepsThePatchedListInMemory()
    {
        _fileSystem.FailWrites = true;
        var cache = BuildCache();
        cache.Get(_userId);

        cache.Update(_userId, current => [.. current, "just-watched"]);

        Assert.Equal(["watched-1", "just-watched"], cache.Get(_userId));
        Assert.Equal(1, _computes);
    }
}
