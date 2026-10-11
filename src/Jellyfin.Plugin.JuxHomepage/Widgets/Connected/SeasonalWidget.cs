using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Connected;

/// <summary>
/// A configurable seasonal widget that shows library items during a recurring, admin-configured
/// <c>MM-dd</c> date window (e.g. Halloween, Christmas), optionally filtered by genre and/or tag.
/// <para>
/// TODO_V3.md Phase 9.1: classified as <see cref="WidgetCategory.Connected"/> rather than
/// <see cref="WidgetCategory.Admin"/> -- even though it queries the local library, not an external
/// source -- because <c>config.html</c>'s admin editor UI is chosen purely by category
/// (<c>isAdmin()</c> tests <c>Category === Admin</c>, independent of the actual C# base class) and
/// renders a generic single "Value" + autocomplete field for any Admin-category widget. This widget
/// needs several custom fields (theme, start/end date, optional genre, optional tag), the same
/// situation <see cref="DiscoverMoviesWidget"/> and Rewards are already in, so it follows their
/// precedent of using <see cref="WidgetCategory.Connected"/> to get a fully custom editor instead.
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
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeasonalWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    public SeasonalWidget(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
    }

    /// <inheritdoc/>
    public string WidgetType => "jux.connected.seasonal";

    /// <inheritdoc/>
    public string DefaultDisplayName => "Seasonal";

    /// <inheritdoc/>
    public WidgetCategory Category => WidgetCategory.Connected;

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

        if (!IsInSeason(DateTime.Now, start, end))
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var user = _userManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();
        var query = new InternalItemsQuery(user)
        {
            Recursive = true,
            IsMissing = false,
            EnableTotalRecordCount = false,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            StartIndex = payload.StartIndex,
            Limit = payload.Limit,
            DtoOptions = dtoOptions
        };

        if (extra.TryGetValue("genre", out var genre) && !string.IsNullOrWhiteSpace(genre))
        {
            query.Genres = [genre];
        }

        if (extra.TryGetValue("tag", out var tag) && !string.IsNullOrWhiteSpace(tag))
        {
            query.Tags = [tag];
        }

        QueryResult<BaseItem> result = _libraryManager.GetItemsResult(query);
        var dtos = _dtoService.GetBaseItemDtos(result.Items, dtoOptions, user);
        return Task.FromResult(new WidgetResult(dtos, dtos.Count));
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
    /// Parses a recurring <c>MM-dd</c> date string (e.g. <c>10-31</c>) into a month/day tuple.
    /// </summary>
    /// <param name="value">The raw string to parse.</param>
    /// <param name="result">The parsed month/day tuple, if successful.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> was a valid <c>MM-dd</c> date.</returns>
    private static bool TryParseMonthDay(string? value, out (int Month, int Day) result)
    {
        result = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // A fixed reference year is used purely to validate the month/day combination (e.g. reject
        // "02-30"); the year itself is discarded, since the window recurs every year.
        if (!DateTime.TryParseExact(
                value,
                "MM-dd",
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
