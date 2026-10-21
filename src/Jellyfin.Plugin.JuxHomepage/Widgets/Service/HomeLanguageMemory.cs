using Jellyfin.Plugin.JuxHomepage.Configuration;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Remembers the language of each user's last home screen in their settings file
/// (<see cref="UserConfiguration.HomeLanguage"/>), so the home screen built ahead after a restart is
/// cached under the language the user will ask for (PERF-07, DEC-P07-10): the layout cache is keyed by
/// language. Written only when the language changes, through one locked read and write, so it never
/// overwrites a settings save made in between.
/// </summary>
public sealed class HomeLanguageMemory
{
    private readonly IUserConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeLanguageMemory"/> class.
    /// </summary>
    /// <param name="store">The per-user settings store.</param>
    public HomeLanguageMemory(IUserConfigurationStore store)
    {
        _store = store;
    }

    /// <summary>Records the language of a home screen the user asked for.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="language">The normalized language, e.g. "fr".</param>
    public void Remember(Guid userId, string language)
    {
        // The read is served from memory: the common case, an unchanged language, writes nothing.
        if (string.Equals(_store.GetUserConfiguration(userId)?.HomeLanguage, language, StringComparison.Ordinal))
        {
            return;
        }

        _store.UpdateUserConfiguration(userId, config =>
        {
            if (string.Equals(config.HomeLanguage, language, StringComparison.Ordinal))
            {
                return false;
            }

            config.HomeLanguage = language;
            return true;
        });
    }

    /// <summary>Returns the language of the user's last home screen, or null when none was seen.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>The normalized language, or null.</returns>
    public string? Get(Guid userId) => _store.GetUserConfiguration(userId)?.HomeLanguage;
}
