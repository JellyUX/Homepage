using Jellyfin.Plugin.JuxHomepage.Localization;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Displays movies directed by the requesting user's favorite directors, derived from their watch
/// history. Each configured row resolves to at most one top-scored director, by rank (see
/// <see cref="ScoringService.GetTopDirectors"/> and <see cref="PersonalizedWidgetBase"/>).
/// </summary>
public sealed class FavoriteDirectorWidget : PersonalizedPersonWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FavoriteDirectorWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="scoringService">User preference scoring service.</param>
    /// <param name="localizationService">Widget display-name translation service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public FavoriteDirectorWidget(
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
    public override string WidgetType => "jux.personalized.favorite-director";

    /// <inheritdoc/>
    protected override string PersonType => "Director";

    /// <inheritdoc/>
    protected override IReadOnlyList<ScoredValue> GetScoredValues(Guid userId, int count, WidgetInstanceConfig config) =>
        ScoringService.GetTopDirectors(userId, count);

    /// <inheritdoc/>
    protected override string FormatDisplayName(string label, string? lang) =>
        LocalizationService.Translate("jux.personalized.favorite-director.format", lang, label);
}
