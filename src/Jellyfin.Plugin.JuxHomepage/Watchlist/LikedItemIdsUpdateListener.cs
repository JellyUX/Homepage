using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// Hosted service that keeps <see cref="LikedItemIdsCache"/> exact (DEC-P07-7): a user data save of a
/// movie or a series adds or removes its id by its <c>Likes</c> value, whatever saved it (the card
/// buttons, the auto-remove of a watched title, another plugin), and an item removed from the library
/// leaves every user's ids. Other item types are not in the Watchlist ids (F-06).
/// </summary>
public sealed class LikedItemIdsUpdateListener : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly LikedItemIdsCache _cache;
    private readonly ILogger<LikedItemIdsUpdateListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LikedItemIdsUpdateListener"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="cache">The liked ids cache.</param>
    /// <param name="logger">Logger.</param>
    public LikedItemIdsUpdateListener(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        LikedItemIdsCache cache,
        ILogger<LikedItemIdsUpdateListener> logger)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemRemoved += OnItemRemoved;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnUserDataSaved), () =>
        {
            if (e.Item is Movie or Series)
            {
                _cache.Apply(e.UserId, e.Item.Id, e.UserData?.Likes == true);
            }
        });

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnItemRemoved), () => _cache.Remove(e.Item.Id));
}
