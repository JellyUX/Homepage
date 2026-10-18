namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Minimal <see cref="TimeProvider"/> test double: <see cref="TimeProvider.GetUtcNow"/> and
/// <see cref="TimeProvider.CreateTimer"/> are virtual precisely so they can be overridden like this
/// for deterministic tests, without a real delay or an external package. Timers fire only from
/// <see cref="Advance"/>, on the calling thread; setting <see cref="UtcNowValue"/> fires none.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the time returned by <see cref="GetUtcNow"/>.</summary>
    public DateTimeOffset UtcNowValue
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        set
        {
            lock (_gate)
            {
                _utcNow = value;
            }
        }
    }

    /// <summary>Gets UTC as the local time zone, so day boundaries do not depend on the test machine.</summary>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Gets the number of timers created and armed, not yet fired (one-shot) or disposed.</summary>
    public int ActiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(t => t.DueAt is not null);
            }
        }
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => UtcNowValue;

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock forward and fires, in due order, every timer whose due time is reached;
    /// a periodic timer fires once per elapsed period.
    /// </summary>
    /// <param name="by">How far to move the clock.</param>
    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _utcNow += by;
        }

        while (true)
        {
            ManualTimer? due;
            lock (_gate)
            {
                due = _timers
                    .Where(t => t.DueAt is { } at && at <= _utcNow)
                    .OrderBy(t => t.DueAt)
                    .FirstOrDefault();
                if (due is null)
                {
                    return;
                }

                due.DueAt = due.Period > TimeSpan.Zero ? due.DueAt + due.Period : null;
            }

            due.Callback(due.State);
        }
    }

    private void Arm(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _utcNow + dueTime;
            timer.Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan Period { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            owner.Arm(this, dueTime, period);
            return true;
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
