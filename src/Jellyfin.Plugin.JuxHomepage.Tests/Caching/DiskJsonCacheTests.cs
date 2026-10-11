using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Caching;

public sealed class DiskJsonCacheTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-diskjsoncache-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private DiskJsonCache<TestItem> BuildCache() =>
        new(_tempDir, new FileSystem(), NullLogger.Instance);

    [Fact]
    public void Write_EmptyList_ReplacesContentAndIsNoLongerStale()
    {
        // F-69: an empty library answer is cached like any other.
        var cache = BuildCache();
        cache.Write("items.json", [new TestItem(1, "a")]);

        cache.Write("items.json", []);

        Assert.Empty(cache.Read("items.json"));
        Assert.False(cache.IsStale("items.json", TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Read_FileDoesNotExist_ReturnsEmpty()
    {
        var cache = BuildCache();

        var items = cache.Read("missing.json");

        Assert.Empty(items);
    }

    [Fact]
    public void WriteThenRead_RoundTripsItems()
    {
        var cache = BuildCache();
        var items = new[] { new TestItem(1, "a"), new TestItem(2, "b") };

        cache.WriteUnlessEmpty("items.json", items);
        var read = cache.Read("items.json");

        Assert.Equal(2, read.Count);
        Assert.Equal("a", read[0].Name);
        Assert.Equal("b", read[1].Name);
    }

    [Fact]
    public void WriteUnlessEmpty_EmptyList_DoesNotOverwriteExistingCache()
    {
        var cache = BuildCache();
        cache.WriteUnlessEmpty("items.json", [new TestItem(1, "a")]);

        cache.WriteUnlessEmpty("items.json", []);

        var read = cache.Read("items.json");
        Assert.Single(read);
        Assert.Equal("a", read[0].Name);
    }

    [Fact]
    public void WriteUnlessEmpty_LeavesNoStrayTempFile()
    {
        var cache = BuildCache();

        cache.WriteUnlessEmpty("items.json", [new TestItem(1, "a")]);

        Assert.False(File.Exists(Path.Combine(_tempDir, "items.json.tmp")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "items.json")));
    }

    [Fact]
    public void IsStale_FileDoesNotExist_ReturnsTrue()
    {
        var cache = BuildCache();

        Assert.True(cache.IsStale("missing.json", TimeSpan.FromHours(24)));
    }

    [Fact]
    public void IsStale_FreshlyWritten_ReturnsFalse()
    {
        var cache = BuildCache();
        cache.WriteUnlessEmpty("items.json", [new TestItem(1, "a")]);

        Assert.False(cache.IsStale("items.json", TimeSpan.FromHours(24)));
    }

    [Fact]
    public void IsStale_OlderThanMaxAge_ReturnsTrue()
    {
        var cache = BuildCache();
        cache.WriteUnlessEmpty("items.json", [new TestItem(1, "a")]);
        var path = Path.Combine(_tempDir, "items.json");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(2));

        Assert.True(cache.IsStale("items.json", TimeSpan.FromHours(1)));
    }

    [Fact]
    public void GetLastWriteUtc_FileDoesNotExist_ReturnsNull()
    {
        var cache = BuildCache();

        Assert.Null(cache.GetLastWriteUtc("missing.json"));
    }

    [Fact]
    public void GetLastWriteUtc_AfterWrite_ReturnsRecentTimestamp()
    {
        var cache = BuildCache();
        cache.WriteUnlessEmpty("items.json", [new TestItem(1, "a")]);

        var lastWrite = cache.GetLastWriteUtc("items.json");

        Assert.NotNull(lastWrite);
        Assert.True(DateTime.UtcNow - lastWrite.Value < TimeSpan.FromMinutes(1));
    }

    private sealed record TestItem(int Id, string Name);
}
