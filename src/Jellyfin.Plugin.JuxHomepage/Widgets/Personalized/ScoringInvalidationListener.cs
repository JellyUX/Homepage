using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Hosted service that marks a user's scoring snapshot stale when they finish a title or toggle
/// its played state (F-02), so a first finished title can produce a personalized section the same
/// day instead of waiting for the day boundary. Other user-data saves (progress, ratings,
/// favorites) are ignored: they fire continuously during playback.
/// </summary>
public sealed class ScoringInvalidationListener : UserDataSavedListener
{
    private readonly ScoringService _scoringService;

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
        : base(userDataManager, logger)
    {
        _scoringService = scoringService;
    }

    /// <inheritdoc/>
    protected override void OnUserDataSaved(UserDataSaveEventArgs e)
    {
        if (e.SaveReason is UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed)
        {
            _scoringService.MarkStale(e.UserId);
        }
    }
}
