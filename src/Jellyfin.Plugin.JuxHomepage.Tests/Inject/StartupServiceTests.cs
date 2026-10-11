using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Inject;

// Regression test for the Phase 10.2 fix (TODO_V2.md): StartAsync must never wait for the
// best-effort TMDb refresh before returning, so FileTransformation registration is never delayed
// behind a slow or unreachable TMDb. If this behavior regresses (e.g. someone re-adds an await on
// the refresh), this test times out waiting on the never-completing refresh task instead of passing.
public sealed class StartupServiceTests
{
    [Fact]
    public async Task StartAsync_TMDbRefreshNeverCompletes_StillReturnsPromptly()
    {
        var neverCompletes = new TaskCompletionSource().Task;

        var tmdbCacheServiceMock = new Mock<ITMDbCacheService>();
        tmdbCacheServiceMock.Setup(s => s.IsStale(It.IsAny<TMDbCacheType>())).Returns(true);
        tmdbCacheServiceMock
            .Setup(s => s.RefreshTrendingMoviesAsync(It.IsAny<CancellationToken>()))
            .Returns(neverCompletes);
        tmdbCacheServiceMock
            .Setup(s => s.RefreshTrendingShowsAsync(It.IsAny<CancellationToken>()))
            .Returns(neverCompletes);
        tmdbCacheServiceMock
            .Setup(s => s.RefreshAiringTodayAsync(It.IsAny<CancellationToken>()))
            .Returns(neverCompletes);

        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.WebPath).Returns("C:\\nonexistent-jellyux-test-path");

        var fileSystemMock = new Mock<IFileSystem>();
        fileSystemMock.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(false);

        // IsAvailable() resolves to false in the test process (no FileTransformation assembly
        // loaded), exercising the warning branch. Plugin.Instance is null in tests, so that branch's
        // Plugin.Instance.SaveConfiguration() call is safely skipped -- the same pattern already
        // relied upon by FileTransformationDetectorTests.
        var detector = new FileTransformationDetector(NullLogger<FileTransformationDetector>.Instance);

        var service = new StartupService(
            applicationPathsMock.Object,
            NullLogger<StartupService>.Instance,
            detector,
            tmdbCacheServiceMock.Object,
            fileSystemMock.Object,
            new FakeLifetime());

        var startTask = service.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(startTask, completed);
        Assert.True(startTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StartAsync_DefersTMDbRefreshUntilApplicationStarted_AndStopCancelsIt()
    {
        // F-07: no TMDb work during the boot; the refresh gets a token that the shutdown cancels.
        CancellationToken refreshToken = default;
        var tmdbCacheServiceMock = new Mock<ITMDbCacheService>();
        tmdbCacheServiceMock.Setup(s => s.IsStale(It.IsAny<TMDbCacheType>())).Returns(true);
        tmdbCacheServiceMock
            .Setup(s => s.RefreshTrendingMoviesAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(t => refreshToken = t)
            .Returns(Task.CompletedTask);
        tmdbCacheServiceMock.Setup(s => s.RefreshTrendingShowsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tmdbCacheServiceMock.Setup(s => s.RefreshAiringTodayAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var applicationPathsMock = new Mock<IApplicationPaths>();
        applicationPathsMock.Setup(p => p.WebPath).Returns("C:\nonexistent-jellyux-test-path");
        var lifetime = new FakeLifetime();
        using var service = new StartupService(
            applicationPathsMock.Object,
            NullLogger<StartupService>.Instance,
            new FileTransformationDetector(NullLogger<FileTransformationDetector>.Instance),
            tmdbCacheServiceMock.Object,
            new Mock<IFileSystem>().Object,
            lifetime);

        await service.StartAsync(CancellationToken.None);
        tmdbCacheServiceMock.Verify(s => s.RefreshTrendingMoviesAsync(It.IsAny<CancellationToken>()), Times.Never);

        lifetime.Start();
        await Task.Delay(100);
        tmdbCacheServiceMock.Verify(s => s.RefreshTrendingMoviesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await service.StopAsync(CancellationToken.None);
        Assert.True(refreshToken.IsCancellationRequested);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Start() => _started.Cancel();

        public void StopApplication()
        {
        }
    }
}
