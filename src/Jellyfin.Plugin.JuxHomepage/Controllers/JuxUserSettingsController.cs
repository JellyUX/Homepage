using Jellyfin.Plugin.JuxHomepage.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JuxHomepage.Controllers;

/// <summary>
/// A user's own JellyUX settings (TODO_V4.md 8.3, F-36), read and saved by the JellyUX block of the
/// native Home preferences page. Any signed-in user may call it for themselves; the IDOR guard (F-22)
/// refuses another user's id unless the caller is an administrator.
/// Route: /JuxHomepage/UserSettings
/// </summary>
[ApiController]
[Route("JuxHomepage/UserSettings")]
public class JuxUserSettingsController : ControllerBase
{
    private readonly UserSettingsService _userSettings;
    private readonly IRequestUserResolver _requestUsers;

    /// <summary>
    /// Initializes a new instance of the <see cref="JuxUserSettingsController"/> class.
    /// </summary>
    /// <param name="userSettings">Reads and saves the user's settings.</param>
    /// <param name="requestUsers">Resolves whose settings the request reads, the IDOR guard.</param>
    public JuxUserSettingsController(UserSettingsService userSettings, IRequestUserResolver requestUsers)
    {
        _userSettings = userSettings;
        _requestUsers = requestUsers;
    }

    /// <summary>Returns the user's JellyUX settings.</summary>
    /// <param name="userId">The user; a regular user always reads their own settings.</param>
    /// <param name="lang">The language of the section names.</param>
    /// <returns>Whether JellyUX runs for the user, and the sections they may hide.</returns>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserSettingsView>> Get([FromQuery] Guid userId, [FromQuery] string? lang = null)
    {
        var resolved = await ResolveUserIdAsync(userId).ConfigureAwait(false);
        if (resolved is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Ok(_userSettings.Get(resolved.Value, lang));
    }

    /// <summary>Saves the user's JellyUX settings.</summary>
    /// <param name="userId">The user; a regular user always saves their own settings.</param>
    /// <param name="update">Whether JellyUX runs for the user, and the keys of the sections they hide.</param>
    /// <returns>No content, or 500 when the settings could not be written.</returns>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Save([FromQuery] Guid userId, [FromBody] UserSettingsUpdate update)
    {
        var resolved = await ResolveUserIdAsync(userId).ConfigureAwait(false);
        if (resolved is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return _userSettings.Save(resolved.Value, update)
            ? NoContent()
            : StatusCode(StatusCodes.Status500InternalServerError);
    }

    private async Task<Guid?> ResolveUserIdAsync(Guid requestedUserId) =>
        (await _requestUsers.ResolveAsync(HttpContext, requestedUserId).ConfigureAwait(false)).UserId;
}
