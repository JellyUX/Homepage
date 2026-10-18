using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class ManualTimeProviderTests
{
    [Fact]
    public void Advance_FiresAOneShotTimerAtItsDueTimeOnly()
    {
        var clock = new ManualTimeProvider();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        Assert.Equal(1, clock.ActiveTimers);

        clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(0, fired);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, fired);
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    public void Advance_FiresAPeriodicTimerOncePerElapsedPeriod()
    {
        var clock = new ManualTimeProvider();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromSeconds(3.5));

        Assert.Equal(3, fired);
        Assert.Equal(1, clock.ActiveTimers);
    }

    [Fact]
    public void ChangeToInfinite_AndDispose_StopTheTimer()
    {
        var clock = new ManualTimeProvider();
        var fired = 0;
        var paused = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        var disposed = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        paused.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        disposed.Dispose();
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, fired);
        Assert.Equal(0, clock.ActiveTimers);
        paused.Dispose();
    }

    [Fact]
    public async Task WaitAsync_TimesOutOnlyWhenTheClockIsAdvanced()
    {
        var clock = new ManualTimeProvider();
        var never = new TaskCompletionSource();

        var wait = never.Task.WaitAsync(TimeSpan.FromSeconds(2), clock);
        Assert.False(wait.IsCompleted);
        Assert.Equal(1, clock.ActiveTimers);

        clock.Advance(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TimeoutException>(() => wait);
    }

    [Fact]
    public void SettingUtcNowValue_FiresNoTimer()
    {
        var clock = new ManualTimeProvider();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.UtcNowValue += TimeSpan.FromMinutes(1);

        Assert.Equal(0, fired);
    }
}
