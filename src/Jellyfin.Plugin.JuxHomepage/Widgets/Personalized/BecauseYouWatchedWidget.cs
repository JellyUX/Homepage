using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JuxHomepage.Localization;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;

/// <summary>
/// Displays items related to a movie or series the requesting user recently watched, sharing at
/// least one genre with the reference item. Each configured row resolves to at most one recently
/// watched reference item, by rank (see <see cref="ScoringService.GetRecentlyWatched"/> and
/// <see cref="PersonalizedWidgetBase"/>).
/// </summary>
public sealed class BecauseYouWatchedWidget : PersonalizedWidgetBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BecauseYouWatchedWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="scoringService">User preference scoring service.</param>
    /// <param name="localizationService">Widget display-name translation service.</param>
    public BecauseYouWatchedWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        ScoringService scoringService,
        ILocalizationService localizationService)
        : base(userManager, libraryManager, dtoService, scoringService, localizationService)
    {
    }

    /// <inheritdoc/>
    public override string WidgetType => "jux.personalized.because-you-watched";

    /// <inheritdoc/>
    /// <remarks>The recommendations keep their random order (<see cref="ApplyFilter"/>).</remarks>
    protected override bool UsesConfiguredSort => false;

    /// <inheritdoc/>
    public override string DefaultDisplayName => "Because You Watched";

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    protected override IReadOnlyList<ScoredValue> GetScoredValues(Guid userId, int count, WidgetInstanceConfig config) =>
        ScoringService.GetRecentlyWatched(userId, count, ResolveScope(config));

    /// <inheritdoc/>
    protected override string FormatDisplayName(string label, string? lang) =>
        LocalizationService.Translate("jux.personalized.because-you-watched.format", lang, label);

    /// <inheritdoc/>
    protected override void ApplyFilter(InternalItemsQuery query, string value, User user)
    {
        if (!Guid.TryParse(value, out var referenceId))
        {
            return;
        }

        var reference = LibraryManager.GetItemById(referenceId);
        if (reference is null)
        {
            return;
        }

        // The recommendation always matches the reference item's own type -- a series reference
        // recommends other series, a movie reference recommends other movies -- regardless of the
        // row's "scope" setting, which only narrows which recently watched item becomes the reference
        // in the first place (see BecauseYouWatchedScope).
        query.IncludeItemTypes = [reference is Series ? BaseItemKind.Series : BaseItemKind.Movie];

        // Recommend items sharing at least one genre with the reference item, in random order,
        // always excluding the reference item itself.
        query.Genres = reference.Genres;
        query.ExcludeItemIds = [referenceId];
        query.OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)];
    }

    /// <summary>
    /// Reads the row-level "scope" admin setting from <see cref="WidgetInstanceConfig.ExtraParams"/>,
    /// restricting which recently watched item type is eligible to become this row's reference.
    /// Defaults to <see cref="BecauseYouWatchedScope.Both"/> when unset or unrecognized.
    /// </summary>
    private static BecauseYouWatchedScope ResolveScope(WidgetInstanceConfig config)
    {
        if (config.ExtraParams is not null && config.ExtraParams.TryGetValue("scope", out var raw))
        {
            return raw switch
            {
                "movies" => BecauseYouWatchedScope.Movies,
                "series" => BecauseYouWatchedScope.Series,
                _ => BecauseYouWatchedScope.Both
            };
        }

        return BecauseYouWatchedScope.Both;
    }
}
