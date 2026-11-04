using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Tasks;

/// <summary>
/// Computes the home screens of the recently active users every night (S2, DR-033), at 04:00 by default: each user's
/// scoring first, then their layout, its deferred rows stored, in the language of their last home screen. The
/// personalized, admin, TMDb, award and seasonal sections then keep that order through the day, their titles'
/// watched state staying live, so no scoring nor probe runs while the server is used. One user at a time, behind the
/// gate of the layout rebuilds; a user that fails is logged and the others are still computed.
/// </summary>
public sealed class HomeSectionsRefreshTask : IScheduledTask
{
    /// <summary>The hour the task runs at by default, server time.</summary>
    internal static readonly TimeSpan DefaultTime = TimeSpan.FromHours(4);

    private readonly ActiveHomeUsers _activeUsers;
    private readonly ScoringService _scoring;
    private readonly WidgetService _widgets;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HomeSectionsRefreshTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSectionsRefreshTask"/> class.
    /// </summary>
    /// <param name="activeUsers">The recently active users and the language of their last home screen.</param>
    /// <param name="scoring">The personalization scoring.</param>
    /// <param name="widgets">Computes and stores home screens.</param>
    /// <param name="timeProvider">Time source of the logged duration.</param>
    /// <param name="logger">Logger.</param>
    public HomeSectionsRefreshTask(
        ActiveHomeUsers activeUsers,
        ScoringService scoring,
        WidgetService widgets,
        TimeProvider timeProvider,
        ILogger<HomeSectionsRefreshTask> logger)
    {
        _activeUsers = activeUsers;
        _scoring = scoring;
        _widgets = widgets;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "JellyUX - Home sections refresh";

    /// <inheritdoc/>
    public string Key => "Jellyfin.Plugin.JuxHomepage.HomeSectionsRefresh";

    /// <inheritdoc/>
    public string Description => "Computes the personalized and deferred home sections of the recently active users ahead.";

    /// <inheritdoc/>
    public string Category => "JellyUX Homepage";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var users = _activeUsers.MostRecent(int.MaxValue);
        var computed = 0;
        for (var i = 0; i < users.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ComputeUserAsync(users[i], cancellationToken).ConfigureAwait(false))
            {
                computed++;
            }

            progress.Report(100.0 * (i + 1) / users.Count);
        }

        _logger.LogInformation(
            "Computed the home screen of {Computed} of {Active} recently active users in {Elapsed} ms.",
            computed,
            users.Count,
            (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = DefaultTime.Ticks
        };
    }

    private async Task<bool> ComputeUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_widgets.IsEnabledFor(userId))
        {
            return false;
        }

        try
        {
            // The scoring of the new day first, so the layout is not computed from yesterday's scores.
            if (!await _scoring.WarmAsync(userId, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("Scoring queue full or scoring dropped; the home screen of user {UserId} is left to its next visit.", userId);
                return false;
            }

            await _widgets.ComputeAsync(userId, _activeUsers.LanguageOf(userId), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to compute the home screen of user {UserId} ahead.", userId);
            return false;
        }
    }
}
