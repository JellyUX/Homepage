using System.Collections.Concurrent;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Library;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Runs the items query of a library section in the order configured on its row (TODO_V4.md 8.1,
/// F-53): Admin, Personalized and Seasonal widgets build the filter, this class orders and pages it.
/// <para>
/// <see cref="WidgetSort.Random"/> is not an <c>ORDER BY RANDOM()</c> per request, which would sort
/// every match on every load and change the images each time: the matching ids are read and shuffled
/// once a day per user and section, the first <see cref="WidgetPayload.MaxItems"/> are kept, and every
/// later page of that day is a slice of them. The draw is per user because the query applies that
/// user's library access and parental rules (F-19); the memory is bounded to MaxItems ids per user
/// and section, and drops every earlier day's draws on the first access of a new day.
/// </para>
/// </summary>
public sealed class WidgetItemFetcher
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<DrawKey, Lazy<Guid[]>> _draws = new();
    private int _drawDay = int.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetItemFetcher"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="timeProvider">Time source of the current day (server local time). Defaults to <see cref="TimeProvider.System"/>.</param>
    public WidgetItemFetcher(ILibraryManager libraryManager, IDtoService dtoService, TimeProvider? timeProvider = null)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the page of <paramref name="query"/> that <paramref name="payload"/> asks for, in the
    /// payload's configured order. The query's own <c>OrderBy</c>, <c>StartIndex</c> and <c>Limit</c>
    /// are replaced.
    /// </summary>
    /// <param name="widgetType">The section's widget type, part of the identity of a random draw.</param>
    /// <param name="user">The user the items are returned to; the query must be built for them.</param>
    /// <param name="query">The filtered items query of the section.</param>
    /// <param name="dtoOptions">The DTO options of the returned items.</param>
    /// <param name="payload">The request: page, order, MaxItems and the parameters of the section.</param>
    /// <returns>The page of items.</returns>
    public WidgetResult Fetch(
        string widgetType,
        User user,
        InternalItemsQuery query,
        DtoOptions dtoOptions,
        WidgetPayload payload)
    {
        FeaturedItems.Restrict(query, payload);
        IReadOnlyList<BaseItem> items;
        if (WidgetSort.NormalizeSortBy(payload.SortBy) == WidgetSort.Random)
        {
            var drawn = GetDraw(widgetType, user, query, payload);
            var page = drawn.Skip(payload.StartIndex).Take(payload.Limit).ToList();
            items = UserVisibleItems.Resolve(_libraryManager, user, page, out _);
        }
        else
        {
            query.OrderBy = WidgetSort.ToOrderBy(payload.SortBy, payload.SortOrder);
            query.StartIndex = payload.StartIndex;
            query.Limit = payload.Limit;
            items = _libraryManager.GetItemsResult(query).Items;
        }

        return WidgetResult.Project(_dtoService, FeaturedItems.BackdropsFirst(items, payload), dtoOptions, user);
    }

    /// <summary>
    /// Test-only seam: the number of random draws held in memory.
    /// </summary>
    /// <returns>The number of (user, section) draws cached for the current day.</returns>
    internal int DrawCountForTesting() => _draws.Count;

    private Guid[] GetDraw(string widgetType, User user, InternalItemsQuery query, WidgetPayload payload)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        PurgeIfNewDay(today);

        var key = new DrawKey(user.Id, today, InstanceKey(widgetType, payload));
        var lazy = _draws.GetOrAdd(key, k => new Lazy<Guid[]>(() => Draw(k, query, payload.MaxItems)));
        try
        {
            return lazy.Value;
        }
        catch
        {
            // A failed draw is not kept for the day: the next request draws again.
            _draws.TryRemove(new KeyValuePair<DrawKey, Lazy<Guid[]>>(key, lazy));
            throw;
        }
    }

    private Guid[] Draw(DrawKey key, InternalItemsQuery query, int maxItems)
    {
        query.OrderBy = [];
        query.StartIndex = null;
        query.Limit = null;
        var ids = _libraryManager.GetItemIds(query).ToList();

        // Guid.GetHashCode and StableHash are the same in every process, unlike string.GetHashCode,
        // so a restart during the day draws the same order again.
        var seed = unchecked((key.UserId.GetHashCode() * 397) ^ key.Day.DayNumber ^ StableHash(key.Instance));
        SeededShuffle.Shuffle(ids, seed);
        return ids.Take(Math.Clamp(maxItems, 1, WidgetConfig.MaxItemsCeiling)).ToArray();
    }

    /// <summary>
    /// Identifies a section by everything that changes its set of items: two rows of the same type
    /// with different values, filters, MaxItems or featured restrictions never share a draw, and
    /// editing a row starts a new one at once instead of the next day.
    /// </summary>
    private static string InstanceKey(string widgetType, WidgetPayload payload)
    {
        var extra = payload.ExtraParams is null
            ? string.Empty
            : string.Join('\u001f', payload.ExtraParams.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + '=' + p.Value));
        return string.Join('\u001e', widgetType, payload.AdditionalData ?? string.Empty, payload.MaxItems, extra, payload.Featured, payload.FeaturedMinRating);
    }

    private static int StableHash(string value)
    {
        // FNV-1a, 32 bits.
        var hash = 2166136261;
        foreach (var c in value)
        {
            hash = unchecked((hash ^ c) * 16777619);
        }

        return unchecked((int)hash);
    }

    private void PurgeIfNewDay(DateOnly today)
    {
        var previous = Volatile.Read(ref _drawDay);
        if (previous == today.DayNumber
            || Interlocked.CompareExchange(ref _drawDay, today.DayNumber, previous) != previous)
        {
            return;
        }

        foreach (var entry in _draws)
        {
            if (entry.Key.Day < today)
            {
                _draws.TryRemove(entry);
            }
        }
    }

    private readonly record struct DrawKey(Guid UserId, DateOnly Day, string Instance);
}
