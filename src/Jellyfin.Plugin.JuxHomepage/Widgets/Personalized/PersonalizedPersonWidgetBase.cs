using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Localization;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Abstract base class for person-based personalized widgets (favorite actor, favorite director).
/// Filters items by person name and person type, mirroring the proven admin
/// <c>PersonWidgetBase</c> pattern (<see cref="InternalItemsQuery.Person"/> +
/// <see cref="InternalItemsQuery.PersonTypes"/>).
/// </summary>
public abstract class PersonalizedPersonWidgetBase : PersonalizedWidgetBase
{
    /// <inheritdoc/>
    protected PersonalizedPersonWidgetBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ScoringService scoringService,
        ILocalizationService localizationService,
        WidgetItemFetcher? itemFetcher = null)
        : base(userManager, libraryManager, dtoService, scoringService, localizationService, itemFetcher)
    {
    }

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <summary>
    /// Gets the Jellyfin person type string used to filter the item query.
    /// Typically "Actor" or "Director".
    /// </summary>
    protected abstract string PersonType { get; }

    /// <inheritdoc/>
    protected override void ApplyFilter(InternalItemsQuery query, string value, User user)
    {
        query.Person = value;
        query.PersonTypes = [PersonType];
    }
}
