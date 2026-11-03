using Jellyfin.Plugin.JuxHomepage.Widgets;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>REV-04.c (DR-032): library changes are cleared once per scan or quiet period, not once per item.</summary>
public sealed class LibraryChangeBatcherTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly List<IReadOnlyCollection<string>> _clears = [];
    private readonly LibraryChangeBatcher _batcher;

    public LibraryChangeBatcherTests()
    {
        _batcher = new LibraryChangeBatcher(_clears.Add, _clock);
    }

    public void Dispose() => _batcher.Dispose();

    [Fact]
    public void FiveHundredChangesDuringAScan_ClearOnceWhenItEnds()
    {
        _batcher.ScanStarted();
        for (var i = 0; i < 500; i++)
        {
            _batcher.Changed(i % 2 == 0 ? SectionSources.Movies : SectionSources.Series);
        }

        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Empty(_clears);

        _batcher.ScanEnded();

        var clear = Assert.Single(_clears);
        Assert.Equal([SectionSources.Movies, SectionSources.Series], clear.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ChangesOutsideAScan_ClearOnceTheLibraryIsQuiet()
    {
        _batcher.Changed(SectionSources.Movies);
        _clock.Advance(LibraryChangeBatcher.QuietDelay - TimeSpan.FromSeconds(1));
        _batcher.Changed(SectionSources.Movies);
        _clock.Advance(LibraryChangeBatcher.QuietDelay - TimeSpan.FromSeconds(1));
        Assert.Empty(_clears);

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Single(_clears);
    }

    [Fact]
    public void SteadyChangesOutsideAScan_ClearAfterTheLongestDelay()
    {
        for (var elapsed = TimeSpan.Zero; elapsed <= LibraryChangeBatcher.MaxDelay; elapsed += TimeSpan.FromSeconds(5))
        {
            _batcher.Changed(SectionSources.Series);
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.NotEmpty(_clears);
    }

    [Fact]
    public void OverlappingScans_ClearWhenTheLastEnds()
    {
        _batcher.ScanStarted();
        _batcher.ScanStarted();
        _batcher.Changed(SectionSources.Movies);

        _batcher.ScanEnded();
        Assert.Empty(_clears);

        _batcher.ScanEnded();
        Assert.Single(_clears);
    }
}
