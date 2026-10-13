using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Connected;

/// <summary>
/// A configurable seasonal widget that shows library items during a recurring, admin-configured
/// <c>MM-dd</c> date window (e.g. Halloween, Christmas), filtered by genres and/or tags. A section with
/// neither is hidden (TODO_V4.md 9.2, F-60).
/// <para>
/// Classified as <see cref="WidgetCategory.Admin"/>: the category names the data source, here the
/// local library (TODO_V4.md 6.4, F-37). The configuration page picks this widget's custom editor
/// (theme, start/end date, genres, tags) by its <see cref="WidgetType"/>, not by its
/// category. The <c>jux.connected.</c> type prefix, the namespace and the documentation URL predate
/// this and are kept, since saved configuration rows reference the type.
/// </para>
/// <para>
/// Implements <see cref="IWidget"/> directly rather than subclassing <see cref="Admin.AdminWidgetBase"/>,
/// which only supports a single <c>ExtraParams["value"]</c> filter -- this widget needs several
/// independent extra parameters read straight from <see cref="WidgetPayload.ExtraParams"/>.
/// Multiple seasonal sections (one per preset, plus any admin-created custom ones) share the same
/// <see cref="WidgetType"/> and are told apart by a GUID in <c>ExtraParams["value"]</c>, the same
/// multi-instance convention <see cref="DiscoverMoviesWidget"/> uses.
/// </para>
/// </summary>
public sealed class SeasonalWidget : IWidget
{
    private readonly IUserManager _userManager;
    private readonly TimeProvider _timeProvider;
    private readonly WidgetItemFetcher _itemFetcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeasonalWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="timeProvider">Time source of the current day (server local time). Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public SeasonalWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        TimeProvider? timeProvider = null,
        WidgetItemFetcher? itemFetcher = null)
    {
        _userManager = userManager;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _itemFetcher = itemFetcher ?? new WidgetItemFetcher(libraryManager, dtoService, _timeProvider);
    }

    /// <inheritdoc/>
    public string WidgetType => "jux.connected.seasonal";

    /// <inheritdoc/>
    public string DefaultDisplayName => "Seasonal";

    /// <inheritdoc/>
    public WidgetCategory Category => WidgetCategory.Admin;

    /// <inheritdoc/>
    public int DefaultMinItems => 4;

    /// <inheritdoc/>
    public IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank) => this;

    /// <inheritdoc/>
    public Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        var extra = payload.ExtraParams;
        if (extra is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        extra.TryGetValue("startDate", out var startDateRaw);
        extra.TryGetValue("endDate", out var endDateRaw);

        if (!TryParseMonthDay(startDateRaw, out var start) || !TryParseMonthDay(endDateRaw, out var end))
        {
            // Missing or invalid dates are treated as "never active", mirroring AdminWidgetBase's
            // fallback when its own single ExtraParams["value"] is missing.
            return Task.FromResult(WidgetResult.Empty);
        }

        if (!IsInSeason(_timeProvider.GetLocalNow().DateTime, start, end))
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var user = _userManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();
        var query = LibrarySectionQuery.Create(user, dtoOptions, [BaseItemKind.Movie, BaseItemKind.Series]);

        var genres = ParseValues(extra, "genre");
        var tags = ParseValues(extra, "tag");
        if (genres.Length == 0 && tags.Length == 0)
        {
            // A season without any filter would show the whole library: hidden instead, like an Admin
            // section whose value does not resolve (TODO_V4.md 9.2, F-60).
            return Task.FromResult(WidgetResult.Empty);
        }

        if (genres.Length > 0)
        {
            query.Genres = genres;
        }

        if (tags.Length > 0)
        {
            query.Tags = tags;
        }

        return Task.FromResult(_itemFetcher.Fetch(WidgetType, user, query, dtoOptions, payload));
    }

    /// <inheritdoc/>
    public WidgetDescriptor GetDescriptor() => new()
    {
        WidgetType = WidgetType,
        DisplayName = DefaultDisplayName,
        Category = Category,
        ViewMode = WidgetViewMode.Portrait,
        MinItems = DefaultMinItems
    };

    /// <summary>
    /// Reads the values of a filter parameter, separated by <c>|</c> as in Jellyfin's own item filters.
    /// An item matches when it has any of them; a genre and a tag must both match.
    /// </summary>
    /// <param name="extra">The section's extra parameters.</param>
    /// <param name="key">The parameter, <c>genre</c> or <c>tag</c>.</param>
    /// <returns>The trimmed, non-empty values, none when the parameter is absent.</returns>
    internal static string[] ParseValues(IReadOnlyDictionary<string, string> extra, string key) =>
        extra.TryGetValue(key, out var raw) && raw is not null
            ? raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];

    /// <summary>
    /// Parses a recurring <c>MM-dd</c> date string (e.g. <c>10-31</c>) into a month/day tuple.
    /// </summary>
    /// <param name="value">The raw string to parse.</param>
    /// <param name="result">The parsed month/day tuple, if successful.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> was a valid <c>MM-dd</c> date.</returns>
    internal static bool TryParseMonthDay(string? value, out (int Month, int Day) result)
    {
        result = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // A fixed leap reference year is used purely to validate the month/day combination (e.g.
        // reject "02-30" but keep "02-29", which parsing in the current year refuses three years out
        // of four); the year itself is discarded, since the window recurs every year.
        if (!DateTime.TryParseExact(
                "2000-" + value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        result = (parsed.Month, parsed.Day);
        return true;
    }

    /// <summary>
    /// Determines whether <paramref name="today"/> falls within a recurring <c>MM-dd</c> date window.
    /// Handles windows that wrap across the end of the year (e.g. New Year's, Dec 26 to Jan 6).
    /// </summary>
    /// <param name="today">The current date (only month/day are considered).</param>
    /// <param name="start">The window's start month/day.</param>
    /// <param name="end">The window's end month/day.</param>
    /// <returns><see langword="true"/> if <paramref name="today"/> is within the window.</returns>
    private static bool IsInSeason(DateTime today, (int Month, int Day) start, (int Month, int Day) end)
    {
        var t = (today.Month, today.Day);
        return start.CompareTo(end) <= 0
            ? t.CompareTo(start) >= 0 && t.CompareTo(end) <= 0
            : t.CompareTo(start) >= 0 || t.CompareTo(end) <= 0;
    }
}
