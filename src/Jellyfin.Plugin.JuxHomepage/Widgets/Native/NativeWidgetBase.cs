using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Native;

/// <summary>
/// Abstract base class for all JellyUX Homepage native widgets.
/// Native widgets query Jellyfin's local library via the standard Jellyfin service interfaces.
/// Subclasses inject their required Jellyfin services and implement <see cref="GetItemsAsync"/>.
/// </summary>
public abstract class NativeWidgetBase : IWidget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeWidgetBase"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    protected NativeWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService)
    {
        UserManager = userManager;
        LibraryManager = libraryManager;
        DtoService = dtoService;
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
    public string DefaultDisplayName => WidgetNames.English(WidgetType);

    /// <inheritdoc/>
    public abstract int DefaultMinItems { get; }

    /// <inheritdoc/>
    public IReadOnlyList<WidgetExtraParam> DefaultExtraParams => [];

    /// <summary>Gets the default view mode identifier for this widget's cards.</summary>
    public abstract string DefaultViewMode { get; }

    /// <summary>Gets the optional front-end route for browsing all items of this type.</summary>
    public abstract string? Route { get; }

    /// <inheritdoc/>
    public virtual WidgetCategory Category => WidgetCategory.Native;

    /// <summary>
    /// Gets a value indicating whether the client reads this section from a native Jellyfin route
    /// (<c>NATIVE_ROUTES</c> of the client script) instead of the Section endpoint: true for a native
    /// section unless it overrides it.
    /// </summary>
    protected virtual bool ReadNatively => true;

    /// <summary>
    /// Gets a value indicating whether a row of this widget may show as a banner: never for a section
    /// read from a native route, which never reaches the Section endpoint (DEC-P11-4).
    /// </summary>
    protected virtual bool CanFeature => !ReadNatively;

    /// <inheritdoc/>
    public IWidget? Resolve(Guid userId, WidgetInstanceConfig config, int rank) => this;

    /// <inheritdoc/>
    public abstract Task<WidgetResult> GetItemsAsync(WidgetPayload payload, CancellationToken cancellationToken);

    /// <inheritdoc/>
    public virtual WidgetDescriptor GetDescriptor() => new()
    {
        WidgetType = WidgetType,
        DisplayName = DefaultDisplayName,
        Category = Category,
        ViewMode = DefaultViewMode,
        Route = Route,
        MinItems = DefaultMinItems,
        ReadNatively = ReadNatively,
        CanFeature = CanFeature ? null : false
    };
}
