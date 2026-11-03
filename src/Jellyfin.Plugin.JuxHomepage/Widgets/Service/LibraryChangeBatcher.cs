namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Groups the library changes into few <see cref="SectionResultCache.ClearSources"/> calls (REV-04.c, DR-032).
/// Jellyfin raises one event per item, so a scan of 500 items dropped every section page 500 times and refused
/// every page computed meanwhile (FIABX-04). While a library scan task runs, the changed sources are only
/// collected and cleared once when it ends. Outside a scan (the real-time monitor), they are cleared once the
/// library stays quiet for <see cref="QuietDelay"/>, or after <see cref="MaxDelay"/> of steady changes.
/// </summary>
public sealed class LibraryChangeBatcher : IDisposable
{
    /// <summary>How long the library stays quiet before the collected sources are cleared, outside a scan.</summary>
    public static readonly TimeSpan QuietDelay = TimeSpan.FromSeconds(10);

    /// <summary>The longest a change waits outside a scan while changes keep coming.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Action<IReadOnlyCollection<string>> _clear;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _quietTimer;
    private DateTimeOffset? _firstPendingAt;
    private int _runningScans;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeBatcher"/> class.
    /// </summary>
    /// <param name="clear">Clears the cached pages of the given sources.</param>
    /// <param name="timeProvider">Time source of the quiet delay.</param>
    public LibraryChangeBatcher(Action<IReadOnlyCollection<string>> clear, TimeProvider timeProvider)
    {
        _clear = clear;
        _timeProvider = timeProvider;
        _quietTimer = timeProvider.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Records a changed source, cleared at the end of the running scan or after the quiet delay.</summary>
    /// <param name="source">The changed source.</param>
    public void Changed(string source)
    {
        bool flushNow;
        lock (_gate)
        {
            _pending.Add(source);
            var now = _timeProvider.GetUtcNow();
            _firstPendingAt ??= now;
            if (_runningScans > 0)
            {
                return;
            }

            flushNow = now - _firstPendingAt >= MaxDelay;
            if (!flushNow)
            {
                _quietTimer.Change(QuietDelay, Timeout.InfiniteTimeSpan);
            }
        }

        if (flushNow)
        {
            Flush();
        }
    }

    /// <summary>Records that a library scan started: its changes wait for <see cref="ScanEnded"/>.</summary>
    public void ScanStarted()
    {
        lock (_gate)
        {
            _runningScans++;
            _quietTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Records that a library scan ended, and clears the sources it changed once no scan runs.</summary>
    public void ScanEnded()
    {
        lock (_gate)
        {
            _runningScans = Math.Max(0, _runningScans - 1);
            if (_runningScans > 0)
            {
                return;
            }
        }

        Flush();
    }

    /// <inheritdoc/>
    public void Dispose() => _quietTimer.Dispose();

    private void Flush()
    {
        string[] sources;
        lock (_gate)
        {
            if (_runningScans > 0 || _pending.Count == 0)
            {
                return;
            }

            sources = [.. _pending];
            _pending.Clear();
            _firstPendingAt = null;
            _quietTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        _clear(sources);
    }
}
