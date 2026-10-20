using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Localization;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Abstract base class for JellyUX Homepage personalized widgets.
/// <para>
/// Personalized widgets display items filtered by a value derived from the requesting user's
/// watch history via <see cref="ScoringService"/> (e.g. a favorite genre, actor, or director),
/// rather than a value chosen by an administrator.
/// </para>
/// <para>
/// Each <see cref="WidgetConfig"/> row is independent, exactly like the other widget categories --
/// "1 row = 1 section". Since a personalized value can't be picked by the administrator, each row
/// is instead assigned a <b>rank</b> (1-indexed) among the rows sharing its <c>WidgetType</c>,
/// computed by <see cref="Jellyfin.Plugin.JuxHomepage.Widgets.WidgetLayoutResolver"/> from the rows'
/// own <c>Order</c>. <see cref="Resolve"/> resolves that row's rank against
/// <see cref="GetScoredValues"/>: if the user has a scored value at that rank, it resolves to
/// exactly one instance carrying it; otherwise it resolves to null, so the row is naturally excluded
/// from the layout (TODO_V2.md Phase 8 -- replaces the previous model where a single row fanned out
/// into up to <c>MaxInstances</c> instances via <c>MemberwiseClone</c> in a loop, one per scored
/// value).
/// </para>
/// </summary>
public abstract class PersonalizedWidgetBase : IWidget
{
    private string? _value;
    private string? _displayName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonalizedWidgetBase"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="scoringService">User preference scoring service.</param>
    /// <param name="localizationService">Widget display-name translation service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    protected PersonalizedWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ScoringService scoringService,
        ILocalizationService localizationService,
        WidgetItemFetcher? itemFetcher = null)
    {
        UserManager = userManager;
        LibraryManager = libraryManager;
        DtoService = dtoService;
        ScoringService = scoringService;
        LocalizationService = localizationService;
        ItemFetcher = itemFetcher ?? new WidgetItemFetcher(libraryManager, dtoService);
    }

    /// <summary>Gets the Jellyfin user manager.</summary>
    protected IUserManager UserManager { get; }

    /// <summary>Gets the Jellyfin library manager.</summary>
    protected ILibraryManager LibraryManager { get; }

    /// <summary>Gets the Jellyfin DTO projection service.</summary>
    protected IDtoService DtoService { get; }

    /// <summary>Gets the user preference scoring service.</summary>
    protected ScoringService ScoringService { get; }

    /// <summary>Gets the widget display-name translation service.</summary>
    protected ILocalizationService LocalizationService { get; }

    /// <summary>Gets the runner of the section query in its configured order (TODO_V4.md 8.1).</summary>
    protected WidgetItemFetcher ItemFetcher { get; }

    /// <summary>
    /// Gets a value indicating whether the row's configured sort applies. False for a widget whose
    /// <see cref="ApplyFilter"/> sets an order of its own.
    /// </summary>
    protected virtual bool UsesConfiguredSort => true;

    /// <inheritdoc/>
    public abstract string WidgetType { get; }

    /// <inheritdoc/>
    public abstract string DefaultDisplayName { get; }

    /// <summary>Gets the default view mode identifier for this widget's cards.</summary>
    public abstract string DefaultViewMode { get; }

    /// <inheritdoc/>
    public virtual int DefaultMinItems => 4;

    /// <inheritdoc/>
    public virtual WidgetCategory Category => WidgetCategory.Personalized;

    /// <summary>
    /// Gets the item types to include in the recommendation query. Never null, unlike
    /// <see cref="Jellyfin.Plugin.JuxHomepage.Widgets.Admin.AdminWidgetBase.IncludeItemTypes"/> --
    /// a personalized recommendation always needs a concrete type constraint (there is no
    /// "recommend across every type" case here, as there is for an admin-chosen filter value).
    /// </summary>
    protected virtual BaseItemKind[] IncludeItemTypes => [BaseItemKind.Movie, BaseItemKind.Series];

    /// <summary>
    /// Returns the user's scored values for this widget's preference dimension (e.g. top genres),
    /// most relevant first. Used by <see cref="Resolve"/> to resolve the value at a given
    /// rank (the <paramref name="count"/>-th element, 1-indexed).
    /// </summary>
    /// <param name="userId">The requesting user's identifier.</param>
    /// <param name="count">The maximum number of scored values to return.</param>
    /// <param name="config">
    /// The row's configuration, exposing <see cref="WidgetInstanceConfig.ExtraParams"/> for widgets
    /// that read row-level admin settings (e.g. <see cref="BecauseYouWatchedWidget"/>'s scope).
    /// </param>
    /// <returns>Up to <paramref name="count"/> scored values.</returns>
    protected abstract IReadOnlyList<ScoredValue> GetScoredValues(Guid userId, int count, WidgetInstanceConfig config);

    /// <summary>
    /// Formats the section display name for a scored value's label (e.g. "More Action"), translated
    /// for <paramref name="lang"/>.
    /// </summary>
    /// <param name="label">The scored value's human-readable label.</param>
    /// <param name="lang">The language to translate the format template into.</param>
    /// <returns>The display name shown for this instance's section.</returns>
    protected abstract string FormatDisplayName(string label, string? lang);

    /// <summary>
    /// Applies the widget-specific filter to the query using the resolved scored value.
    /// </summary>
    /// <param name="query">The query to modify.</param>
    /// <param name="value">The scored value to filter by.</param>
    /// <param name="user">The requesting Jellyfin user.</param>
    protected abstract void ApplyFilter(InternalItemsQuery query, string value, User user);

    /// <remarks>
    /// For this category, <paramref name="rank"/> is the row's 1-indexed rank among rows sharing
    /// this widget's <c>WidgetType</c> (see the class-level remarks). Resolves to exactly one
    /// instance if the user has a scored value at that rank, otherwise null. A single
    /// <c>MemberwiseClone()</c> per call keeps this safe under concurrent requests --
    /// <see cref="PersonalizedWidgetBase"/> instances are DI singletons shared across every row/user,
    /// and <see cref="Widgets.WidgetLayoutResolver.BuildDescriptors(Guid, string?, CancellationToken)"/> calls this from up to four
    /// thread-pool workers at once (one per configured row, PERF-02) for the very same widget
    /// instance; each call produces its own private clone rather than mutating <see langword="this"/>
    /// directly, so concurrent calls never race on shared state
    /// (<c>FavoriteGenre_Resolve_ManyConcurrentCalls_EachCloneKeepsItsRankValue</c>).
    /// </remarks>
    /// <inheritdoc/>
    public IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank)
    {
        var scoredValues = GetScoredValues(userId, rank, config);
        if (scoredValues.Count < rank)
        {
            return null;
        }

        var scored = scoredValues[rank - 1];
        var clone = (PersonalizedWidgetBase)MemberwiseClone();
        clone._value = scored.Value;
        clone._displayName = FormatDisplayName(scored.Label, config.Lang);
        return clone;
    }

    /// <inheritdoc/>
    public Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        var value = payload.AdditionalData;
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
        query.StartIndex = payload.StartIndex;
        query.Limit = payload.Limit;

        if (ExcludeWatched(payload))
        {
            query.IsPlayed = false;
        }

        ApplyFilter(query, value, user);
        if (UsesConfiguredSort)
        {
            return Task.FromResult(ItemFetcher.Fetch(WidgetType, user, query, dtoOptions, payload));
        }

        QueryResult<BaseItem> result = LibraryManager.GetItemsResult(query);
        return Task.FromResult(WidgetResult.Project(DtoService, result.Items, dtoOptions, user));
    }

    /// <inheritdoc/>
    public WidgetDescriptor GetDescriptor() => new()
    {
        WidgetType = WidgetType,
        DisplayName = _displayName ?? DefaultDisplayName,
        Category = Category,
        ViewMode = DefaultViewMode,
        AdditionalData = _value,
        Route = Route,
        MinItems = DefaultMinItems
    };

    /// <summary>
    /// Gets the "see all" target of the section (a <see cref="WidgetRoutes"/> value, the scored value
    /// completing it), or null when Jellyfin has no page listing the same items.
    /// </summary>
    protected virtual string? Route => null;

    /// <summary>
    /// Reads the row-level "exclude already watched" setting from <see cref="WidgetPayload.ExtraParams"/>.
    /// Defaults to <see langword="true"/> when not explicitly set to "false".
    /// </summary>
    /// <param name="payload">The request payload.</param>
    /// <returns><see langword="true"/> unless the administrator explicitly disabled the exclusion.</returns>
    private static bool ExcludeWatched(WidgetPayload payload)
    {
        if (payload.ExtraParams is not null &&
            payload.ExtraParams.TryGetValue("excludeWatched", out var raw) &&
            bool.TryParse(raw, out var value))
        {
            return value;
        }

        return true;
    }
}
