using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Configuration;

public sealed class UserConfigurationStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UserConfigurationStore _store;

    public UserConfigurationStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "jux-user-config-tests-" + Guid.NewGuid());
        _store = BuildStore(new FileSystem());
    }

    [Fact]
    public void SaveUserConfiguration_WhenTheDiskRefuses_ReturnsFalse()
    {
        var fileSystem = new FailingFileSystem();
        var store = BuildStore(fileSystem);
        var userId = Guid.NewGuid();
        Assert.True(store.SaveUserConfiguration(new UserConfiguration { UserId = userId, Enabled = false }));

        fileSystem.FailWrites = true;

        Assert.False(store.SaveUserConfiguration(new UserConfiguration { UserId = userId, Enabled = true }));
        Assert.False(store.GetUserConfiguration(userId)!.Enabled);
    }

    [Fact]
    public void Constructor_WhenTheUsersDirectoryCannotBeCreated_StillBuildsAndRefusesSaves()
    {
        Directory.Delete(_tempDir, recursive: true);
        var store = BuildStore(new FailingFileSystem { FailCreateDirectory = true });

        Assert.Null(store.GetUserConfiguration(Guid.NewGuid()));
        Assert.False(store.SaveUserConfiguration(new UserConfiguration { UserId = Guid.NewGuid() }));
    }

    [Fact]
    public void SaveUserConfiguration_CreatesTheDirectoryOnceItCanBeCreated_AfterAFailedStart()
    {
        Directory.Delete(_tempDir, recursive: true);
        var fileSystem = new FailingFileSystem { FailCreateDirectory = true };
        var store = BuildStore(fileSystem);
        var userId = Guid.NewGuid();

        fileSystem.FailCreateDirectory = false;

        Assert.True(store.SaveUserConfiguration(new UserConfiguration { UserId = userId, Enabled = false }));
        Assert.False(store.GetUserConfiguration(userId)!.Enabled);
    }

    [Fact]
    public void GetUserConfiguration_NeverSaved_ReturnsNull()
    {
        var result = _store.GetUserConfiguration(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void SaveThenGet_RoundTripsEquivalentData()
    {
        var userId = Guid.NewGuid();
        var config = new UserConfiguration
        {
            UserId = userId,
            Enabled = false,
            WidgetOverrides = [new Widgets.WidgetConfig { WidgetType = "jux.admin.genre", MinItems = 2 }]
        };

        _store.SaveUserConfiguration(config);
        var result = _store.GetUserConfiguration(userId);

        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(false, result.Enabled);
        Assert.Single(result.WidgetOverrides);
        Assert.Equal("jux.admin.genre", result.WidgetOverrides[0].WidgetType);
        Assert.Equal(2, result.WidgetOverrides[0].MinItems);
    }

    [Fact]
    public void GetUserConfiguration_CorruptFile_ReturnsNullAndTheNextSaveReplacesIt()
    {
        // Failure path "corrupt file on disk": a truncated file reads as "no overrides" instead of
        // throwing into the home request, and saving again recovers the user.
        var userId = Guid.NewGuid();
        var usersDir = Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage", "users");
        File.WriteAllText(Path.Combine(usersDir, $"{userId}.json"), "{\"UserId\": \"" + userId + "\", \"Enabled\": fal");

        Assert.Null(_store.GetUserConfiguration(userId));

        _store.SaveUserConfiguration(new UserConfiguration { UserId = userId, Enabled = false });
        Assert.Equal(false, _store.GetUserConfiguration(userId)?.Enabled);
    }

    [Fact]
    public void SaveUserConfiguration_LeavesNoStrayTempFileBehind()
    {
        var userId = Guid.NewGuid();
        _store.SaveUserConfiguration(new UserConfiguration { UserId = userId });

        var usersDir = Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage", "users");
        var tmpFiles = Directory.GetFiles(usersDir, "*.tmp");

        Assert.Empty(tmpFiles);
    }

    [Fact]
    public void GetUserConfiguration_ReadAgain_ReadsTheDiskOnce_AndASaveIsSeenAtOnce()
    {
        // Annexe.a (DEC-P07-8): every request that checks whether JellyUX runs for the user reads
        // the settings; the file is read once, and the store's own saves replace what it kept.
        var fileSystem = new CountingFileSystem();
        var store = BuildStore(fileSystem);
        var saved = Guid.NewGuid();
        var none = Guid.NewGuid();
        Assert.True(store.SaveUserConfiguration(new UserConfiguration { UserId = saved, Enabled = false }));
        File.Delete(Path.Combine(_tempDir, "Jellyfin.Plugin.JuxHomepage", "users", $"{saved}.json"));
        fileSystem.Reads = 0;

        Assert.False(store.GetUserConfiguration(saved)!.Enabled);
        Assert.Null(store.GetUserConfiguration(none));
        Assert.Null(store.GetUserConfiguration(none));
        Assert.Equal(1, fileSystem.Reads);

        Assert.True(store.SaveUserConfiguration(new UserConfiguration { UserId = none, Enabled = false }));
        Assert.False(store.GetUserConfiguration(none)!.Enabled);
        Assert.Equal(1, fileSystem.Reads);
    }

    [Fact]
    public void GetUserConfiguration_ReturnsANewInstanceEachTime()
    {
        var userId = Guid.NewGuid();
        _store.SaveUserConfiguration(new UserConfiguration { UserId = userId, Enabled = false });

        var first = _store.GetUserConfiguration(userId)!;
        first.Enabled = true;

        Assert.False(_store.GetUserConfiguration(userId)!.Enabled);
    }

    // Proves that the ReaderWriterLockSlim correctly serializes a concurrent reader against a
    // writer: the reader started while the writer is mid-save must wait for the writer to finish
    // (never interleave), and must then see the newly-written value. A reader observing a
    // half-written file is not the right thing to test here -- the lock already makes that
    // structurally impossible to hit via the public API (GetUserConfiguration blocks on
    // EnterReadLock for as long as the writer holds the write lock). BlockingFileSystem pauses the
    // writer right before the atomic rename so the test can deterministically control the
    // interleaving instead of relying on real-world race timing.
    [Fact]
    public async Task SaveUserConfiguration_ConcurrentWithGetUserConfiguration_ReaderWaitsForWriterThenSeesNewValue()
    {
        var userId = Guid.NewGuid();
        var blockingFileSystem = new BlockingFileSystem();
        var store = BuildStore(blockingFileSystem);

        // Seed an initial value so the "new value" assertion below is unambiguous. Release the
        // pause for this seed write only -- it must complete normally, not block.
        blockingFileSystem.ReleaseWrite.Set();
        store.SaveUserConfiguration(new UserConfiguration { UserId = userId, Enabled = false });
        blockingFileSystem.WriteStarted.Reset();
        blockingFileSystem.ReleaseWrite.Reset();

        var writeTask = Task.Run(() =>
            store.SaveUserConfiguration(new UserConfiguration { UserId = userId, Enabled = true }));

        // Wait until the writer has entered the write lock and is paused right before the rename,
        // so the interleaving below is deterministic rather than a guessed delay.
        var writerReachedPause = blockingFileSystem.WriteStarted.Wait(TimeSpan.FromSeconds(5));
        Assert.True(writerReachedPause, "Writer did not reach the pause point in time.");

        var readTask = Task.Run(() => store.GetUserConfiguration(userId));

        // The reader must still be blocked on EnterReadLock while the writer holds the write lock.
        var readCompletedTooEarly = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromMilliseconds(200))) == readTask;
        Assert.False(readCompletedTooEarly, "Reader should block until the writer releases the lock.");

        blockingFileSystem.ReleaseWrite.Set();
        await writeTask;
        var result = await readTask;

        Assert.NotNull(result);
        Assert.True(result.Enabled);

        store.Dispose();
    }

    public void Dispose()
    {
        _store.Dispose();

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; leftover temp dirs under %TEMP% are harmless.
        }
    }

    private UserConfigurationStore BuildStore(IFileSystem fileSystem)
    {
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.DataPath).Returns(_tempDir);

        return new UserConfigurationStore(
            applicationPathsMock.Object,
            fileSystem,
            NullLogger<UserConfigurationStore>.Instance);
    }

    /// <summary>Wraps a real <see cref="FileSystem"/>, counting the existence checks and reads of files.</summary>
    private sealed class CountingFileSystem : IFileSystem
    {
        private readonly FileSystem _inner = new();

        /// <summary>Gets or sets the number of FileExists and ReadAllText calls.</summary>
        public int Reads { get; set; }

        public bool FileExists(string path)
        {
            Reads++;
            return _inner.FileExists(path);
        }

        public string ReadAllText(string path)
        {
            Reads++;
            return _inner.ReadAllText(path);
        }

        public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);

        public void Move(string sourceFileName, string destFileName, bool overwrite) => _inner.Move(sourceFileName, destFileName, overwrite);

        public void Delete(string path) => _inner.Delete(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
            _inner.GetFiles(path, searchPattern, searchOption);

        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }

    /// <summary>
    /// Wraps a real <see cref="FileSystem"/>, pausing right before <see cref="Move"/> (the atomic
    /// rename) until the test explicitly releases it -- lets a test deterministically control when
    /// a writer is "mid-save" without relying on real-world race timing.
    /// </summary>
    private sealed class BlockingFileSystem : IFileSystem
    {
        private readonly FileSystem _inner = new();

        /// <summary>Signaled by <see cref="Move"/> right before it pauses.</summary>
        public ManualResetEventSlim WriteStarted { get; } = new(initialState: false);

        /// <summary>The test sets this to let a paused <see cref="Move"/> proceed.</summary>
        public ManualResetEventSlim ReleaseWrite { get; } = new(initialState: false);

        public bool FileExists(string path) => _inner.FileExists(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);

        public void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            WriteStarted.Set();
            ReleaseWrite.Wait(TimeSpan.FromSeconds(5));
            _inner.Move(sourceFileName, destFileName, overwrite);
        }

        public void Delete(string path) => _inner.Delete(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
            _inner.GetFiles(path, searchPattern, searchOption);

        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }
}
