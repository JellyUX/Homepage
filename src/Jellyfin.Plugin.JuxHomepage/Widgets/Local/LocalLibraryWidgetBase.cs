using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Local;

/// <summary>
/// Base class of the library-wide sections that need no TMDb data (TODO_V4.md 9.1, F-61): the whole
/// library of the chosen item types, in an order fixed by the widget. One items query per section,
/// without a total count (F-05).
/// <para>
/// <c>ExtraParams["itemTypes"]</c> picks the item types: <c>Movie</c>, <c>Series</c> or both
/// (<c>Movie,Series</c>, also the fallback for a missing or unknown value).
/// </para>
/// </summary>
public abstract class LocalLibraryWidgetBase : IWidget
{
    /// <summary>The <c>ExtraParams</c> key of the item types of the section.</summary>
    public const string ItemTypesParam = "itemTypes";

    private readonly IUserManager _userManager;
    private readonly WidgetItemFetcher _itemFetcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalLibraryWidgetBase"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="timeProvider">Time source of the current day. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="itemFetcher">Runs the section query in its order; a private one when null (tests).</param>
    protected LocalLibraryWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        TimeProvider? timeProvider = null,
        WidgetItemFetcher? itemFetcher = null)
    {
        _userManager = userManager;
        TimeProvider = timeProvider ?? TimeProvider.System;
        _itemFetcher = itemFetcher ?? new WidgetItemFetcher(libraryManager, dtoService, TimeProvider);
    }

    /// <inheritdoc/>
    public abstract string WidgetType { get; }

    /// <inheritdoc/>
    public abstract string DefaultDisplayName { get; }

    /// <inheritdoc/>
    public WidgetCategory Category => WidgetCategory.Admin;

    /// <inheritdoc/>
    public int DefaultMinItems => 4;

    /// <summary>Gets the time source of the current day.</summary>
    protected TimeProvider TimeProvider { get; }

    /// <summary>Gets the order of the section's cards, a <see cref="WidgetSort"/> value.</summary>
    protected abstract string SortBy { get; }

    /// <inheritdoc/>
    public IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank) => this;

    /// <inheritdoc/>
    public Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();
        var query = LibrarySectionQuery.Create(user, dtoOptions, ParseItemTypes(payload.ExtraParams));
        ApplyFilter(query);

        // The order belongs to the widget, not to the row: the payload is copied so the caller's
        // configured sort is never read nor changed.
        var ordered = new WidgetPayload
        {
            UserId = payload.UserId,
            AdditionalData = payload.AdditionalData,
            StartIndex = payload.StartIndex,
            Limit = payload.Limit,
            ExtraParams = payload.ExtraParams,
            SortBy = SortBy,
            SortOrder = WidgetSort.Descending,
            MaxItems = payload.MaxItems
        };

        return Task.FromResult(_itemFetcher.Fetch(WidgetType, user, query, dtoOptions, ordered));
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
    /// Reads the item types of a section from its <c>ExtraParams</c>.
    /// </summary>
    /// <param name="extra">The section's extra parameters.</param>
    /// <returns>Movies, series, or both when the value is missing or unknown.</returns>
    internal static BaseItemKind[] ParseItemTypes(IReadOnlyDictionary<string, string>? extra)
    {
        var raw = extra is not null && extra.TryGetValue(ItemTypesParam, out var value) ? value : null;
        return raw switch
        {
            "Movie" => [BaseItemKind.Movie],
            "Series" => [BaseItemKind.Series],
            _ => [BaseItemKind.Movie, BaseItemKind.Series]
        };
    }

    /// <summary>
    /// Narrows the section's query to the items its order makes sense for.
    /// </summary>
    /// <param name="query">The items query of the section.</param>
    protected abstract void ApplyFilter(InternalItemsQuery query);
}
