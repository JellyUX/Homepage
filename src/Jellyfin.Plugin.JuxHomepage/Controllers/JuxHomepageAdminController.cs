using System.Globalization;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.Library.Models;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Rewards;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Admin;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// The routes of the admin configuration page (integration warnings, configuration restore, widget catalog,
/// row preview and values, TMDb and Wikidata lookups), all restricted to administrators. Same base route as
/// <see cref="JuxHomepageController"/>, so every URL is unchanged.
/// Route: /JuxHomepage
/// </summary>
[ApiController]
[Route("JuxHomepage")]
public class JuxHomepageAdminController : ControllerBase
{
    /// <summary>
    /// How long an admin lookup (TMDb and Wikidata search, TMDb countries and genres) waits for the external
    /// service before answering empty, instead of the 30 s x 2 (TMDb) or 90 s (Wikidata) of the HTTP clients
    /// (FIAB-10.a; SearchTMDbPerson_TMDbNeverAnswers_AnswersEmptyAtTheInteractiveDeadline).
    /// </summary>
    internal static readonly TimeSpan InteractiveDeadline = TimeSpan.FromSeconds(8);

    private readonly IWidgetRegistry _registry;
    private readonly IUserManager _userManager;
    private readonly ITMDbCacheService _tmdbCacheService;
    private readonly ITMDbApiClient _tmdbApiClient;
    private readonly IRewardsCacheService _rewardsCacheService;
    private readonly IWikidataApiClient _wikidataApiClient;
    private readonly ILocalizationService _localizationService;
    private readonly IRequestUserResolver _requestUsers;
    private readonly ILogger<JuxHomepageAdminController> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="JuxHomepageAdminController"/> class.
    /// </summary>
    /// <param name="registry">The widget registry.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="tmdbCacheService">TMDb disk cache service.</param>
    /// <param name="tmdbApiClient">TMDb HTTP API client.</param>
    /// <param name="rewardsCacheService">Rewards disk cache service.</param>
    /// <param name="wikidataApiClient">Wikidata HTTP API client.</param>
    /// <param name="localizationService">Widget/admin-UI translation service.</param>
    /// <param name="requestUsers">Resolves who sent a request and whose data it reads.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Clock of the admin lookups' deadline; defaults to <see cref="TimeProvider.System"/>.</param>
    public JuxHomepageAdminController(
        IWidgetRegistry registry,
        IUserManager userManager,
        ITMDbCacheService tmdbCacheService,
        ITMDbApiClient tmdbApiClient,
        IRewardsCacheService rewardsCacheService,
        IWikidataApiClient wikidataApiClient,
        ILocalizationService localizationService,
        IRequestUserResolver requestUsers,
        ILogger<JuxHomepageAdminController> logger,
        TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _userManager = userManager;
        _tmdbCacheService = tmdbCacheService;
        _tmdbApiClient = tmdbApiClient;
        _rewardsCacheService = rewardsCacheService;
        _wikidataApiClient = wikidataApiClient;
        _localizationService = localizationService;
        _requestUsers = requestUsers;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the integration warning codes the admin page shows in its banner (missing
    /// FileTransformation, Custom Tabs installed), then one <c>TMDb{kind}</c> code per kind of failure that
    /// left a TMDb list empty (FIAB-10.c, DEC-P10-1), computed on each call.
    /// </summary>
    /// <param name="coexistenceWarnings">The warning source.</param>
    /// <returns>The warning codes, empty when JellyUX can run normally.</returns>
    [HttpGet("Admin/Warnings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetAdminWarnings([FromServices] CoexistenceWarnings coexistenceWarnings)
    {
        return Ok(coexistenceWarnings.Get().Concat(TMDbWarningCodes(_tmdbCacheService)).ToList().AsReadOnly());
    }

    /// <summary>
    /// One <c>TMDb{kind}</c> warning code per kind of failure that left a fixed TMDb list empty, in
    /// <see cref="TMDbFailureKind"/> order (<c>TMDbWarningCodes_OneCodePerKindOfFailure</c>).
    /// </summary>
    /// <param name="tmdbCacheService">The source of each list's last failure.</param>
    /// <returns>The warning codes, empty when no list failed.</returns>
    internal static IEnumerable<string> TMDbWarningCodes(ITMDbCacheService tmdbCacheService) =>
        Enum.GetValues<TMDbCacheType>()
            .Select(tmdbCacheService.GetLastError)
            .OfType<TMDbFailureKind>()
            .Distinct()
            .Order()
            .Select(kind => "TMDb" + kind);

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
            .Select(WithDefaultRow)
            .ToList()
            .AsReadOnly();

        return Ok(new WidgetCatalog(Enum.GetNames<WidgetCategory>(), descriptors));
    }

    // The descriptor of a widget with the row the admin page adds for it (QUAL-03.c).
    private static WidgetDescriptor WithDefaultRow(IWidget widget)
    {
        var descriptor = widget.GetDescriptor();
        descriptor.DefaultRow = new WidgetConfig
        {
            WidgetType = widget.WidgetType,
            MinItems = descriptor.MinItems,
            ViewMode = descriptor.ViewMode,
            ExtraParams = [.. widget.DefaultExtraParams.Select(p => new WidgetExtraParam { Key = p.Key, Value = p.Value })]
        };
        return descriptor;
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
    /// Returns the last-refreshed timestamp, cached item count, library match count and last failure for
    /// each TMDb cache type, for display in the admin UI.
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
                    items.Count(i => i.LibraryItemId.HasValue),
                    _tmdbCacheService.GetLastError(type)?.ToString(),
                    _tmdbCacheService.GetLastErrorUtc(type));
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
        var countries = await WithInteractiveDeadlineAsync("TMDb countries", _tmdbApiClient.GetCountriesAsync, cancellationToken).ConfigureAwait(false);
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
        var genres = await WithInteractiveDeadlineAsync("TMDb genres", _tmdbApiClient.GetMovieGenresAsync, cancellationToken).ConfigureAwait(false);
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

    // Whose data the request reads, or null when it must be refused (IDOR, IRequestUserResolver.ResolveAsync).
    private async Task<Guid?> ResolveRequestUserIdAsync(Guid requestedUserId) =>
        (await _requestUsers.ResolveAsync(HttpContext, requestedUserId).ConfigureAwait(false)).UserId;

    /// <summary>
    /// Runs an admin lookup against an external service under <see cref="InteractiveDeadline"/> (FIAB-10.a):
    /// past it, the lookup answers empty, as it does when the service fails. A request the browser aborts is
    /// still cancelled (GetTMDbCountries_RequestAborted_IsNotAnsweredAsADeadline).
    /// </summary>
    private async Task<IReadOnlyList<T>> WithInteractiveDeadlineAsync<T>(
        string lookup,
        Func<CancellationToken, Task<IReadOnlyList<T>>> call,
        CancellationToken requestAborted)
    {
        using var deadline = new CancellationTokenSource(InteractiveDeadline, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, deadline.Token);
        try
        {
            return await call(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            _logger.LogWarning("The admin lookup '{Lookup}' had no answer within {Seconds} s; answering empty.", lookup, InteractiveDeadline.TotalSeconds);
            return [];
        }
    }

    private async Task<IReadOnlyList<AdminWidgetValue>> SearchTMDbAsync(
        Func<string, CancellationToken, Task<IReadOnlyList<TMDbSearchResult>>> search,
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var results = await WithInteractiveDeadlineAsync("TMDb search", token => search(query, token), cancellationToken).ConfigureAwait(false);
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

        var results = await WithInteractiveDeadlineAsync("Wikidata search", token => _wikidataApiClient.SearchEntitiesAsync(query, token), cancellationToken).ConfigureAwait(false);
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
    /// <param name="LastError">The <see cref="TMDbFailureKind"/> name of why the last refresh came back empty, or null (FIAB-10.c).</param>
    /// <param name="LastErrorUtc">When that failure was recorded, or null (ADM-02.a).</param>
    public record TMDbCacheStatusDto(string Type, DateTime? LastRefreshedUtc, int ItemCount, int MatchedCount, string? LastError, DateTime? LastErrorUtc);
}
