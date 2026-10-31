using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Rewards.Tasks;

/// <summary>
/// Scheduled task that refreshes every configured Rewards widget instance once a week. Weekly rather
/// than daily (unlike <see cref="TMDb.Tasks.TMDbDailyRefreshTask"/>): award data changes at most once
/// per ceremony edition, so a daily refresh would only add load on Wikidata's public service for no
/// benefit (see TODO_V2.md Phase 14 research on Wikidata's 2026 rate-limit policy).
/// Auto-discovered by Jellyfin -- not registered in DI (its dependencies are DI-registered, but the
/// task itself is instantiated by Jellyfin's scheduled task subsystem, exactly like
/// <see cref="TMDb.Tasks.TMDbDailyRefreshTask"/>).
/// </summary>
public sealed class RewardsWeeklyRefreshTask : IScheduledTask
{
    private readonly IRewardsCacheService _cacheService;
    private readonly ILogger<RewardsWeeklyRefreshTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RewardsWeeklyRefreshTask"/> class.
    /// </summary>
    /// <param name="cacheService">The Rewards cache service to refresh.</param>
    /// <param name="logger">Logger.</param>
    public RewardsWeeklyRefreshTask(IRewardsCacheService cacheService, ILogger<RewardsWeeklyRefreshTask> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "JellyUX - Rewards Refresh";

    /// <inheritdoc/>
    public string Key => "Jellyfin.Plugin.JuxHomepage.RewardsRefresh";

    /// <inheritdoc/>
    public string Description => "Refreshes cached Wikidata award data for every configured Rewards widget instance.";

    /// <inheritdoc/>
    public string Category => "JellyUX Homepage";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // RefreshAllInstancesAsync is independently fault-tolerant per instance (RewardsCacheService
        // logs and swallows its own failures), so no special-casing is needed here beyond delegating
        // to it. Shared with the admin "Refresh now" action (JuxHomepageAdminController) so the
        // instance-enumeration logic isn't duplicated.
        await _cacheService.RefreshAllInstancesAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
        _logger.LogInformation("Rewards weekly refresh completed.");
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Sunday,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }
}
