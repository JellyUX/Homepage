using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.TMDb;

public sealed class TMDbExternalIdCacheTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-external-ids-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetImdbIdAsync_KnownIdAfterRestart_IsNotFetchedAgain()
    {
        // F-07: a saved mapping survives a restart, so the next refresh makes no HTTP call for it.
        var calls = 0;
        Task<string?> Fetch(int id, CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<string?>(id == 27205 ? "tt1375666" : null);
        }

        using (var first = new TMDbExternalIdCache(_tempDir, "ids.json", new FileSystem(), Fetch, NullLogger.Instance))
        {
            Assert.Equal("tt1375666", await first.GetImdbIdAsync(27205, CancellationToken.None));
            Assert.Null(await first.GetImdbIdAsync(1, CancellationToken.None));
            first.Save();
        }

        using var restarted = new TMDbExternalIdCache(_tempDir, "ids.json", new FileSystem(), Fetch, NullLogger.Instance);
        Assert.Equal("tt1375666", await restarted.GetImdbIdAsync(27205, CancellationToken.None));
        Assert.Null(await restarted.GetImdbIdAsync(1, CancellationToken.None));

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task GetImdbIdAsync_TitleWithoutImdbId_IsNotAskedAgainForSevenDays()
    {
        // Annexe A "TMDb": TMDb answers "" for a title it has no IMDb id for. It was asked again at
        // every refresh; it is now remembered for 7 days (DEC-P06-4).
        var clock = new ManualTimeProvider();
        var calls = 0;
        using var cache = CacheAnswering(_ => string.Empty, () => calls++, clock);

        Assert.Null(await cache.GetImdbIdAsync(1, CancellationToken.None));
        clock.Advance(TMDbExternalIdCache.MissingIdLifetime - TimeSpan.FromSeconds(1));
        Assert.Null(await cache.GetImdbIdAsync(1, CancellationToken.None));
        Assert.Equal(1, calls);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await cache.GetImdbIdAsync(1, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetImdbIdAsync_FailedFetch_IsAskedAgain()
    {
        var calls = 0;
        using var cache = CacheAnswering(_ => null, () => calls++, new ManualTimeProvider());

        Assert.Null(await cache.GetImdbIdAsync(1, CancellationToken.None));
        Assert.Null(await cache.GetImdbIdAsync(1, CancellationToken.None));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ClearMissing_TitleWithoutImdbId_IsAskedAgain()
    {
        var calls = 0;
        using var cache = CacheAnswering(_ => string.Empty, () => calls++, new ManualTimeProvider());

        await cache.GetImdbIdAsync(1, CancellationToken.None);
        cache.ClearMissing();
        await cache.GetImdbIdAsync(1, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetImdbIdAsync_MoreTitlesWithoutImdbIdThanTheBound_RemembersOnlyTheBound()
    {
        var calls = 0;
        using var cache = CacheAnswering(_ => string.Empty, () => calls++, new ManualTimeProvider());
        for (var id = 1; id <= TMDbExternalIdCache.MaxMissingIds + 1; id++)
        {
            await cache.GetImdbIdAsync(id, CancellationToken.None);
        }

        await cache.GetImdbIdAsync(1, CancellationToken.None);
        await cache.GetImdbIdAsync(TMDbExternalIdCache.MaxMissingIds + 1, CancellationToken.None);

        Assert.Equal(TMDbExternalIdCache.MaxMissingIds + 2, calls);
    }

    private TMDbExternalIdCache CacheAnswering(Func<int, string?> answer, Action onCall, TimeProvider clock) =>
        new(
            _tempDir,
            "ids.json",
            new FileSystem(),
            (id, _) =>
            {
                onCall();
                return Task.FromResult(answer(id));
            },
            NullLogger.Instance,
            clock);
}
