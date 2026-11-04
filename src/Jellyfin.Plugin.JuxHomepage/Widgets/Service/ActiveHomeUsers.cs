using Jellyfin.Plugin.JuxHomepage.Localization;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// The users whose home screens are built ahead (after a start, every night): those active in the last 7 days, most
/// recent first, each in the language of their last home screen, or the server's interface language (DEC-P07-10).
/// </summary>
public sealed class ActiveHomeUsers
{
    /// <summary>How recently a user must have been active to have their home screen built ahead.</summary>
    internal static readonly TimeSpan ActiveWithin = TimeSpan.FromDays(7);

    private readonly IUserManager _userManager;
    private readonly HomeLanguageMemory _homeLanguages;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveHomeUsers"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="homeLanguages">The language of each user's last home screen.</param>
    /// <param name="serverConfiguration">The server configuration, for its interface language.</param>
    /// <param name="localization">Normalizes a language like a home screen request does.</param>
    /// <param name="timeProvider">Time source of the activity window.</param>
    public ActiveHomeUsers(
        IUserManager userManager,
        HomeLanguageMemory homeLanguages,
        IServerConfigurationManager serverConfiguration,
        ILocalizationService localization,
        TimeProvider timeProvider)
    {
        _userManager = userManager;
        _homeLanguages = homeLanguages;
        _serverConfiguration = serverConfiguration;
        _localization = localization;
        _timeProvider = timeProvider;
    }

    /// <summary>Returns the users active in the last 7 days, most recent first, at most <paramref name="max"/>.</summary>
    /// <param name="max">The most users returned.</param>
    /// <returns>Their ids.</returns>
    public IReadOnlyList<Guid> MostRecent(int max)
    {
        var activeSince = _timeProvider.GetUtcNow().UtcDateTime - ActiveWithin;
        return [.. _userManager.GetUsers()
            .Where(u => u.LastActivityDate >= activeSince)
            .OrderByDescending(u => u.LastActivityDate)
            .Take(max)
            .Select(u => u.Id)];
    }

    /// <summary>Returns the language of the user's last home screen, or the server's interface language.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>The normalized language.</returns>
    public string LanguageOf(Guid userId) =>
        _homeLanguages.Get(userId) ?? _localization.NormalizeLanguage(_serverConfiguration.Configuration.UICulture);
}
