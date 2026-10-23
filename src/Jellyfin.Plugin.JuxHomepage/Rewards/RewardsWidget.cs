using Jellyfin.Plugin.JuxHomepage.Rewards.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Rewards;

/// <summary>
/// Displays films from a Wikidata-backed award query (ceremony/category/year, admin-configured) that
/// are present in the local library. Like <see cref="DiscoverMoviesWidget"/>, each instance has its
/// own filter and its own cache, identified by <see cref="WidgetPayload.AdditionalData"/> (the config
/// row's <c>ExtraParams["value"]</c>).
/// </summary>
public sealed class RewardsWidget : ConnectedWidgetBase<RewardsWinner>
{
    /// <summary>The <c>ExtraParams</c> key of the instance id of the section.</summary>
    public const string ValueParam = WidgetConfig.ValueParam;

    /// <summary>The <c>ExtraParams</c> key of the Wikidata Q-id of the ceremony.</summary>
    public const string CeremonyQidParam = "ceremonyQid";

    /// <summary>The <c>ExtraParams</c> key of the Wikidata Q-id of the award category.</summary>
    public const string CategoryQidParam = "categoryQid";

    /// <summary>The <c>ExtraParams</c> key of the ceremony year.</summary>
    public const string YearParam = "year";

    private readonly IRewardsCacheService _cacheService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RewardsWidget"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="dtoService">Jellyfin DTO projection service.</param>
    /// <param name="cacheService">Rewards disk cache service.</param>
    /// <param name="logger">Logger.</param>
    public RewardsWidget(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IRewardsCacheService cacheService,
        ILogger<RewardsWidget> logger)
        : base(userManager, libraryManager, dtoService, logger)
    {
        _cacheService = cacheService;
    }

    /// <inheritdoc/>
    public override string WidgetType => RewardsWidgetTypes.Rewards;

    /// <summary>Gets a new instance id; the admin picks the ceremony or category.</summary>
    public override IReadOnlyList<WidgetExtraParam> DefaultExtraParams =>
        [new WidgetExtraParam { Key = ValueParam, Value = string.Empty }];

    /// <inheritdoc/>
    public override string DefaultViewMode => WidgetViewMode.Portrait;

    /// <inheritdoc/>
    protected override IReadOnlyList<RewardsWinner> GetCachedItems(WidgetPayload payload) =>
        payload.AdditionalData is null ? [] : _cacheService.GetRewards(payload.AdditionalData);
}
