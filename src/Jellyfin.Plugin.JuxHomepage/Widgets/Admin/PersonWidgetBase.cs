using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Admin;

/// <summary>
/// Abstract base class for person-based admin widgets (Actor, Director).
/// Filters items by person name and person type using <see cref="ILibraryManager.GetPeopleNames"/>.
/// </summary>
public abstract class PersonWidgetBase : AdminWidgetBase
{
    /// <inheritdoc/>
    protected PersonWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override int DefaultMinItems => 4;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <summary>
    /// Gets the Jellyfin person type string used to filter the person list and item queries.
    /// Typically "Actor" or "Director".
    /// </summary>
    protected abstract string PersonType { get; }

    /// <inheritdoc/>
    protected override bool ApplyFilter(InternalItemsQuery query, string value)
    {
        query.Person = value;
        query.PersonTypes = [PersonType];
        return true;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<AdminWidgetValue> GetAvailableValues(User user, string? search)
    {
        var names = LibraryManager.GetPeopleNames(new InternalPeopleQuery([PersonType], [])
        {
            NameContains = string.IsNullOrEmpty(search) ? null : search,
            Limit = string.IsNullOrEmpty(search) ? 100 : 50,
            User = user
        });

        return names
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new AdminWidgetValue(n, n))
            .ToList()
            .AsReadOnly();
    }
}
