using System.Reflection;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.Library.Models;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Watchlist.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using MediaBrowser.Controller.Library;
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

    private readonly WidgetService _widgetService;
    private readonly IUserManager _userManager;
    private readonly ILocalizationService _localizationService;
    private readonly IWatchlistService _watchlistService;
    private readonly ISeriesProgressViewService _seriesProgressViewService;
    private readonly IMovieHistoryViewService _movieHistoryViewService;
    private readonly IStatisticsService _statisticsService;
    private readonly ICollectionsIndexCacheService _collectionsIndexCacheService;
    private readonly IRequestUserResolver _requestUsers;
    private readonly HomeLanguageMemory _homeLanguages;
    private readonly ILogger<JuxHomepageController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JuxHomepageController"/> class.
    /// </summary>
    /// <param name="widgetService">The widget orchestration service.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="localizationService">Widget/admin-UI translation service.</param>
    /// <param name="watchlistService">Watchlist query service.</param>
    /// <param name="seriesProgressViewService">Series Progress view service.</param>
    /// <param name="movieHistoryViewService">Movie History view service.</param>
    /// <param name="statisticsService">Watch-history statistics service.</param>
    /// <param name="collectionsIndexCacheService">Collections reverse index cache service.</param>
    /// <param name="requestUsers">Resolves who sent a request and whose data it reads.</param>
    /// <param name="homeLanguages">Remembers the language of each user's last home screen (DEC-P07-10).</param>
    /// <param name="logger">Logger.</param>
    public JuxHomepageController(
        WidgetService widgetService,
        IUserManager userManager,
        ILocalizationService localizationService,
        IWatchlistService watchlistService,
        ISeriesProgressViewService seriesProgressViewService,
        IMovieHistoryViewService movieHistoryViewService,
        IStatisticsService statisticsService,
        ICollectionsIndexCacheService collectionsIndexCacheService,
        IRequestUserResolver requestUsers,
        HomeLanguageMemory homeLanguages,
        ILogger<JuxHomepageController> logger)
    {
        _widgetService = widgetService;
        _userManager = userManager;
        _localizationService = localizationService;
        _watchlistService = watchlistService;
        _seriesProgressViewService = seriesProgressViewService;
        _movieHistoryViewService = movieHistoryViewService;
        _statisticsService = statisticsService;
        _collectionsIndexCacheService = collectionsIndexCacheService;
        _requestUsers = requestUsers;
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
        var (resolvedUserId, isCaller) = await _requestUsers.ResolveAsync(HttpContext, userId).ConfigureAwait(false);
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
        var caller = await _requestUsers.ResolveCallerAsync(HttpContext).ConfigureAwait(false);
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
    // Private helpers
    // -------------------------------------------------------------------------

    // Whose data the request reads, or null when it must be refused (IDOR, IRequestUserResolver.ResolveAsync).
    private async Task<Guid?> ResolveRequestUserIdAsync(Guid requestedUserId) =>
        (await _requestUsers.ResolveAsync(HttpContext, requestedUserId).ConfigureAwait(false)).UserId;

    private static void SetCacheHeaders(HttpRequest request, HttpResponse response)
    {
        response.Headers.CacheControl = ClientAssetVersion.CacheControlFor(request.Query["v"], ClientAssetVersion.Current);
    }
}
