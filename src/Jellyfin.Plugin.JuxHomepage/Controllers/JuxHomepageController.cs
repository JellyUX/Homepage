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
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Web resources (anonymous)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serves the JellyUX Homepage JavaScript bundle.
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-homepage.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetScript()
    {
        var stream = GetEmbeddedResource("jux-homepage.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX shared UI helpers script (empty state, loading spinner, progress bar, stat
    /// card, native sort dialog -- TODO_V3.md Phase 6 bis). Anonymous - loaded by a script tag
    /// injected into index.html, before the tab-rendering scripts that consume it.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-ui.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetUiScript()
    {
        var stream = GetEmbeddedResource("jux-ui.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX shared UI helpers stylesheet (TODO_V3.md Phase 6 bis).
    /// Anonymous - loaded by a link tag injected into index.html.
    /// </summary>
    /// <returns>CSS file contents.</returns>
    [HttpGet("jux-ui.css")]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetUiStylesheet()
    {
        var stream = GetEmbeddedResource("jux-ui.css");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "text/css");
    }

    /// <summary>
    /// Serves the JellyUX tab injector script (Watchlist/Progress/History/Statistics tab buttons).
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-tab-injector.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetTabInjectorScript()
    {
        var stream = GetEmbeddedResource("jux-tab-injector.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX Watchlist tab rendering script.
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-watchlist.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetWatchlistScript()
    {
        var stream = GetEmbeddedResource("jux-watchlist.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX card-overlay watchlist toggle button script.
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-card-hooks.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetCardHooksScript()
    {
        var stream = GetEmbeddedResource("jux-card-hooks.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX Series Progress tab rendering script.
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-progress.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetProgressScript()
    {
        var stream = GetEmbeddedResource("jux-progress.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX Movie History tab rendering script.
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-history.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetHistoryScript()
    {
        var stream = GetEmbeddedResource("jux-history.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX Statistics tab rendering script.
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-statistics.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetStatisticsScript()
    {
        var stream = GetEmbeddedResource("jux-statistics.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX single-season show flattening script (TODO_V3.md Phase 7.1).
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-series-flatten.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetSeriesFlattenScript()
    {
        var stream = GetEmbeddedResource("jux-series-flatten.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX "Included In" collections + collection sorting script (TODO_V3.md Phase 7.2).
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-collections.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetCollectionsScript()
    {
        var stream = GetEmbeddedResource("jux-collections.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX breadcrumb navigation script (TODO_V3.md Phase 8.1).
    /// Anonymous - loaded by a script tag injected into index.html.
    /// </summary>
    /// <returns>JavaScript file contents.</returns>
    [HttpGet("jux-breadcrumb.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBreadcrumbScript()
    {
        var stream = GetEmbeddedResource("jux-breadcrumb.js");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Serves the JellyUX Homepage CSS stylesheet.
    /// Anonymous - loaded by a link tag injected into index.html.
    /// </summary>
    /// <returns>CSS file contents.</returns>
    [HttpGet("jux-homepage.css")]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetStylesheet()
    {
        var stream = GetEmbeddedResource("jux-homepage.css");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "text/css");
    }

    /// <summary>
    /// Serves the JellyUX Homepage admin config page's CSS stylesheet.
    /// Anonymous - a browser-loaded &lt;link&gt; tag carries no Jellyfin auth token, same reasoning as
    /// jux-homepage.css above.
    /// </summary>
    /// <returns>CSS file contents.</returns>
    [HttpGet("config.css")]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetConfigStylesheet()
    {
        var stream = GetEmbeddedResource("config.css");
        if (stream is null)
        {
            return NotFound();
        }

        SetCacheHeaders(Request, Response);
        return File(stream, "text/css");
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
    /// Returns plugin status metadata: enabled flag and the ordered list of
    /// widget category names (matching <see cref="WidgetCategory"/>'s declaration order), so the
    /// admin config page can resolve a descriptor's numeric <c>Category</c> to a name without
    /// hardcoding the enum's underlying values.
    /// </summary>
    /// <returns>Plugin metadata object.</returns>
    [HttpGet("meta")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginMeta> GetMeta()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return Ok(new PluginMeta(config.Enabled, Enum.GetNames<WidgetCategory>()));
    }

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
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        var sectionsPage = await _widgetService
            .GetWidgetsForUser(userId, page, lang, cancellationToken)
            .ConfigureAwait(false);

        return Ok(sectionsPage);
    }

    /// <summary>
    /// Returns items for a specific widget section.
    /// Called lazily by the front end when a section scrolls into view.
    /// </summary>
    /// <param name="widgetType">The widget type identifier.</param>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="additionalData">Optional instance-specific data (e.g. library ID).</param>
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
            .GetWidgetItems(userId, widgetType, additionalData, startIndex, limit, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    // -------------------------------------------------------------------------
    // Watchlist (authenticated user)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a filtered, sorted, paginated page of the user's Watchlist items (every item with
    /// <c>UserData.Likes == true</c>). TODO_V3.md Phase 5.1.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="sortBy">One of "Name", "DateAdded", "ReleaseDate", "CommunityRating". Defaults to "DateAdded".</param>
    /// <param name="sortOrder">"Ascending" or "Descending". Defaults to "Descending".</param>
    /// <param name="includeItemTypes">One of "Movie", "Series", "All". Defaults to "All".</param>
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

        var result = _watchlistService.GetItems(userId, sortBy, sortOrder, includeItemTypes, startIndex, limit, includeTotalRecordCount: true, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns every item id currently in the user's Watchlist, with no image/metadata fields --
    /// a lightweight payload for <c>jux-card-hooks.js</c> to pre-load once per page so each card can
    /// immediately show the correct toggle icon. TODO_V3.md Phase 5.2.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every item id currently liked by this user.</returns>
    [HttpGet("Watchlist/Ids")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<Guid>>> GetWatchlistIds(
        [FromQuery] Guid userId,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = await ResolveRequestUserIdAsync(userId).ConfigureAwait(false);
        if (resolvedUserId is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        userId = resolvedUserId.Value;

        return Ok(_watchlistService.GetLikedItemIds(userId, cancellationToken));
    }

    /// <summary>
    /// Returns a page of the user's in-progress series (Series Progress view, TODO_V3.md Phase 6.1),
    /// each hydrated with a poster/name and the watched/total episode counts and last-watched episode.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="sortBy">"Name" or "LastPlayed". Defaults to "LastPlayed".</param>
    /// <param name="sortOrder">"Ascending" or "Descending". Defaults to "Descending".</param>
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

        var result = _seriesProgressViewService.GetItems(userId, sortBy, sortOrder, startIndex, limit, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns a page of the user's watched movies (Movie History view, TODO_V3.md Phase 6.2), each
    /// hydrated with a poster/name and last-played date.
    /// </summary>
    /// <param name="userId">The requesting user's Jellyfin identifier.</param>
    /// <param name="sortBy">"Name" or "LastPlayed". Defaults to "LastPlayed".</param>
    /// <param name="sortOrder">"Ascending" or "Descending". Defaults to "Descending".</param>
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

        var result = _movieHistoryViewService.GetItems(userId, sortBy, sortOrder, startIndex, limit, cancellationToken);
        return Ok(result);
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

        return Ok(_statisticsService.GetStatistics(userId));
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
        var authInfo = await _authContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        if (authInfo.IsApiKey)
        {
            return Ok(_collectionsIndexCacheService.GetCollectionsFor(itemId));
        }

        var user = _userManager.GetUserById(authInfo.UserId);
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
    /// Returns descriptors for all registered widget types.
    /// Used by the admin UI to display available widgets and their capabilities.
    /// </summary>
    /// <returns>An array of <see cref="WidgetDescriptor"/> objects.</returns>
    [HttpGet("Widgets")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WidgetDescriptor>> GetWidgets()
    {
        var descriptors = _registry.GetAll()
            .Select(w => w.GetDescriptor())
            .ToList()
            .AsReadOnly();

        return Ok(descriptors);
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
    /// Returns the last-refreshed timestamp and cached item count for each TMDb cache type, for
    /// display in the admin UI.
    /// </summary>
    /// <returns>An array of <see cref="TMDbCacheStatusDto"/> objects, one per <see cref="TMDbCacheType"/>.</returns>
    [HttpGet("TMDb/Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<TMDbCacheStatusDto>> GetTMDbStatus()
    {
        var statuses = Enum.GetValues<TMDbCacheType>()
            .Select(type => new TMDbCacheStatusDto(
                type.ToString(),
                _tmdbCacheService.GetLastRefreshedUtc(type),
                GetTMDbItemCount(type)))
            .ToList()
            .AsReadOnly();

        return Ok(statuses);
    }

    /// <summary>
    /// Triggers an immediate refresh of all four TMDb cache types, plus every configured Rewards
    /// widget instance (TODO_V2.md Phase 14: Rewards deliberately has no separate "Refresh now" button
    /// -- this one action covers both external data providers). Runs in the background; the response
    /// does not wait for the refresh to complete. Rejects the request with 409 Conflict if a TMDb
    /// refresh (manual or the daily scheduled task) is already in progress.
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
    private async Task<Guid?> ResolveRequestUserIdAsync(Guid requestedUserId)
    {
        var authInfo = await _authContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        return UserAccessGuard.ResolveUserId(
            requestedUserId,
            authInfo.UserId,
            authInfo.IsApiKey,
            User.IsInRole("Administrator"));
    }

    private static Stream? GetEmbeddedResource(string suffix)
    {
        var name = PluginAssembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        return name is null ? null : PluginAssembly.GetManifestResourceStream(name);
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

    private int GetTMDbItemCount(TMDbCacheType type)
    {
        switch (type)
        {
            case TMDbCacheType.TrendingMovies:
                return _tmdbCacheService.GetTrendingMovies().Count;
            case TMDbCacheType.TrendingShows:
                return _tmdbCacheService.GetTrendingShows().Count;
            case TMDbCacheType.AiringToday:
                return _tmdbCacheService.GetAiringToday().Count;
            case TMDbCacheType.TopRatedMovies:
                return _tmdbCacheService.GetTopRatedMovies().Count;
            case TMDbCacheType.TopRatedShows:
                return _tmdbCacheService.GetTopRatedShows().Count;
            case TMDbCacheType.NowPlayingMovies:
                return _tmdbCacheService.GetNowPlayingMovies().Count;
            default:
                _logger.LogWarning("Unhandled TMDbCacheType {Type} in item-count switch; reporting 0.", type);
                return 0;
        }
    }

    /// <summary>Plugin status metadata returned by /JuxHomepage/meta.</summary>
    /// <param name="Enabled">Whether the plugin is active.</param>
    /// <param name="WidgetCategories">
    /// Widget category names in <see cref="WidgetCategory"/> declaration order (index = underlying
    /// enum value), so the admin config page can resolve a descriptor's <c>Category</c> without
    /// hardcoding the enum's numeric values.
    /// </param>
    public record PluginMeta(bool Enabled, IReadOnlyList<string> WidgetCategories);

    /// <summary>TMDb cache status entry returned by /JuxHomepage/TMDb/Status.</summary>
    /// <param name="Type">The <see cref="TMDbCacheType"/> name (e.g. "TrendingMovies").</param>
    /// <param name="LastRefreshedUtc">UTC timestamp of the last successful refresh, or null if never refreshed.</param>
    /// <param name="ItemCount">The number of items currently cached for this type.</param>
    public record TMDbCacheStatusDto(string Type, DateTime? LastRefreshedUtc, int ItemCount);
}
