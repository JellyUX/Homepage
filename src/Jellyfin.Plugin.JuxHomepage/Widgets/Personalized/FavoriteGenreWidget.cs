using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Localization;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Displays items from the requesting user's favorite genres, derived from their watch history.
/// Each configured row resolves to at most one top-scored genre, by rank (see
/// <see cref="ScoringService.GetTopGenres"/> and <see cref="PersonalizedWidgetBase"/>).
/// </summary>
public sealed class FavoriteGenreWidget : PersonalizedWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FavoriteGenreWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="scoringService">User preference scoring service.</param>
    /// <param name="localizationService">Widget display-name translation service.</param>
    /// <param name="itemFetcher">Runs the section query in its configured order; a private one when null (tests).</param>
    public FavoriteGenreWidget(
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
    public override string WidgetType => "jux.personalized.favorite-genre";

    /// <inheritdoc/>
    protected override string? Route => WidgetRoutes.Genre;

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    protected override IReadOnlyList<ScoredValue> GetScoredValues(Guid userId, int count, WidgetInstanceConfig config) =>
        ScoringService.GetTopGenres(userId, count);

    /// <inheritdoc/>
    protected override string FormatDisplayName(string label, string? lang) =>
        LocalizationService.Translate("jux.personalized.favorite-genre.format", lang, label);

    /// <inheritdoc/>
    protected override void ApplyFilter(InternalItemsQuery query, string value, User user)
    {
        query.Genres = [value];
    }
}
