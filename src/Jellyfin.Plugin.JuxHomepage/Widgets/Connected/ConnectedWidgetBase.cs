using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Connected;

/// <summary>
/// Abstract base class for JellyUX Homepage connected widgets.
/// <para>
/// Connected widgets display external data (TMDb trending/on the air/top rated/now playing/discover,
/// or Wikidata-backed Rewards) that has been cross-referenced against the local Jellyfin library by a
/// disk cache service (<see cref="ITMDbCacheService"/> or <see cref="Rewards.IRewardsCacheService"/>).
/// Only cached entries that carry a non-null <see cref="IExternalCacheItem.LibraryItemId"/> -- i.e.
/// items the user actually owns -- are ever displayed; a cached entry with no local match is not a
/// playable Jellyfin item and is silently excluded.
/// </para>
/// <para>
/// Deliberately does not itself depend on any specific cache service type: each concrete widget holds
/// its own cache service field and implements <see cref="GetCachedItems"/> against it, so this base
/// class can be reused across unrelated external data providers without coupling them to each other
/// (see TODO_V2.md Phase 14, which added the second provider, Wikidata).
/// </para>
/// <para>
/// Single-instance, like <see cref="Native.NativeWidgetBase"/>: there is exactly one "Trending
/// Movies" section, not one per scored/chosen value.
/// </para>
/// </summary>
/// <typeparam name="T">The cached item type (<see cref="TMDbMovie"/>, <see cref="TMDbShow"/>, or <see cref="Rewards.Models.RewardsWinner"/>).</typeparam>
public abstract class ConnectedWidgetBase<T> : IWidget
    where T : IExternalCacheItem
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectedWidgetBase{T}"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="logger">Logger.</param>
    protected ConnectedWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ILogger logger)
    {
        UserManager = userManager;
        LibraryManager = libraryManager;
        DtoService = dtoService;
        _logger = logger;
    }

    /// <summary>Gets the Jellyfin user manager.</summary>
    protected IUserManager UserManager { get; }

    /// <summary>Gets the Jellyfin library manager.</summary>
    protected ILibraryManager LibraryManager { get; }

    /// <summary>Gets the Jellyfin DTO projection service.</summary>
    protected IDtoService DtoService { get; }

    /// <inheritdoc/>
    public abstract string WidgetType { get; }

    /// <inheritdoc/>
    public abstract string DefaultDisplayName { get; }

    /// <summary>Gets the default view mode identifier for this widget's cards.</summary>
    public abstract string DefaultViewMode { get; }

    /// <inheritdoc/>
    public virtual int DefaultMinItems => 4;

    /// <summary>Gets the optional front-end route for browsing all items of this type.</summary>
    public virtual string? Route => null;

    /// <inheritdoc/>
    public virtual WidgetCategory Category => WidgetCategory.Connected;

    /// <summary>
    /// Returns the currently cached external items for this widget's data set (e.g. trending movies,
    /// or a Rewards instance's award winners).
    /// </summary>
    /// <param name="payload">
    /// The request payload. Fixed single-instance widgets ignore it; multi-instance widgets (e.g.
    /// Discover) read <see cref="WidgetPayload.AdditionalData"/> to identify which instance's cache
    /// to read.
    /// </param>
    /// <returns>The cached items, or an empty list if no refresh has completed yet.</returns>
    protected abstract IReadOnlyList<T> GetCachedItems(WidgetPayload payload);

    /// <inheritdoc/>
    public IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank) => this;

    /// <inheritdoc/>
    public Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken)
    {
        User? user = UserManager.GetUserById(payload.UserId);
        if (user is null)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        var libraryIds = GetCachedItems(payload)
            .Where(i => i.LibraryItemId.HasValue)
            .Select(i => i.LibraryItemId!.Value)
            .ToList();

        if (libraryIds.Count == 0)
        {
            return Task.FromResult(WidgetResult.Empty);
        }

        // Every cached id is checked against the user's rights before paginating, so the page and the
        // total only ever reflect items this user may open (the cache itself is shared by all users).
        var visibleItems = UserVisibleItems.Resolve(LibraryManager, user, libraryIds, out var unresolvedCount);

        if (unresolvedCount > 0)
        {
            _logger.LogDebug(
                "{Count} cached item(s) for widget '{WidgetType}' no longer resolve to a local library item (likely removed since the last refresh).",
                unresolvedCount,
                WidgetType);
        }

        // In-memory list in the TMDb ranking order, so pagination is a plain Skip/Take -- the same
        // pattern MyMediaWidget uses over its in-memory library folder list.
        var pageItems = visibleItems.Skip(payload.StartIndex).Take(payload.Limit).ToList();
        return Task.FromResult(WidgetResult.Project(DtoService, pageItems, WidgetDtoOptions.Standard(), user, visibleItems.Count));
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
}
