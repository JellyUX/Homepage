using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Hosted service that removes an item from a user's Watchlist (<c>UserData.Likes = false</c>) as
/// soon as they finish watching it (<see cref="UserDataSavedListener"/>).
/// <para>
/// Deliberately server-side rather than a client-side WebSocket listener: this event fires in the
/// Jellyfin server process itself regardless of which client (web, mobile, TV, another plugin)
/// finished the playback, and survives the browser tab closing right after playback ends -- exactly
/// the moment a client-side listener would be most likely to miss it (TODO_V3.md Phase 5.3).
/// </para>
/// </summary>
public sealed class WatchlistAutoRemoveService : UserDataSavedListener
{
    private readonly IUserManager _userManager;
    private readonly ILogger<WatchlistAutoRemoveService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistAutoRemoveService"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="userManager">Jellyfin user manager, used to resolve the full user from the event's <c>UserId</c>.</param>
    /// <param name="logger">Logger.</param>
    public WatchlistAutoRemoveService(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILogger<WatchlistAutoRemoveService> logger)
        : base(userDataManager, logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Removes the item from the watchlist when playback genuinely finishes. Deliberately checks
    /// <see cref="UserDataSaveReason.PlaybackFinished"/> specifically, not just "is now played" --
    /// <see cref="UserDataSaveReason.TogglePlayed"/> (a manual "mark as watched" action, without
    /// actually watching) must NOT trigger auto-removal, per TODO_V3.md Phase 5.3's own caution.
    /// Writing back with <see cref="UserDataSaveReason.UpdateUserRating"/> (not
    /// <c>PlaybackFinished</c>) means this handler's own write can never re-trigger itself: the guard
    /// below rejects any reason other than <c>PlaybackFinished</c> before it would ever recurse.
    /// </summary>
    protected override void OnUserDataSaved(UserDataSaveEventArgs e)
    {
        if (e.SaveReason != UserDataSaveReason.PlaybackFinished)
        {
            return;
        }

        var userData = e.UserData;
        if (userData is null || userData.Likes != true || !userData.Played)
        {
            return;
        }

        var user = _userManager.GetUserById(e.UserId);
        if (user is null)
        {
            return;
        }

        userData.Likes = false;
        UserDataManager.SaveUserData(user, e.Item, userData, UserDataSaveReason.UpdateUserRating, CancellationToken.None);

        _logger.LogInformation(
            "Removed '{Item}' from {User}'s watchlist after playback finished.",
            e.Item.Name,
            user.Username);
    }
}
