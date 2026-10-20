using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.TMDb.Tasks;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.TMDb;

// QUAL-01: the scheduled task honors TMDbRefreshIntervalHours. Each list is stamped as written one
// minute after its run started, like a real refresh that takes a moment, so at the next run it is a
// little younger than the interval: the tolerance of one task period (DEC-P06-3) is what keeps it
// from waiting one more run.
public sealed class TMDbDailyRefreshTaskTests : IDisposable
{
    private const string DiscoverInstance = "8f3a1b2c-0001-4b1e-9c3a-1f2e3d4c5b6a";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jux-tmdb-task-tests-" + Guid.NewGuid());
    private readonly ManualTimeProvider _clock = new();
    private readonly Mock<ITMDbApiClient> _api = new();

    public TMDbDailyRefreshTaskTests()
    {
        _api.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 1, Title = "Trending" }]);
        _api.Setup(c => c.DiscoverMoviesAsync(It.IsAny<TMDbDiscoverFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TMDbMovie>)[new TMDbMovie { Id = 2, Title = "Discovered" }]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TMDbRefreshIntervalHours_72h_DailyTaskSkipsFreshLists()
    {
        var task = TaskWithInterval(72);

        await RunDailyAsync(task, days: 4);

        Assert.Equal(2, TrendingFetches);
        Assert.Equal(2, DiscoverFetches);
    }

    [Fact]
    public async Task TMDbRefreshIntervalHours_24h_DailyTaskRefreshesEveryDay()
    {
        var task = TaskWithInterval(24);

        await RunDailyAsync(task, days: 3);

        Assert.Equal(3, TrendingFetches);
        Assert.Equal(3, DiscoverFetches);
    }

    [Fact]
    public async Task TMDbRefreshIntervalHours_6h_HourlyTaskRefreshesEverySixHours()
    {
        var task = TaskWithInterval(6);

        for (var hour = 0; hour <= 12; hour++)
        {
            await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);
            _clock.Advance(TimeSpan.FromHours(1));
        }

        Assert.Equal(3, TrendingFetches);
    }

    [Fact]
    public async Task ExecuteAsync_RefreshAlreadyRunning_DoesNotLogCompleted()
    {
        // R11: a run that found another refresh in progress did nothing and must not say it completed.
        var service = new Mock<ITMDbCacheService>();
        service.Setup(s => s.RefreshDueAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var logger = new Mock<ILogger<TMDbDailyRefreshTask>>();

        await new TMDbDailyRefreshTask(service.Object, logger.Object).ExecuteAsync(new Progress<double>(), CancellationToken.None);

        var messages = logger.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log))
            .Select(i => i.Arguments[2]?.ToString() ?? string.Empty)
            .ToList();
        Assert.DoesNotContain(messages, m => m.Contains("completed", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("already running", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDefaultTriggers_RunsEveryHour()
    {
        var trigger = Assert.Single(TaskWithInterval(24).GetDefaultTriggers());

        Assert.Equal(TaskTriggerInfoType.IntervalTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, trigger.IntervalTicks);
    }

    private int TrendingFetches => _api.Invocations.Count(i => i.Method.Name == nameof(ITMDbApiClient.GetTrendingMoviesAsync));

    private int DiscoverFetches => _api.Invocations.Count(i => i.Method.Name == nameof(ITMDbApiClient.DiscoverMoviesAsync));

    private async Task RunDailyAsync(TMDbDailyRefreshTask task, int days)
    {
        for (var day = 0; day < days; day++)
        {
            await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);
            _clock.Advance(TimeSpan.FromHours(24));
        }
    }

    private TMDbDailyRefreshTask TaskWithInterval(int hours)
    {
        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.Setup(p => p.DataPath).Returns(_tempDir);
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([]);
        var configuration = new PluginConfiguration
        {
            Cache = new CacheConfig { TMDbRefreshIntervalHours = hours },
            ApiKeys = new ApiKeysConfig { TMDb = "key" },
            Widgets =
            [
                new WidgetConfig
                {
                    WidgetType = TMDbWidgetTypes.DiscoverMovies,
                    ExtraParams = [new WidgetExtraParam { Key = "value", Value = DiscoverInstance }]
                }
            ]
        };

        var service = new TMDbCacheService(
            applicationPaths.Object,
            new ClockedFileSystem(_clock),
            _api.Object,
            library.Object,
            () => configuration,
            NullLogger<TMDbCacheService>.Instance,
            _clock);
        return new TMDbDailyRefreshTask(service, NullLogger<TMDbDailyRefreshTask>.Instance);
    }

    // The real file system, except that a file's last write time is the test clock's time of the
    // write plus one minute, so a list's age follows the clock.
    private sealed class ClockedFileSystem(ManualTimeProvider clock) : IFileSystem
    {
        private readonly FileSystem _inner = new();
        private readonly Dictionary<string, DateTime> _writtenAt = new(StringComparer.Ordinal);

        public bool FileExists(string path) => _inner.FileExists(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);

        public void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            _inner.Move(sourceFileName, destFileName, overwrite);
            _writtenAt[destFileName] = (clock.UtcNowValue + TimeSpan.FromMinutes(1)).UtcDateTime;
        }

        public void Delete(string path) => _inner.Delete(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
            _inner.GetFiles(path, searchPattern, searchOption);

        public DateTime GetLastWriteTimeUtc(string path) =>
            _writtenAt.TryGetValue(path, out var at) ? at : _inner.GetLastWriteTimeUtc(path);
    }
}
