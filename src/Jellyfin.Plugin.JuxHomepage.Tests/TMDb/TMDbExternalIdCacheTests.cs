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
}
