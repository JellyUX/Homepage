using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Displays items belonging to a specific genre chosen by the administrator.
/// Multiple instances can be created, each pinned to a different genre.
/// </summary>
public sealed class GenreWidget : AdminWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenreWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    public GenreWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService)
        : base(userManager, libraryManager, dtoService)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.admin.genre";

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Genre";

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    protected override bool ApplyFilter(InternalItemsQuery query, string value)
    {
        query.Genres = [value];
        return true;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<AdminWidgetValue> GetAvailableValues(User user, string? search)
    {
        var result = LibraryManager.GetGenres(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true
        });

        return FilterAndProject(result.Items.Select(x => x.Item1.Name), search);
    }
}
