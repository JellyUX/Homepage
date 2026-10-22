using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Builds the home screen of recently active users ahead, after a server start (PERF-07, DEC-P07-3):
/// otherwise the first home screen after a restart waits for the user's scoring and builds every
/// section cold. A while after the server started (30 s, 90 s on a machine of 2 processors or fewer,
/// so the server's own startup work goes first), it takes the users active in the last 7 days, the
/// 10 most recent at most, and one at a time computes their scoring, then builds their home screen in
/// the language of their last home screen (DEC-P07-10). Users active earlier, or for whom JellyUX is
/// off, are left alone.
/// </summary>
public sealed class HomeWarmupService : IHostedService, IDisposable
{
    /// <summary>The most users warmed after a start.</summary>
    internal const int MaxUsers = 10;

    /// <summary>How recently a user must have been active to be warmed.</summary>
    internal static readonly TimeSpan ActiveWithin = TimeSpan.FromDays(7);

    private readonly IHostApplicationLifetime _lifetime;
    private readonly IUserManager _userManager;
    private readonly ScoringService _scoring;
    private readonly WidgetService _widgets;
    private readonly HomeLanguageMemory _homeLanguages;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HomeWarmupService> _logger;
    private readonly int _processorCount;
    private readonly ServerStartedRun _afterStart = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeWarmupService"/> class.
    /// </summary>
    /// <param name="lifetime">Signals the server start.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="scoring">The personalization scoring.</param>
    /// <param name="widgets">Builds and caches home screens.</param>
    /// <param name="homeLanguages">The language of each user's last home screen.</param>
    /// <param name="serverConfiguration">The server configuration, for its interface language.</param>
    /// <param name="localization">Normalizes a language like a home screen request does.</param>
    /// <param name="timeProvider">Time source of the delay and of the activity window.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="processorCount">The machine's processor count; <see cref="Environment.ProcessorCount"/> when null.</param>
    public HomeWarmupService(
        IHostApplicationLifetime lifetime,
        IUserManager userManager,
        ScoringService scoring,
        WidgetService widgets,
        HomeLanguageMemory homeLanguages,
        IServerConfigurationManager serverConfiguration,
        ILocalizationService localization,
        TimeProvider timeProvider,
        ILogger<HomeWarmupService> logger,
        int? processorCount = null)
    {
        _lifetime = lifetime;
        _userManager = userManager;
        _scoring = scoring;
        _widgets = widgets;
        _homeLanguages = homeLanguages;
        _serverConfiguration = serverConfiguration;
        _localization = localization;
        _timeProvider = timeProvider;
        _logger = logger;
        _processorCount = processorCount ?? Environment.ProcessorCount;
    }

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
            var activeSince = _timeProvider.GetUtcNow().UtcDateTime - ActiveWithin;
            var users = _userManager.GetUsers()
                .Where(u => u.LastActivityDate >= activeSince)
                .OrderByDescending(u => u.LastActivityDate)
                .Take(MaxUsers)
                .Select(u => u.Id)
                .ToList();
            var fallback = _localization.NormalizeLanguage(_serverConfiguration.Configuration.UICulture);

            var warmed = 0;
            foreach (var userId in users)
            {
                if (await WarmUserAsync(userId, fallback, cancellationToken).ConfigureAwait(false))
                {
                    warmed++;
                }
            }

            _logger.LogInformation(
                "Built the home screen of {Warmed} of {Active} recently active users ahead in {Elapsed} ms.",
                warmed,
                users.Count,
                (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds);
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

    private async Task<bool> WarmUserAsync(Guid userId, string fallbackLanguage, CancellationToken cancellationToken)
    {
        if (!_widgets.IsEnabledFor(userId))
        {
            return false;
        }

        try
        {
            // A layout built before the scoring finished would not be cached: the scoring goes first, and
            // a full scoring queue skips the user.
            if (!await _scoring.WarmAsync(userId, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("Scoring queue full or scoring dropped while computed; the home screen of user {UserId} is not built ahead.", userId);
                return false;
            }

            var language = _homeLanguages.Get(userId) ?? fallbackLanguage;
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
