namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// How fresh a widget's section must be when the home screen shows it (REV-04.a, DR-032). No value is zero, so a
/// widget that leaves its level unset is caught (<c>EveryWidget_DeclaresAFreshnessLevel</c>).
/// </summary>
public enum FreshnessLevel
{
    /// <summary>
    /// Reflects the server state read while the home screen mounts: playback rows, recently added items and the
    /// Watchlist. Never served from a cache a playback end did not drop (V17).
    /// </summary>
    Direct = 1,

    /// <summary>
    /// May be served from the cache and recomputed in the background: the choice and order of personalized
    /// sections, library selections, TMDb and Rewards lists.
    /// </summary>
    Deferred = 2
}
