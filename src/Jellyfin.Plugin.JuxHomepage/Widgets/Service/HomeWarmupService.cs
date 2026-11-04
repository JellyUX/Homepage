using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Builds the home screen of recently active users ahead, after a server start (PERF-07, DEC-P07-3):
/// otherwise the first home screen after a restart builds every section cold. A while after the server
/// started (30 s, 90 s on a machine of 2 processors or fewer, so the server's own startup work goes
/// first), it takes the users active in the last 7 days, most recent first, as many as 5 % of the memory
/// available to the server allows at about 10 MiB each (DR-011), 1 to 10, and one at a time builds their
/// home screen in the language of their last home screen (DEC-P07-10). A user whose stored home screen
/// is fresh has it read again, its first pages kept, without scoring nor probing (S5, DR-033); any
/// other user's scoring is computed first, then the home screen. Users active earlier, or for whom
/// JellyUX is off, are left alone.
/// </summary>
public sealed class HomeWarmupService : IHostedService, IDisposable
{
    /// <summary>The most users warmed after a start.</summary>
    internal const int MaxUsers = 10;

    /// <summary>The memory a warmed user keeps, measured on the large library (DR-011: ~48 MiB for 5 users).</summary>
    internal const long BytesPerUser = 10L * 1024 * 1024;

    private readonly IHostApplicationLifetime _lifetime;
    private readonly ActiveHomeUsers _activeUsers;
    private readonly ScoringService _scoring;
    private readonly WidgetService _widgets;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HomeWarmupService> _logger;
    private readonly int _processorCount;
    private readonly long _availableMemory;
    private readonly ServerStartedRun _afterStart = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeWarmupService"/> class.
    /// </summary>
    /// <param name="lifetime">Signals the server start.</param>
    /// <param name="activeUsers">The recently active users and the language of their last home screen.</param>
    /// <param name="scoring">The personalization scoring.</param>
    /// <param name="widgets">Builds and caches home screens.</param>
    /// <param name="timeProvider">Time source of the delay.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="processorCount">The machine's processor count; <see cref="Environment.ProcessorCount"/> when null.</param>
    /// <param name="availableMemoryBytes">
    /// The memory available to the server; <see cref="GCMemoryInfo.TotalAvailableMemoryBytes"/> when null.
    /// </param>
    public HomeWarmupService(
        IHostApplicationLifetime lifetime,
        ActiveHomeUsers activeUsers,
        ScoringService scoring,
        WidgetService widgets,
        TimeProvider timeProvider,
        ILogger<HomeWarmupService> logger,
        int? processorCount = null,
        long? availableMemoryBytes = null)
    {
        _lifetime = lifetime;
        _activeUsers = activeUsers;
        _scoring = scoring;
        _widgets = widgets;
        _timeProvider = timeProvider;
        _logger = logger;
        _processorCount = processorCount ?? Environment.ProcessorCount;
        _availableMemory = availableMemoryBytes ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <summary>Gets the most users warmed: 5 % of the available memory at <see cref="BytesPerUser"/> each, 1 to 10.</summary>
    internal int UserBound => (int)Math.Clamp(_availableMemory / 20 / BytesPerUser, 1, MaxUsers);

    /// <summary>Gets the delay between the server start and the warmup.</summary>
    internal TimeSpan Delay => TimeSpan.FromSeconds(_processorCount <= 2 ? 90 : 30);

    /// <summary>Gets the running warmup, once the server started; a test seam.</summary>
    internal Task? RunningForTesting => _afterStart.Running;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _afterStart.Register(_lifetime, RunAsync);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _afterStart.Stop();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose() => _afterStart.Dispose();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Delay, _timeProvider, cancellationToken).ConfigureAwait(false);

            var started = _timeProvider.GetTimestamp();
            var users = _activeUsers.MostRecent(UserBound);

            var warmed = 0;
            foreach (var userId in users)
            {
                if (await WarmUserAsync(userId, cancellationToken).ConfigureAwait(false))
                {
                    warmed++;
                }
            }

            _logger.LogInformation(
                "Built the home screen of {Warmed} of {Active} recently active users ahead in {Elapsed} ms (at most {Bound} for the memory available).",
                warmed,
                users.Count,
                (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds,
                UserBound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The server is stopping.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build the home screens of recently active users ahead.");
        }
    }

    private async Task<bool> WarmUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_widgets.IsEnabledFor(userId))
        {
            return false;
        }

        try
        {
            // A fresh stored home screen is read again without the scoring (S5). Otherwise a layout built before the
            // scoring finished would not be cached: the scoring goes first, and a full scoring queue skips the user.
            var language = _activeUsers.LanguageOf(userId);
            if (!_widgets.HasFreshStoredLayout(userId, language)
                && !await _scoring.WarmAsync(userId, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("Scoring queue full or scoring dropped while computed; the home screen of user {UserId} is not built ahead.", userId);
                return false;
            }

            await _widgets.GetWidgetsForUser(userId, 0, language, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the server stopping ends the loop: any other failure, a cancellation included,
            // skips this user alone.
            _logger.LogWarning(ex, "Failed to build the home screen of user {UserId} ahead.", userId);
            return false;
        }
    }
}
