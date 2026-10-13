using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Hosted service that removes a title from a user's Watchlist (<c>UserData.Likes = false</c>) as
/// soon as they finish watching it (<see cref="UserDataSavedListener"/>): a movie when its playback
/// finishes, a series when its last unplayed episode does (TODO_V4.md 8.4, F-59).
/// <para>
/// The Watchlist is Jellyfin's own <c>Likes</c>, which other plugins read too (Jellyfin Enhanced
/// syncs it to Seerr): with <see cref="PluginConfiguration.WatchlistAutoRemove"/> off, JellyUX never
/// writes it on its own.
/// </para>
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
    private readonly ILibraryManager? _libraryManager;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private readonly ILogger<WatchlistAutoRemoveService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistAutoRemoveService"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="userManager">Jellyfin user manager, used to resolve the full user from the event's <c>UserId</c>.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="libraryManager">Jellyfin library manager, which tells whether a series has an unplayed episode left; series are never removed without it.</param>
    /// <param name="getConfiguration">Returns the plugin configuration; the switch is on when null.</param>
    public WatchlistAutoRemoveService(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILogger<WatchlistAutoRemoveService> logger,
        ILibraryManager? libraryManager = null,
        Func<PluginConfiguration?>? getConfiguration = null)
        : base(userDataManager, logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _getConfiguration = getConfiguration ?? (() => null);
        _logger = logger;
    }

    /// <summary>
    /// Removes the title from the watchlist when playback genuinely finishes. Deliberately checks
    /// <see cref="UserDataSaveReason.PlaybackFinished"/> specifically, not just "is now played" --
    /// <see cref="UserDataSaveReason.TogglePlayed"/> (a manual "mark as watched" action, without
    /// actually watching) must NOT trigger auto-removal, per TODO_V3.md Phase 5.3's own caution.
    /// Writing back with <see cref="UserDataSaveReason.UpdateUserRating"/> (not
    /// <c>PlaybackFinished</c>) means this handler's own write can never re-trigger itself.
    /// </summary>
    /// <param name="e">The save.</param>
    protected override void OnUserDataSaved(UserDataSaveEventArgs e)
    {
        if (e.SaveReason != UserDataSaveReason.PlaybackFinished
            || _getConfiguration()?.WatchlistAutoRemove == false
            || e.UserData is null
            || !e.UserData.Played)
        {
            return;
        }

        var user = _userManager.GetUserById(e.UserId);
        if (user is null)
        {
            return;
        }

        if (e.Item is Episode episode)
        {
            RemoveFinishedSeries(user, episode);
        }
        else if (e.UserData.Likes == true)
        {
            Remove(user, e.Item, e.UserData);
        }
    }

    // A series leaves the watchlist with its last unplayed episode: the episode just finished, and
    // no other episode of the series is left unplayed for this user.
    private void RemoveFinishedSeries(User user, Episode episode)
    {
        if (_libraryManager?.GetItemById(episode.SeriesId) is not Series series)
        {
            return;
        }

        var seriesData = UserDataManager.GetUserData(user, series);
        if (seriesData?.Likes != true)
        {
            return;
        }

        var unplayed = _libraryManager.GetItemIds(new InternalItemsQuery(user)
        {
            AncestorIds = [series.Id],
            IncludeItemTypes = [BaseItemKind.Episode],
            IsPlayed = false,
            IsVirtualItem = false,
            Recursive = true,
            Limit = 1
        });
        if (unplayed.Count == 0)
        {
            Remove(user, series, seriesData);
        }
    }

    private void Remove(User user, BaseItem item, UserItemData userData)
    {
        userData.Likes = false;
        UserDataManager.SaveUserData(user, item, userData, UserDataSaveReason.UpdateUserRating, CancellationToken.None);

        _logger.LogInformation(
            "Removed '{Item}' from {User}'s watchlist after playback finished.",
            item.Name,
            user.Username);
    }
}
