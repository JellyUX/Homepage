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

    /// <summary>Real file system that counts file reads.</summary>
    private sealed class CountingFileSystem : IFileSystem
    {
        private readonly FileSystem _inner = new();

        public int ReadAllTextCalls { get; private set; }

        public bool FileExists(string path) => _inner.FileExists(path);

        public string ReadAllText(string path)
        {
            ReadAllTextCalls++;
            return _inner.ReadAllText(path);
        }

        public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);

        public void Move(string sourceFileName, string destFileName, bool overwrite) => _inner.Move(sourceFileName, destFileName, overwrite);

        public void Delete(string path) => _inner.Delete(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) => _inner.GetFiles(path, searchPattern, searchOption);

        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }

    private DiskJsonCache<TestItem> BuildCache() =>
        new(_tempDir, new FileSystem(), NullLogger.Instance);

    [Fact]
    public void Write_WhenTheDiskRefuses_ReturnsFalseAndLeavesNoTempFile()
    {
        var fileSystem = new FailingFileSystem();
        var cache = new DiskJsonCache<TestItem>(_tempDir, fileSystem, NullLogger.Instance);
        Assert.True(cache.Write("items.json", [new TestItem(1, "a")]));

        fileSystem.FailWrites = true;

        Assert.False(cache.Write("items.json", [new TestItem(2, "b")]));
        Assert.Equal([new TestItem(1, "a")], cache.Read("items.json"));
        Assert.False(File.Exists(Path.Combine(_tempDir, "items.json.tmp")));
    }

    [Fact]
    public void Constructor_WhenTheDirectoryCannotBeCreated_BuildsADegradedCacheThatRefusesWrites()
    {
        var fileSystem = new FailingFileSystem { FailCreateDirectory = true };

        var cache = new DiskJsonCache<TestItem>(_tempDir, fileSystem, NullLogger.Instance);

        Assert.Empty(cache.Read("items.json"));
        Assert.False(cache.Write("items.json", [new TestItem(1, "a")]));
    }

    [Fact]
    public void Write_CreatesTheDirectoryOnceItCanBeCreated_AfterAFailedStart()
    {
        // H-07 (R11): a data path that becomes writable after startup (a late NAS mount) works again
        // without a restart.
        var fileSystem = new FailingFileSystem { FailCreateDirectory = true };
        var cache = new DiskJsonCache<TestItem>(_tempDir, fileSystem, NullLogger.Instance);

        fileSystem.FailCreateDirectory = false;

        Assert.True(cache.Write("items.json", [new TestItem(1, "a")]));
        Assert.Equal([new TestItem(1, "a")], cache.Read("items.json"));
    }

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
    public void Read_Twice_ReadsTheFileOnce()
    {
        // F-04: the second read is served from memory.
        var fileSystem = new CountingFileSystem();
        var writer = new DiskJsonCache<TestItem>(_tempDir, new FileSystem(), NullLogger.Instance);
        writer.Write("items.json", [new TestItem(1, "a")]);
        var cache = new DiskJsonCache<TestItem>(_tempDir, fileSystem, NullLogger.Instance);

        cache.Read("items.json");
        var second = cache.Read("items.json");

        Assert.Equal("a", Assert.Single(second).Name);
        Assert.Equal(1, fileSystem.ReadAllTextCalls);
    }

    [Fact]
    public void Read_FileRewrittenByAnotherWriter_ReturnsTheNewContent()
    {
        var cache = BuildCache();
        cache.Write("items.json", [new TestItem(1, "old")]);
        cache.Read("items.json");

        BuildCache().Write("items.json", [new TestItem(2, "new")]);
        File.SetLastWriteTimeUtc(Path.Combine(_tempDir, "items.json"), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal("new", Assert.Single(cache.Read("items.json")).Name);
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
