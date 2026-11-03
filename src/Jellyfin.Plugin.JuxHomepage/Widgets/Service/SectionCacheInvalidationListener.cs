using Jellyfin.Data.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Hosted service that keeps the section pages of <see cref="SectionResultCache"/> fresh (DEC-P07-2, REV-04.b,
/// DR-032). A playback start or progress report changes no cached section: the playback rows are read natively or
/// by the client on each home screen mount (V17), and dropping the pages on each report left the cache useless
/// during a playback (DR-008: 0 % hits); nor does an import or a generic user data update. A playback end, a played
/// toggle or a rating (the Watchlist, a like) drops the user's direct pages and marks the deferred ones stale, read
/// again in the background while still served. An
/// item added to or removed from the library drops the pages of the sections that read its kind
/// (<see cref="SectionSources.OfItemKind"/>, REV-04.c), once per library scan or quiet period
/// (<see cref="LibraryChangeBatcher"/>). A configuration change or a user's settings change already
/// drops them through the layout cache's version, and a rewritten TMDb or Rewards list drops the pages of its
/// sections from <c>TMDbCacheService</c> and <c>RewardsCacheService</c>.
/// </summary>
public sealed class SectionCacheInvalidationListener : IHostedService, IDisposable
{
    /// <summary>The key of Jellyfin's library scan task (<c>RefreshMediaLibraryTask</c>).</summary>
    internal const string LibraryScanTaskKey = "RefreshLibrary";

    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly SectionResultCache _cache;
    private readonly IWidgetRegistry _registry;
    private readonly ITaskManager _taskManager;
    private readonly LibraryChangeBatcher _libraryChanges;
    private readonly ILogger<SectionCacheInvalidationListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionCacheInvalidationListener"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="cache">The section page cache.</param>
    /// <param name="registry">The widgets, which tell whether a section is direct.</param>
    /// <param name="taskManager">Jellyfin task manager, which tells when a library scan runs.</param>
    /// <param name="timeProvider">Time source of the quiet delay between library changes.</param>
    /// <param name="logger">Logger.</param>
    public SectionCacheInvalidationListener(
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        SectionResultCache cache,
        IWidgetRegistry registry,
        ITaskManager taskManager,
        TimeProvider timeProvider,
        ILogger<SectionCacheInvalidationListener> logger)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _cache = cache;
        _registry = registry;
        _taskManager = taskManager;
        _logger = logger;
        _libraryChanges = new LibraryChangeBatcher(cache.ClearSources, timeProvider, logger);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        _taskManager.TaskExecuting += OnTaskExecuting;
        _taskManager.TaskCompleted += OnTaskCompleted;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        _taskManager.TaskExecuting -= OnTaskExecuting;
        _taskManager.TaskCompleted -= OnTaskCompleted;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose() => _libraryChanges.Dispose();

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnUserDataSaved), () =>
        {
            // Only what DR-032 names ends a playback's effect on the sections; a progress report changes none, and an
            // import (NFO watched state during a scan) would mark the pages stale item by item.
            if (e.SaveReason is not (UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed or UserDataSaveReason.UpdateUserRating))
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
        EventHandlerGuard.Run(_logger, nameof(OnLibraryChanged), () =>
        {
            if (SectionSources.OfItemKind(e.Item.GetBaseItemKind()) is { } source)
            {
                _libraryChanges.Changed(source);
            }
        });

    private void OnTaskExecuting(object? sender, GenericEventArgs<IScheduledTaskWorker> e) =>
        EventHandlerGuard.Run(_logger, nameof(OnTaskExecuting), () =>
        {
            if (e.Argument.ScheduledTask.Key == LibraryScanTaskKey)
            {
                _libraryChanges.ScanStarted();
            }
        });

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e) =>
        EventHandlerGuard.Run(_logger, nameof(OnTaskCompleted), () =>
        {
            if (e.Task.ScheduledTask.Key == LibraryScanTaskKey)
            {
                _libraryChanges.ScanEnded();
            }
        });
}
