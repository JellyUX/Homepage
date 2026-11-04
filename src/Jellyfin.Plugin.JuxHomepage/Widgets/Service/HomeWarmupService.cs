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
/// JellyUX is off, are left alone. Once the server runs, a library change that clears the library
/// sections (the end of a scan) has the same users' home screens computed again in the background, so
/// their next visit reads the new titles without waiting for it.
/// </summary>
public sealed class HomeWarmupService : IHostedService, IDisposable
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ActiveHomeUsers _activeUsers;
    private readonly ScoringService _scoring;
    private readonly WidgetService _widgets;
    private readonly SectionResultCache _sectionResults;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HomeWarmupService> _logger;
    private readonly int _processorCount;
    private readonly ServerStartedRun _afterStart = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeWarmupService"/> class.
    /// </summary>
    /// <param name="lifetime">Signals the server start.</param>
    /// <param name="activeUsers">The recently active users and the language of their last home screen.</param>
    /// <param name="scoring">The personalization scoring.</param>
    /// <param name="widgets">Builds and caches home screens.</param>
    /// <param name="sectionResults">Reports the library sources a scan changed.</param>
    /// <param name="timeProvider">Time source of the delay.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="processorCount">The machine's processor count; <see cref="Environment.ProcessorCount"/> when null.</param>
    public HomeWarmupService(
        IHostApplicationLifetime lifetime,
        ActiveHomeUsers activeUsers,
        ScoringService scoring,
        WidgetService widgets,
        SectionResultCache sectionResults,
        TimeProvider timeProvider,
        ILogger<HomeWarmupService> logger,
        int? processorCount = null)
    {
        _lifetime = lifetime;
        _activeUsers = activeUsers;
        _scoring = scoring;
        _widgets = widgets;
        _sectionResults = sectionResults;
        _timeProvider = timeProvider;
        _logger = logger;
        _processorCount = processorCount ?? Environment.ProcessorCount;
    }

    /// <summary>Gets the most users warmed: <see cref="ActiveHomeUsers.KeptBound"/>.</summary>
    internal int UserBound => _activeUsers.KeptBound;

    /// <summary>Gets the delay between the server start and the warmup.</summary>
    internal TimeSpan Delay => TimeSpan.FromSeconds(_processorCount <= 2 ? 90 : 30);

    /// <summary>Gets the running warmup, once the server started; a test seam.</summary>
    internal Task? RunningForTesting => _afterStart.Running;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _afterStart.Register(_lifetime, RunAsync);
        _sectionResults.SourcesCleared += OnSourcesCleared;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sectionResults.SourcesCleared -= OnSourcesCleared;
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

    private void OnSourcesCleared(IReadOnlyCollection<string> sources)
    {
        if (!sources.Any(SectionSources.AllLibrary.Contains))
        {
            return;
        }

        try
        {
            foreach (var userId in _activeUsers.MostRecent(UserBound).Where(_widgets.IsEnabledFor))
            {
                _widgets.ComputeInBackground(userId, _activeUsers.LanguageOf(userId));
            }
        }
        catch (Exception ex)
        {
            // The home screens are still computed again at the users' next visit.
            _logger.LogWarning(ex, "Failed to queue the home screens of recently active users after a library change.");
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
