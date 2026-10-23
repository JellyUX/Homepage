using System.Globalization;
using System.Reflection;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.Library.Models;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Rewards;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Admin;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// Serves JellyUX Homepage web resources, plugin metadata, and widget data.
/// Route: /JuxHomepage
/// </summary>
[ApiController]
[Route("[controller]")]
public class JuxHomepageController : ControllerBase
{
    private static readonly Assembly PluginAssembly = typeof(JuxHomepageController).Assembly;

    private static readonly string WebResourcePrefix = $"{typeof(Plugin).Namespace}.Web.";

    private readonly IWidgetRegistry _registry;
    private readonly WidgetService _widgetService;
    private readonly IUserManager _userManager;
    private readonly ITMDbCacheService _tmdbCacheService;
    private readonly ITMDbApiClient _tmdbApiClient;
    private readonly IRewardsCacheService _rewardsCacheService;
    private readonly IWikidataApiClient _wikidataApiClient;
    private readonly ILocalizationService _localizationService;
    private readonly IWatchlistService _watchlistService;
    private readonly ISeriesProgressViewService _seriesProgressViewService;
    private readonly IMovieHistoryViewService _movieHistoryViewService;
    private readonly IStatisticsService _statisticsService;
    private readonly ICollectionsIndexCacheService _collectionsIndexCacheService;
    private readonly IAuthorizationContext _authContext;
    private readonly HomeLanguageMemory _homeLanguages;
    private readonly ILogger<JuxHomepageController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JuxHomepageController"/> class.
    /// </summary>
    /// <param name="registry">The widget registry.</param>
    /// <param name="widgetService">The widget orchestration service.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="tmdbCacheService">TMDb disk cache service.</param>
    /// <param name="tmdbApiClient">TMDb HTTP API client.</param>
    /// <param name="rewardsCacheService">Rewards disk cache service.</param>
    /// <param name="wikidataApiClient">Wikidata HTTP API client.</param>
    /// <param name="localizationService">Widget/admin-UI translation service.</param>
    /// <param name="watchlistService">Watchlist query service.</param>
    /// <param name="seriesProgressViewService">Series Progress view service.</param>
    /// <param name="movieHistoryViewService">Movie History view service.</param>
    /// <param name="statisticsService">Watch-history statistics service.</param>
    /// <param name="collectionsIndexCacheService">Collections reverse index cache service.</param>
    /// <param name="authContext">Jellyfin request authorization context.</param>
    /// <param name="homeLanguages">Remembers the language of each user's last home screen (DEC-P07-10).</param>
    /// <param name="logger">Logger.</param>
    public JuxHomepageController(
        IWidgetRegistry registry,
        WidgetService widgetService,
        IUserManager userManager,
        ITMDbCacheService tmdbCacheService,
        ITMDbApiClient tmdbApiClient,
        IRewardsCacheService rewardsCacheService,
        IWikidataApiClient wikidataApiClient,
        ILocalizationService localizationService,
        IWatchlistService watchlistService,
        ISeriesProgressViewService seriesProgressViewService,
        IMovieHistoryViewService movieHistoryViewService,
        IStatisticsService statisticsService,
        ICollectionsIndexCacheService collectionsIndexCacheService,
        IAuthorizationContext authContext,
        HomeLanguageMemory homeLanguages,
        ILogger<JuxHomepageController> logger)
    {
        _registry = registry;
        _widgetService = widgetService;
        _userManager = userManager;
        _tmdbCacheService = tmdbCacheService;
        _tmdbApiClient = tmdbApiClient;
        _rewardsCacheService = rewardsCacheService;
        _wikidataApiClient = wikidataApiClient;
        _localizationService = localizationService;
        _watchlistService = watchlistService;
        _seriesProgressViewService = seriesProgressViewService;
        _movieHistoryViewService = movieHistoryViewService;
        _statisticsService = statisticsService;
        _collectionsIndexCacheService = collectionsIndexCacheService;
        _authContext = authContext;
        _homeLanguages = homeLanguages;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Web resources (anonymous)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serves a JellyUX client asset (script or stylesheet) embedded in the plugin.
    /// Anonymous - loaded by the tags injected into index.html and by the configuration page, and a
    /// browser-loaded &lt;script&gt; or &lt;link&gt; tag carries no Jellyfin auth token. Only the
    /// assets named by <see cref="TransformationPatches.ServedAssetName"/> are served.
    /// </summary>
    /// <param name="fileName">The asset file name, e.g. <c>jux-homepage.js</c>.</param>
    /// <returns>The file contents, or 404 for a name outside the served assets.</returns>
    [HttpGet("{fileName}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetAsset([FromRoute] string fileName)
    {
        var name = TransformationPatches.ServedAssetName(fileName);
        if (name == TransformationPatches.ClientI18nAsset)
        {
            SetCacheHeaders(Request, Response);
            return Content(_localizationService.GetClientScript(), "application/javascript");
        }

        var stream = name is null ? null : PluginAssembly.GetManifestResourceStream(WebResourcePrefix + name);
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, name!.EndsWith(".css", StringComparison.Ordinal) ? "text/css" : "application/javascript");
    }

    /// <summary>
    /// Returns the flat translation dictionary for a language, for the admin config page and the
    /// home screen script to translate their own UI/section titles client- or server-side.
    /// Anonymous - translation strings are not sensitive, same as the JS/CSS resources above.
    /// </summary>
    /// <param name="lang">The requested language code (e.g. "fr"), or null to use English.</param>
    /// <returns>The merged key-to-string dictionary for the requested language.</returns>
    [HttpGet("Localizations")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyDictionary<string, string>> GetLocalizations([FromQuery] string? lang = null)
    {
        return Ok(_localizationService.GetDictionary(lang));
    }

    // -------------------------------------------------------------------------
    // Plugin metadata
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the integration warning codes the admin page shows in its banner (missing
    /// FileTransformation, Custom Tabs installed), computed on each call.
    /// </summary>
    /// <param name="coexistenceWarnings">The warning source.</param>
    /// <returns>The warning codes, empty when JellyUX can run normally.</returns>
    [HttpGet("Admin/Warnings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetAdminWarnings([FromServices] CoexistenceWarnings coexistenceWarnings)
    {
        return Ok(coexistenceWarnings.Get());
    }

    /// <summary>
    /// Restores the saved copy of the configuration the admin page offers after a reset (FIAB-07): it goes
    /// through the plugin's save, so it is validated and becomes the new copy.
    /// </summary>
    /// <param name="backup">The saved copy.</param>
    /// <returns>No content; 404 without a readable copy; 400 when the copy fails validation.</returns>
    [HttpPost("Admin/RestoreConfiguration")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RestoreConfiguration([FromServices] ConfigurationBackup backup)
    {
        var plugin = Plugin.Instance;
        return Restore(backup, plugin is null ? null : plugin.UpdateConfiguration);
    }

    /// <summary>
    /// Restores the copy through <paramref name="save"/> and maps the outcome to the HTTP answer.
    /// </summary>
    /// <param name="backup">The saved copy.</param>
    /// <param name="save">The plugin's save, or null when the plugin is not loaded.</param>
    /// <returns>No content; 404 without a plugin or a readable copy; 400 when the copy fails validation.</returns>
    internal static ActionResult Restore(ConfigurationBackup backup, Action<PluginConfiguration>? save)
    {
        if (save is null)
        {
            return new NotFoundResult();
        }

        try
        {
            return backup.Restore(save) ? new NoContentResult() : new NotFoundResult();
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(ex.Message);
        }
    }

    /// <summary>
    /// Returns the patch state the startup dry run found (FIAB-04). The client's stale chunk guard asks
    /// for it before reloading the page, and reloads only when the server patches the home chunk.
    /// </summary>
    /// <param name="patchStatus">The patch state.</param>
    /// <returns>The loadSections state and whether the tab anchor was found.</returns>
    [HttpGet("PatchStatus")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PatchStatusView> GetPatchStatus([FromServices] PatchStatus patchStatus)
    {
        return Ok(new PatchStatusView(patchStatus.LoadSections.ToString(), patchStatus.TabsAnchor));
    }

    // -------------------------------------------------------------------------
    // Widget layout (authenticated user)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the ordered list of widget descriptors for the user's home screen.
    /// Widgets that return fewer items than their MinItems threshold are excluded.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="page">Zero-based page index (20 descriptors per page).</param>
    /// <param name="lang">
    /// The requested language code (e.g. "fr"), sourced client-side from
    /// <c>document.documentElement.lang</c>. Null defaults to English.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SectionsPage"/>: the page's descriptors and whether a later page exists.</returns>
    [HttpGet("Sections")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SectionsPage>> GetSections(
        [FromQuery] Guid userId,
        [FromQuery] int page = 0,
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedUserId, isCaller) = await ResolveRequestUserAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        // Off globally or for this user alone (TODO_V4.md 8.3, F-36): Jellyfin renders the home screen.
        if (!_widgetService.IsEnabledFor(userId))
        {
            return Ok(new SectionsPage([], HasMore: false, Enabled: false));
        }

        var language = _localizationService.NormalizeLanguage(lang);
        // Only the user's own home screen tells their language, not an administrator reading it.
        if (isCaller)
        {
            _homeLanguages.Remember(userId, language);
        }

        var sectionsPage = await _widgetService
            .GetWidgetsForUser(userId, page, language, cancellationToken)
            .ConfigureAwait(false);

        return Ok(sectionsPage);
    }

    /// <summary>
    /// Returns items for a specific widget section.
    /// Called by the front end when a section comes within one screen below the viewport
    /// (test: jux-homepage.test.js "sections loaded near the viewport").
    /// </summary>
    /// <param name="widgetType">The widget type identifier.</param>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="additionalData">Optional instance-specific data (e.g. library ID).</param>
    /// <param name="sectionKey">The section's key from the layout, naming its configuration row (TODO_V4.md 9.3, F-27).</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="WidgetResult"/> with items and total count.</returns>
    [HttpGet("Section/{widgetType}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WidgetResult>> GetSection(
        [FromRoute] string widgetType,
        [FromQuery] Guid userId,
        [FromQuery] string? additionalData = null,
        [FromQuery] string? sectionKey = null,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        var result = await _widgetService
            .GetWidgetItems(userId, widgetType, additionalData, sectionKey, startIndex, limit, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    // Bounds a tab page (TODO_V4.md 5.5, F-21): a negative start reads from the first row, and a
    // limit stays between 1 and WidgetConfig.MaxItemsCeiling, so no request can return the whole
    // library.
    private static (int StartIndex, int Limit) BoundPage(int startIndex, int limit) =>
        (Math.Max(0, startIndex), Math.Clamp(limit, 1, WidgetConfig.MaxItemsCeiling));

    // Reads one tab view (TODO_V4.md 5.5, F-23): a failure is logged with its view and user and
    // answered with an explicit 500, which the tab shows as an error with a retry (5.2), the same
    // protection on every view instead of none.
    private ActionResult<T> ReadView<T>(string view, Guid userId, Func<T> read)
    {
        try
        {
            return Ok(read());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "JellyUX {View} failed for user {UserId}.", view, userId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // -------------------------------------------------------------------------
    // Watchlist (authenticated user)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a filtered, sorted, paginated page of the user's Watchlist items (every item with
    /// <c>UserData.Likes == true</c>). TODO_V3.md Phase 5.1.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="sortBy">One of "SortName", "DateCreated", "PremiereDate", "CommunityRating" (<see cref="WidgetSort"/>). Defaults to "DateCreated".</param>
    /// <param name="sortOrder">"Ascending" or "Descending". Defaults to "Descending".</param>
    /// <param name="includeItemTypes">One of "Movie", "Series", "All". Defaults to "All".</param>
    /// <param name="searchTerm">Name filter (case and accents ignored, TODO_V4.md 8.5); blank keeps every row.</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="WidgetResult"/> with items and total count.</returns>
    [HttpGet("Watchlist/Items")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WidgetResult>> GetWatchlistItems(
        [FromQuery] Guid userId,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        [FromQuery] string? includeItemTypes = null,
        [FromQuery] string? searchTerm = null,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        (startIndex, limit) = BoundPage(startIndex, limit);
        return ReadView("Watchlist", userId, () =>
        {
            var page = _watchlistService.GetItems(userId, sortBy, sortOrder, includeItemTypes, searchTerm, startIndex, limit, includeTotalRecordCount: true, cancellationToken);

            // Projected here, so a failed projection is answered like a failed read.
            _ = page.Items;
            return page;
        });
    }

    /// <summary>
    /// Returns every item id currently in the user's Watchlist, with no image/metadata fields --
    /// a lightweight payload for <c>jux-card-hooks.js</c> to pre-load once per page so each card can
    /// immediately show the correct toggle icon. TODO_V3.md Phase 5.2.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every item id currently liked by this user, and whether JellyUX runs for them.</returns>
    [HttpGet("Watchlist/Ids")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WatchlistIdsResult>> GetWatchlistIds(
        [FromQuery] Guid userId,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        if (!_widgetService.IsEnabledFor(userId))
        {
            return Ok(new WatchlistIdsResult([], Enabled: false));
        }

        return ReadView("Watchlist ids", userId, () => new WatchlistIdsResult(_watchlistService.GetLikedItemIds(userId, cancellationToken), Enabled: true));
    }

    /// <summary>
    /// Returns a page of the user's in-progress series (Series Progress view, TODO_V3.md Phase 6.1),
    /// each hydrated with a poster/name and the watched/total episode counts and last-watched episode.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="sortBy">"SortName" or "LastPlayed" (<see cref="WidgetSort"/>). Defaults to "LastPlayed".</param>
    /// <param name="sortOrder">"Ascending" or "Descending". Defaults to "Descending".</param>
    /// <param name="searchTerm">Name filter (case and accents ignored, TODO_V4.md 8.5); blank keeps every row.</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SeriesProgressResult"/> with items and total count.</returns>
    [HttpGet("SeriesProgress/Items")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SeriesProgressResult>> GetSeriesProgressItems(
        [FromQuery] Guid userId,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        [FromQuery] string? searchTerm = null,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        (startIndex, limit) = BoundPage(startIndex, limit);
        return ReadView("Series Progress", userId, () => _seriesProgressViewService.GetItems(userId, sortBy, sortOrder, searchTerm, startIndex, limit, cancellationToken));
    }

    /// <summary>
    /// Returns a page of the user's watched movies (Movie History view, TODO_V3.md Phase 6.2), each
    /// hydrated with a poster/name and last-played date.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="sortBy">"SortName" or "LastPlayed" (<see cref="WidgetSort"/>). Defaults to "LastPlayed".</param>
    /// <param name="sortOrder">"Ascending" or "Descending". Defaults to "Descending".</param>
    /// <param name="searchTerm">Name filter (case and accents ignored, TODO_V4.md 8.5); blank keeps every row.</param>
    /// <param name="startIndex">Zero-based start index for pagination.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="WidgetResult"/> with items and total count.</returns>
    [HttpGet("MovieHistory/Items")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WidgetResult>> GetMovieHistoryItems(
        [FromQuery] Guid userId,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        [FromQuery] string? searchTerm = null,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        (startIndex, limit) = BoundPage(startIndex, limit);
        return ReadView("Movie History", userId, () => _movieHistoryViewService.GetItems(userId, sortBy, sortOrder, searchTerm, startIndex, limit, cancellationToken));
    }

    /// <summary>
    /// Returns the user's aggregate watch-history counters (Statistics view, TODO_V3.md Phase 6.3),
    /// derived entirely from the Series Progress / Movie History caches -- no new library scan.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <returns>The aggregate counters.</returns>
    [HttpGet("Statistics")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WatchingStatistics>> GetStatistics([FromQuery] Guid userId)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        return ReadView("Statistics", userId, () => _statisticsService.GetStatistics(userId));
    }

    /// <summary>
    /// Returns the collections (BoxSets) a single item belongs to (TODO_V3.md Phase 7.2, "Included In"
    /// on the item detail page). Takes no userId: the answer is scoped to the authenticated user's own
    /// rights (see <see cref="ICollectionsIndexCacheService.GetCollectionsFor(Guid, Jellyfin.Database.Implementations.Entities.User)"/>),
    /// so there is nothing to forge. The underlying <see cref="ICollectionsIndexCacheService"/> (built in Phase 4.3, refreshed
    /// daily by WatchlistDailyRefreshTask) already existed before this endpoint -- this is only a thin
    /// read. If the index is stale, a best-effort background refresh is kicked off (non-blocking, using
    /// the service's own refresh lock) so a fresh install doesn't stay empty until the next scheduled
    /// run; the current (possibly empty) cached data is always returned immediately regardless.
    /// </summary>
    /// <param name="itemId">The item to look up.</param>
    /// <returns>Every collection this item belongs to, or an empty list if none.</returns>
    [HttpGet("Collections/IncludedIn/{itemId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CollectionRef>>> GetCollectionsForItem([FromRoute] Guid itemId)
    {
        if (_collectionsIndexCacheService.IsStale() && _collectionsIndexCacheService.TryAcquireRefreshLock())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _collectionsIndexCacheService.RunRefreshLockedAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background collections index refresh failed.");
                }
            });
        }

        // A server API key is not tied to a user and keeps the global answer; any user session only
        // sees collections (and items) its own library access and parental rules allow.
        var caller = await RequestCaller.ResolveAsync(HttpContext, _authContext).ConfigureAwait(false);
        if (caller.IsApiKey)
        {
            return Ok(_collectionsIndexCacheService.GetCollectionsFor(itemId));
        }

        var user = _userManager.GetUserById(caller.UserId);
        if (user is null)
        {
            return Ok(Array.Empty<CollectionRef>());
        }

        return Ok(_collectionsIndexCacheService.GetCollectionsFor(itemId, user));
    }

    // -------------------------------------------------------------------------
    // Administration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the widget catalog of the admin configuration page: the descriptor of every registered
    /// widget type, and the widget category names in <see cref="WidgetCategory"/> declaration order,
    /// so the page resolves a descriptor's numeric <c>Category</c> without hardcoding the enum.
    /// </summary>
    /// <returns>The <see cref="WidgetCatalog"/>.</returns>
    [HttpGet("Admin/Catalog")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<WidgetCatalog> GetAdminCatalog()
    {
        var descriptors = _registry.GetAll()
            .Select(w => w.GetDescriptor())
            .ToList()
            .AsReadOnly();

        return Ok(new WidgetCatalog(Enum.GetNames<WidgetCategory>(), descriptors));
    }

    /// <summary>
    /// Renders one widget row of the admin page before it is saved (TODO_V4.md 6.5, F-62): the items
    /// the section would show for <paramref name="userId"/>, how many it finds, and why the home
    /// screen would hide it.
    /// </summary>
    /// <param name="row">The widget row as edited on the admin page.</param>
    /// <param name="resolver">The layout resolver that renders the row.</param>
    /// <param name="userId">The user whose library the row reads; the caller by default.</param>
    /// <param name="lang">The requested language of the section title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <see cref="WidgetPreview"/>.</returns>
    [HttpPost("Preview")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<WidgetPreview>> PreviewSection(
        [FromBody] WidgetConfig row,
        [FromServices] WidgetLayoutResolver resolver,
        [FromQuery] Guid userId = default,
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        WidgetPreview? preview;
        try
        {
            preview = await resolver
                .PreviewAsync(resolvedUserId.Value, row, _localizationService.NormalizeLanguage(lang), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Previewing widget '{WidgetType}' failed for user {UserId}.", row.WidgetType, resolvedUserId.Value);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return preview is null ? NotFound() : Ok(preview);
    }

    /// <summary>
    /// Returns the list of available values for an admin widget type, for use in the autocomplete
    /// picker when the administrator adds a new section.
    /// </summary>
    /// <param name="widgetType">The widget type identifier (e.g. "jux.admin.genre").</param>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="search">Optional search string to filter results.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects.</returns>
    [HttpGet("Widget/{widgetType}/values")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<AdminWidgetValue>> GetWidgetValues(
        [FromRoute] string widgetType,
        [FromQuery] Guid userId,
        [FromQuery] string? search = null)
    {
        var widget = _registry.GetByType(widgetType);
        if (widget is null)
        {
            return NotFound();
        }

        if (widget is not AdminWidgetBase adminWidget)
        {
            return BadRequest("The specified widget type does not support configurable values.");
        }

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(adminWidget.GetAvailableValues(user, search));
    }

    /// <summary>
    /// Returns the last-refreshed timestamp, cached item count and library match count for each
    /// TMDb cache type, for display in the admin UI.
    /// </summary>
    /// <returns>An array of <see cref="TMDbCacheStatusDto"/> objects, one per <see cref="TMDbCacheType"/>.</returns>
    [HttpGet("TMDb/Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<TMDbCacheStatusDto>> GetTMDbStatus()
    {
        var statuses = Enum.GetValues<TMDbCacheType>()
            .Select(type =>
            {
                var items = GetTMDbItems(type);
                return new TMDbCacheStatusDto(
                    type.ToString(),
                    _tmdbCacheService.GetLastRefreshedUtc(type),
                    items.Count,
                    items.Count(i => i.LibraryItemId.HasValue));
            })
            .ToList()
            .AsReadOnly();

        return Ok(statuses);
    }

    /// <summary>
    /// Triggers an immediate refresh of all six TMDb lists and Discover instances, whatever their
    /// age, plus every configured Rewards widget instance (TODO_V2.md Phase 14: Rewards deliberately
    /// has no separate "Refresh now" button -- this one action covers both external data
    /// providers). Runs in the background; the response
    /// does not wait for the refresh to complete. Rejects the request with 409 Conflict if a TMDb
    /// refresh (manual, the hourly scheduled task or the startup catch-up) is already in progress.
    /// </summary>
    /// <returns>202 Accepted if a refresh was started; 409 Conflict if one was already running.</returns>
    [HttpPost("TMDb/Refresh")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult RefreshTMDbCache()
    {
        if (!_tmdbCacheService.TryAcquireRefreshLock())
        {
            _logger.LogInformation("TMDb refresh already in progress; rejecting this manual request.");
            return Conflict(new { message = "A TMDb refresh is already in progress." });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _tmdbCacheService.RunRefreshLockedAsync(null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manually triggered TMDb refresh failed.");
            }

            try
            {
                await _rewardsCacheService.RefreshAllInstancesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manually triggered Rewards refresh failed.");
            }
        });

        return Accepted();
    }

    /// <summary>
    /// Returns the ISO 3166-1 country list from TMDb, for use in the Now Playing region picker.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects (Value=country code, Label=name).</returns>
    [HttpGet("TMDb/Countries")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> GetTMDbCountries(CancellationToken cancellationToken)
    {
        var countries = await _tmdbApiClient.GetCountriesAsync(cancellationToken).ConfigureAwait(false);
        var values = countries
            .Select(country => new AdminWidgetValue(country.Code, country.Name))
            .OrderBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        return Ok(values);
    }

    /// <summary>
    /// Returns the full TMDb movie genre list, for the Discover widget's genre picker.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects (Value=genre id, Label=name).</returns>
    [HttpGet("TMDb/Genres")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> GetTMDbGenres(CancellationToken cancellationToken)
    {
        var genres = await _tmdbApiClient.GetMovieGenresAsync(cancellationToken).ConfigureAwait(false);
        var values = genres
            .Select(genre => new AdminWidgetValue(genre.Id.ToString(CultureInfo.InvariantCulture), genre.Name))
            .OrderBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        return Ok(values);
    }

    /// <summary>
    /// Searches TMDb people by name, for the Discover widget's person autocomplete.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects (Value=person id, Label=name).</returns>
    [HttpGet("TMDb/Search/Person")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> SearchTMDbPerson(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        return Ok(await SearchTMDbAsync(_tmdbApiClient.SearchPersonAsync, query, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Searches TMDb keywords by name, for the Discover widget's keyword autocomplete.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects (Value=keyword id, Label=name).</returns>
    [HttpGet("TMDb/Search/Keyword")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> SearchTMDbKeyword(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        return Ok(await SearchTMDbAsync(_tmdbApiClient.SearchKeywordAsync, query, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Searches TMDb companies by name, for the Discover widget's company autocomplete.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects (Value=company id, Label=name).</returns>
    [HttpGet("TMDb/Search/Company")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> SearchTMDbCompany(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        return Ok(await SearchTMDbAsync(_tmdbApiClient.SearchCompanyAsync, query, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Searches Wikidata entities by label, for the Rewards widget's Ceremony and Category
    /// autocomplete fields. Proxied server-side (TODO_V2.md Phase 14 research): browsers cannot set a
    /// custom <c>User-Agent</c> header via <c>fetch()</c>/<c>XMLHttpRequest</c> (a "forbidden header
    /// name" per the Fetch spec), so a Wikidata-policy-compliant request can only be made from the
    /// server, exactly like the TMDb search endpoints above.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects (Value=Wikidata Q-id, Label=entity label).</returns>
    [HttpGet("Rewards/Search/Ceremony")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> SearchRewardsCeremony(
        [FromQuery] string query,
        CancellationToken cancellationToken) => SearchWikidataAsync(query, cancellationToken);

    /// <summary>
    /// Searches Wikidata entities by label, for the Rewards widget's Category autocomplete field.
    /// Shares the same Wikidata entity search endpoint as
    /// <see cref="SearchRewardsCeremony"/> -- ceremonies and award categories are both plain Wikidata
    /// entities, distinguished only by how they're used in the SPARQL query
    /// (<see cref="Rewards.WikidataApiClient"/>), not by a different search mechanism.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of <see cref="AdminWidgetValue"/> objects (Value=Wikidata Q-id, Label=entity label).</returns>
    [HttpGet("Rewards/Search/Category")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> SearchRewardsCategory(
        [FromQuery] string query,
        CancellationToken cancellationToken) => SearchWikidataAsync(query, cancellationToken);

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves whose data the request reads (see <see cref="UserAccessGuard.ResolveUserId"/>): a
    /// regular user always reads their own data, whatever userId the query string carries, and is
    /// refused when it names someone else (IDOR); administrators may name any user.
    /// </summary>
    /// <param name="requestedUserId">The userId from the query string, or <see cref="Guid.Empty"/>.</param>
    /// <returns>The user to read, or null when the request must be refused.</returns>
    private async Task<Guid?> ResolveRequestUserIdAsync(Guid requestedUserId) =>
        (await ResolveRequestUserAsync(requestedUserId).ConfigureAwait(false)).UserId;

    /// <summary>
    /// <see cref="ResolveRequestUserIdAsync"/>, and whether the caller reads their own data rather than
    /// an administrator or an API key reading someone else's.
    /// </summary>
    /// <param name="requestedUserId">The userId from the query string, or <see cref="Guid.Empty"/>.</param>
    /// <returns>The user to read, or null when the request must be refused, and whether it is the caller.</returns>
    private async Task<(Guid? UserId, bool IsCaller)> ResolveRequestUserAsync(Guid requestedUserId)
    {
        var caller = await RequestCaller.ResolveAsync(HttpContext, _authContext).ConfigureAwait(false);
        var userId = UserAccessGuard.ResolveUserId(
            requestedUserId,
            caller.UserId,
            caller.IsApiKey,
            User.IsInRole("Administrator"));
        return (userId, !caller.IsApiKey && userId == caller.UserId);
    }

    private static void SetCacheHeaders(HttpRequest request, HttpResponse response)
    {
        response.Headers.CacheControl = ClientAssetVersion.CacheControlFor(request.Query["v"], ClientAssetVersion.Current);
    }

    private static async Task<IReadOnlyList<AdminWidgetValue>> SearchTMDbAsync(
        Func<string, CancellationToken, Task<IReadOnlyList<TMDbSearchResult>>> search,
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var results = await search(query, cancellationToken).ConfigureAwait(false);
        return results
            .Select(r => new AdminWidgetValue(r.Id.ToString(CultureInfo.InvariantCulture), r.Name))
            .ToList()
            .AsReadOnly();
    }

    private async Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> SearchWikidataAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(Array.Empty<AdminWidgetValue>());
        }

        var results = await _wikidataApiClient.SearchEntitiesAsync(query, cancellationToken).ConfigureAwait(false);
        var values = results
            .Select(r => new AdminWidgetValue(r.Id, string.IsNullOrEmpty(r.Description) ? r.Label : $"{r.Label} ({r.Description})"))
            .ToList()
            .AsReadOnly();

        return Ok(values);
    }

    private IReadOnlyList<IExternalCacheItem> GetTMDbItems(TMDbCacheType type)
    {
        switch (type)
        {
            case TMDbCacheType.TrendingMovies:
                return _tmdbCacheService.GetTrendingMovies();
            case TMDbCacheType.TrendingShows:
                return _tmdbCacheService.GetTrendingShows();
            case TMDbCacheType.AiringToday:
                return _tmdbCacheService.GetAiringToday();
            case TMDbCacheType.TopRatedMovies:
                return _tmdbCacheService.GetTopRatedMovies();
            case TMDbCacheType.TopRatedShows:
                return _tmdbCacheService.GetTopRatedShows();
            case TMDbCacheType.NowPlayingMovies:
                return _tmdbCacheService.GetNowPlayingMovies();
            default:
                _logger.LogWarning("Unhandled TMDbCacheType {Type} in item switch; reporting no item.", type);
                return [];
        }
    }

    /// <summary>Widget catalog returned by /JuxHomepage/Admin/Catalog.</summary>
    /// <param name="WidgetCategories">
    /// Widget category names in <see cref="WidgetCategory"/> declaration order (index = underlying
    /// enum value).
    /// </param>
    /// <param name="Widgets">The descriptor of every registered widget type.</param>
    public record WidgetCatalog(IReadOnlyList<string> WidgetCategories, IReadOnlyList<WidgetDescriptor> Widgets);

    /// <summary>TMDb cache status entry returned by /JuxHomepage/TMDb/Status.</summary>
    /// <param name="Type">The <see cref="TMDbCacheType"/> name (e.g. "TrendingMovies").</param>
    /// <param name="LastRefreshedUtc">UTC timestamp of the last successful refresh, or null if never refreshed.</param>
    /// <param name="ItemCount">The number of items currently cached for this type.</param>
    /// <param name="MatchedCount">How many of those items were found in the local library (the only ones a section can show).</param>
    public record TMDbCacheStatusDto(string Type, DateTime? LastRefreshedUtc, int ItemCount, int MatchedCount);
}
