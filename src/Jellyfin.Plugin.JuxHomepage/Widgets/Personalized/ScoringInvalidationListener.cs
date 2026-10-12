using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Hosted service that marks a user's scoring snapshot stale when they finish a title or toggle
/// its played state (F-02), so a first finished title can produce a personalized section the same
/// day instead of waiting for the day boundary. Other user-data saves (progress, ratings,
/// favorites) are ignored: they fire continuously during playback.
/// </summary>
public sealed class ScoringInvalidationListener : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly ScoringService _scoringService;
    private readonly ILogger<ScoringInvalidationListener> _logger;
    private EventHandler<UserDataSaveEventArgs>? _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScoringInvalidationListener"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="scoringService">The scoring service whose snapshots to refresh.</param>
    /// <param name="logger">Logger.</param>
    public ScoringInvalidationListener(
        IUserDataManager userDataManager,
        ScoringService scoringService,
        ILogger<ScoringInvalidationListener> logger)
    {
        _userDataManager = userDataManager;
        _scoringService = scoringService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = OnUserDataSaved;
        _userDataManager.UserDataSaved += _handler;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler is not null)
        {
            _userDataManager.UserDataSaved -= _handler;
        }

        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnUserDataSaved), () =>
        {
            if (e.SaveReason is UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed)
            {
                _scoringService.MarkStale(e.UserId);
            }
        });
}
