namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// A widget whose rows may leave out the titles the user has played (the "exclude watched" setting of the
/// personalized rows): a stored order of such a row is filtered live when it is read again (DR-033).
/// </summary>
public interface IHidesPlayedItems
{
    /// <summary>Returns whether a row with these parameters leaves out the titles the user has played.</summary>
    /// <param name="extraParams">The row's parameters.</param>
    /// <returns>True when played titles are left out.</returns>
    bool HidesPlayed(IReadOnlyDictionary<string, string>? extraParams);
}
