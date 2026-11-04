using System.Text.Json.Serialization;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Result returned by <see cref="IWidget.GetItemsAsync"/>.
/// Encapsulates the item page and the total record count independently of Jellyfin's internal
/// QueryResult type, keeping widget implementations decoupled from library internals.
/// <para>
/// A widget returns its page projected lazily (<see cref="Project"/>): the layout probe reads
/// <see cref="ItemCount"/> and never builds a DTO, and the DTOs are built once, on the first read of
/// <see cref="Items"/> (PERF-03.a, <c>Probe_BuildsNoDto_AndTheFirstReadOfItemsBuildsThemOnce</c>).
/// </para>
/// </summary>
public sealed class WidgetResult
{
    private readonly Lazy<IReadOnlyList<BaseItemDto>> _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetResult"/> class.
    /// </summary>
    /// <param name="items">The page of items to return.</param>
    /// <param name="totalRecordCount">The total number of matching records before pagination.</param>
    public WidgetResult(IReadOnlyList<BaseItemDto> items, int totalRecordCount)
    {
        _items = new Lazy<IReadOnlyList<BaseItemDto>>(items);
        ItemCount = items.Count;
        TotalRecordCount = totalRecordCount;
    }

    private WidgetResult(int itemCount, int totalRecordCount, Func<IReadOnlyList<BaseItemDto>> project, IReadOnlyList<Guid>? itemIds)
    {
        // A failed projection is not kept: the next read projects again.
        _items = new Lazy<IReadOnlyList<BaseItemDto>>(project, LazyThreadSafetyMode.PublicationOnly);
        ItemCount = itemCount;
        TotalRecordCount = totalRecordCount;
        ItemIds = itemIds;
    }

    /// <summary>Gets the page of items for this result, projected to DTOs on the first read.</summary>
    public IReadOnlyList<BaseItemDto> Items => _items.Value;

    /// <summary>
    /// Gets the total number of matching records before pagination, or the page size for widgets
    /// that skip the count query (F-05): no home section reads the full total.
    /// </summary>
    public int TotalRecordCount { get; }

    /// <summary>Gets the number of items in the page, without projecting them.</summary>
    [JsonIgnore]
    public int ItemCount { get; }

    /// <summary>
    /// Gets the library ids of the page's items, in the page's order, when the page was projected from library
    /// items (<see cref="Project"/>, <see cref="FeaturedItems.Project"/>); null for a page built otherwise, which
    /// cannot be stored as an order and read again (DR-033).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<Guid>? ItemIds { get; }

    /// <summary>
    /// Gets the first items of the page as a page of their own, projected as the widget projects a page, without
    /// reading the library again; null when the page cannot be cut (DR-033: a computed row reads a margin past the
    /// page the client reads).
    /// </summary>
    [JsonIgnore]
    internal Func<int, WidgetResult>? Head { get; private set; }

    /// <summary>Gets an empty result with no items and a count of zero.</summary>
    public static WidgetResult Empty { get; } = new WidgetResult([], 0);

    /// <summary>
    /// Returns a page whose DTOs are built on the first read of <see cref="Items"/>, not now.
    /// </summary>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="items">The page of library items.</param>
    /// <param name="dtoOptions">The DTO options of the section.</param>
    /// <param name="user">The user the items are returned to.</param>
    /// <param name="totalRecordCount">The total before pagination; the page size when null.</param>
    /// <returns>The result.</returns>
    public static WidgetResult Project(
        IDtoService dtoService,
        IReadOnlyList<BaseItem> items,
        DtoOptions dtoOptions,
        User user,
        int? totalRecordCount = null) =>
        Defer(items.Count, totalRecordCount ?? items.Count, () => dtoService.GetBaseItemDtos(items, dtoOptions, user), IdsOf(items));

    /// <summary>
    /// Returns a page of <paramref name="itemCount"/> items whose DTOs <paramref name="project"/>
    /// builds on the first read of <see cref="Items"/>.
    /// </summary>
    /// <param name="itemCount">The number of items in the page.</param>
    /// <param name="totalRecordCount">The total before pagination.</param>
    /// <param name="project">Builds the page's DTOs.</param>
    /// <param name="itemIds">The library ids of the page's items, in order; null when they are not library items.</param>
    /// <returns>The result.</returns>
    public static WidgetResult Defer(int itemCount, int totalRecordCount, Func<IReadOnlyList<BaseItemDto>> project, IReadOnlyList<Guid>? itemIds = null) =>
        new(itemCount, totalRecordCount, project, itemIds);

    /// <summary>Sets how the first items of this page are cut into a page of their own (<see cref="Head"/>).</summary>
    /// <param name="head">Returns the page of the first n items.</param>
    /// <returns>This result.</returns>
    internal WidgetResult WithHead(Func<int, WidgetResult> head)
    {
        Head = head;
        return this;
    }

    /// <summary>Returns the ids of <paramref name="items"/>, in order.</summary>
    /// <param name="items">Library items.</param>
    /// <returns>Their ids.</returns>
    internal static IReadOnlyList<Guid> IdsOf(IReadOnlyList<BaseItem> items) => [.. items.Select(item => item.Id)];
}
