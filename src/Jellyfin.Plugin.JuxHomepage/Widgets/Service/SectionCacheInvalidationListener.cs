using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Hosted service that keeps the section pages of <see cref="SectionResultCache"/> fresh (DEC-P07-2):
/// any user data save drops that user's pages (a title watched, resumed, rated or liked changes
/// Continue Watching, Next Up, the Watchlist and the sections that hide watched titles), and an item
/// added to or removed from the library drops every page (Recently Added). A configuration change or a
/// user's settings change already drops them through the layout cache's version.
/// </summary>
public sealed class SectionCacheInvalidationListener : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly SectionResultCache _cache;
    private readonly ILogger<SectionCacheInvalidationListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionCacheInvalidationListener"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="cache">The section page cache.</param>
    /// <param name="logger">Logger.</param>
    public SectionCacheInvalidationListener(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        SectionResultCache cache,
        ILogger<SectionCacheInvalidationListener> logger)
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
        EventHandlerGuard.Run(_logger, nameof(OnUserDataSaved), () => _cache.Invalidate(e.UserId));

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnLibraryChanged), _cache.Clear);
}
