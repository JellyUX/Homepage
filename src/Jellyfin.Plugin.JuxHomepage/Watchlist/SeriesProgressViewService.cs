using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <inheritdoc cref="ISeriesProgressViewService"/>
public sealed class SeriesProgressViewService : ISeriesProgressViewService
{
    private readonly ISeriesProgressCacheService _seriesProgressCache;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly ILogger<SeriesProgressViewService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesProgressViewService"/> class.
    /// </summary>
    /// <param name="seriesProgressCache">Series Progress disk cache.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="logger">Logger.</param>
    public SeriesProgressViewService(
        ISeriesProgressCacheService seriesProgressCache,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ILogger<SeriesProgressViewService> logger)
    {
        _seriesProgressCache = seriesProgressCache;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public SeriesProgressResult GetItems(
        Guid userId,
        string? sortBy,
        string? sortOrder,
        string? searchTerm,
        int startIndex,
        int limit,
        CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return SeriesProgressResult.Empty;
        }

        // A finished series has nothing left to watch: it leaves Progress and stays in Statistics,
        // which counts the completed series from the same cache (TODO_V4.md 8.2, F-54).
        var entries = NameSearch.Filter(
            _seriesProgressCache.GetProgress(userId).Where(e => e.WatchedEpisodes < e.TotalEpisodes).ToList(),
            e => e.SeriesName,
            NameSearch.Normalize(searchTerm));
        var sorted = Sort(entries, sortBy, sortOrder);

        // Same rights check as MovieHistoryViewService: the whole list is filtered before paginating
        // so neither the page nor the total includes a series the user can no longer open.
        var visibleItems = UserVisibleItems.Resolve(
            _libraryManager,
            user,
            sorted.Select(e => e.SeriesId).ToList(),
            out var unresolvedCount);

        if (unresolvedCount > 0)
        {
            _logger.LogDebug(
                "{Count} cached Series Progress entrie(s) no longer resolve to a local library item (likely removed since the last cache refresh).",
                unresolvedCount);
        }

        var entriesById = sorted.GroupBy(e => e.SeriesId).ToDictionary(g => g.Key, g => g.First());
        var resolvedItems = visibleItems.Skip(startIndex).Take(limit).ToList();
        if (resolvedItems.Count == 0)
        {
            return new SeriesProgressResult { Items = [], TotalRecordCount = visibleItems.Count };
        }

        var resolvedEntries = resolvedItems.Select(i => entriesById[i.Id]).ToList();
        var dtos = _dtoService.GetBaseItemDtos(resolvedItems, WidgetDtoOptions.Standard(), user);

        var items = new List<SeriesProgressItem>(dtos.Count);
        for (var i = 0; i < dtos.Count; i++)
        {
            var entry = resolvedEntries[i];
            items.Add(new SeriesProgressItem
            {
                Item = dtos[i],
                WatchedEpisodes = entry.WatchedEpisodes,
                TotalEpisodes = entry.TotalEpisodes,
                LastPlayedDate = entry.LastPlayedDate,
                LastEpisodeName = entry.LastEpisodeName,
                LastEpisodeSeasonNumber = entry.LastEpisodeSeasonNumber,
                LastEpisodeIndexNumber = entry.LastEpisodeIndexNumber
            });
        }

        return new SeriesProgressResult { Items = items, TotalRecordCount = visibleItems.Count };
    }

    /// <summary>
    /// Sorts the cached entries in memory -- these are precomputed, exhaustive per-user lists (not a
    /// live library query), so sorting here rather than in <see cref="ISeriesProgressCacheService"/>
    /// keeps the cache itself agnostic of view-specific sort options.
    /// </summary>
    private static IReadOnlyList<SeriesProgressEntry> Sort(
        IReadOnlyList<SeriesProgressEntry> entries,
        string? sortBy,
        string? sortOrder)
    {
        var descending = !WidgetSort.Matches(sortOrder, WidgetSort.Ascending);

        IOrderedEnumerable<SeriesProgressEntry> ordered = WidgetSort.Matches(sortBy, WidgetSort.SortName)
            ? (descending
                ? entries.OrderByDescending(e => e.SeriesName, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(e => e.SeriesName, StringComparer.OrdinalIgnoreCase))
            : (descending
                ? entries.OrderByDescending(e => e.LastPlayedDate ?? DateTime.MinValue)
                : entries.OrderBy(e => e.LastPlayedDate ?? DateTime.MinValue));

        return ordered.ToList();
    }
}
