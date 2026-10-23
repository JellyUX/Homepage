using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Abstract base class for JellyUX Homepage admin widgets.
/// <para>
/// Admin widgets display items filtered by a value chosen by the administrator (e.g. a specific
/// genre, actor, studio, collection, tag, or year). The chosen value is stored in
/// <c>WidgetConfig.ExtraParams["value"]</c> and forwarded as <see cref="WidgetPayload.AdditionalData"/>
/// at runtime.
/// </para>
/// <para>
/// Multiple sections of the same admin widget type are modeled as multiple
/// <see cref="WidgetConfig"/> rows sharing the same <see cref="IWidget.WidgetType"/>, each with a
/// different <c>ExtraParams["value"]</c>.
/// </para>
/// </summary>
public abstract class AdminWidgetBase : IWidget
{
    /// <summary>The <c>ExtraParams</c> key of the item the section shows.</summary>
    public const string ValueParam = WidgetConfig.ValueParam;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminWidgetBase"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    protected AdminWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
    {
        UserManager = userManager;
        LibraryManager = libraryManager;
        DtoService = dtoService;
        ItemFetcher = itemFetcher ?? new WidgetItemFetcher(libraryManager, dtoService);
    }

    /// <summary>Gets the Jellyfin user manager.</summary>
    protected IUserManager UserManager { get; }

    /// <summary>Gets the Jellyfin library manager.</summary>
    protected ILibraryManager LibraryManager { get; }

    /// <summary>Gets the Jellyfin DTO projection service.</summary>
    protected IDtoService DtoService { get; }

    /// <summary>Gets the runner of the section query in its configured order (TODO_V4.md 8.1).</summary>
    protected WidgetItemFetcher ItemFetcher { get; }

    /// <inheritdoc/>
    public abstract string WidgetType { get; }

    /// <inheritdoc/>
    public string DefaultDisplayName => WidgetNames.English(WidgetType);

    /// <summary>Gets the default view mode identifier for this widget's cards.</summary>
    public abstract string DefaultViewMode { get; }

    /// <inheritdoc/>
    public abstract int DefaultMinItems { get; }

    /// <inheritdoc/>
    public IReadOnlyList<WidgetExtraParam> DefaultExtraParams => [];

    /// <inheritdoc/>
    public virtual WidgetCategory Category => WidgetCategory.Admin;

    /// <summary>
    /// Gets the item types to include in the query.
    /// Override and return <see langword="null"/> to include all item types (e.g. for collections,
    /// see <see cref="CollectionWidget"/>, whose items are filtered by ancestor/BoxSet linkage
    /// instead). Nullable here because an admin-chosen filter value (a specific genre, actor, etc.)
    /// can meaningfully apply across every item type; contrast with
    /// <see cref="Jellyfin.Plugin.JuxHomepage.Widgets.Personalized.PersonalizedWidgetBase.IncludeItemTypes"/>,
    /// which is never null since a personalized recommendation always needs a concrete type constraint.
    /// </summary>
    protected virtual BaseItemKind[]? IncludeItemTypes => [BaseItemKind.Movie, BaseItemKind.Series];

    /// <inheritdoc/>
    public IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank) => this;

    /// <summary>
    /// Applies the widget-specific filter to the query using the provided value.
    /// Called by <see cref="GetItemsAsync"/> after the query is initialized.
    /// </summary>
    /// <param name="query">The query to modify.</param>
    /// <param name="value">The value from <c>ExtraParams["value"]</c> / <see cref="WidgetPayload.AdditionalData"/>.</param>
    /// <returns>
    /// False when the value does not resolve to anything (deleted collection, malformed id, unknown
    /// studio...): the section is then empty, instead of an unfiltered query over the whole library.
    /// </returns>
    protected abstract bool ApplyFilter(InternalItemsQuery query, string value);

    /// <summary>
    /// Returns the list of available values for this widget type, optionally filtered by a search term.
    /// Used by the <c>GET /JuxHomepage/Widget/{widgetType}/values</c> autocomplete endpoint.
    /// </summary>
    /// <param name="user">The requesting Jellyfin user.</param>
    /// <param name="search">Optional search string for filtering results.</param>
    /// <returns>A list of <see cref="AdminWidgetValue"/> options.</returns>
    public abstract IReadOnlyList<AdminWidgetValue> GetAvailableValues(User user, string? search);

    /// <summary>
    /// Filters a list of candidate value names by an optional case-insensitive substring search,
    /// sorts them alphabetically (case-insensitive), optionally truncates to <paramref name="take"/>
    /// results, and projects each into an <see cref="AdminWidgetValue"/>. Shared by
    /// <see cref="GetAvailableValues"/> implementations whose values are plain, already-distinct
    /// names (<see cref="GenreWidget"/>, <see cref="StudioWidget"/>,
    /// <see cref="TagWidget"/>). <see cref="YearWidget"/> does not use this helper: its
    /// filter (prefix match, case-sensitive) and sort (numeric, descending) are intentionally
    /// different, not a copy-paste duplicate of this pattern.
    /// </summary>
    /// <param name="names">The candidate value names.</param>
    /// <param name="search">Optional case-insensitive substring filter.</param>
    /// <param name="take">Optional maximum number of results to return, applied after filtering/sorting.</param>
    /// <returns>The filtered, sorted, and projected list of values.</returns>
    protected static IReadOnlyList<AdminWidgetValue> FilterAndProject(
        IEnumerable<string> names,
        string? search,
        int? take = null)
    {
        var filtered = names
            .Where(name => string.IsNullOrEmpty(search)
                || name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        var limited = take.HasValue ? filtered.Take(take.Value) : filtered;

        return limited
            .Select(name => new AdminWidgetValue(name, name))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        // Resolve the instance value: prefer AdditionalData (set from ExtraParams["value"] by
        // WidgetLayoutResolver.BuildInstanceConfig), fall back to ExtraParams["value"] for direct calls.
        string? value = payload.AdditionalData;
        if (string.IsNullOrEmpty(value) && payload.ExtraParams is not null)
        {
            payload.ExtraParams.TryGetValue(ValueParam, out value);
        }

        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        User? user = UserManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var dtoOptions = WidgetDtoOptions.Standard();
        var query = LibrarySectionQuery.Create(user, dtoOptions, IncludeItemTypes);

        if (!ApplyFilter(query, value))
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        return Task.FromResult(ItemFetcher.Fetch(WidgetType, user, query, dtoOptions, payload));
    }

    /// <inheritdoc/>
    public WidgetDescriptor GetDescriptor() => new()
    {
        WidgetType = WidgetType,
        DisplayName = DefaultDisplayName,
        Category = Category,
        ViewMode = DefaultViewMode,
        Route = Route,
        MinItems = DefaultMinItems
    };

    /// <summary>
    /// Gets the "see all" target of the section (a <see cref="WidgetRoutes"/> value, the row's value
    /// completing it), or null when Jellyfin has no page listing the same items.
    /// </summary>
    protected virtual string? Route => null;
}
