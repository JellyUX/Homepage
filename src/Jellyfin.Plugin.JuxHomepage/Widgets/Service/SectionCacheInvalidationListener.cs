using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Hosted service that keeps the section pages of <see cref="SectionResultCache"/> fresh (DEC-P07-2, REV-04.b,
/// DR-032). A playback start or progress report changes no cached section: the playback rows are read natively or
/// by the client on each home screen mount (V17), and dropping the pages on each report left the cache useless
/// during a playback (DR-008: 0 % hits). A playback end, a played toggle or a rating (the Watchlist, a like) drops
/// the user's direct pages and marks the deferred ones stale, read again in the background while still served. An
/// item added to or removed from the library drops every page. A configuration change or a user's settings change
/// already drops them through the layout cache's version, and a rewritten TMDb or Rewards list drops every page
/// from <c>TMDbCacheService</c> and <c>RewardsCacheService</c>.
/// </summary>
public sealed class SectionCacheInvalidationListener : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly SectionResultCache _cache;
    private readonly IWidgetRegistry _registry;
    private readonly ILogger<SectionCacheInvalidationListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionCacheInvalidationListener"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="cache">The section page cache.</param>
    /// <param name="registry">The widgets, which tell whether a section is direct.</param>
    /// <param name="logger">Logger.</param>
    public SectionCacheInvalidationListener(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        SectionResultCache cache,
        IWidgetRegistry registry,
        ILogger<SectionCacheInvalidationListener> logger)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _cache = cache;
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnUserDataSaved), () =>
        {
            if (e.SaveReason is UserDataSaveReason.PlaybackStart or UserDataSaveReason.PlaybackProgress)
            {
                return;
            }

            _cache.MarkStale(e.UserId, IsDirect);
        });

    /// <summary>
    /// Whether a section is direct; a widget type no longer registered counts as direct, so its page is dropped
    /// rather than served stale.
    /// </summary>
    private bool IsDirect(string widgetType) => _registry.GetByType(widgetType)?.Freshness != FreshnessLevel.Deferred;

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnLibraryChanged), _cache.Clear);
}
