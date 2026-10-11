namespace Jellyfin.Plugin.JuxHomepage.TMDb.Models;

/// <summary>
/// A TMDb id and the IMDb id TMDb reports for it, persisted by <see cref="TMDbExternalIdCache"/>.
/// </summary>
public sealed class TMDbExternalIdEntry
{
    /// <summary>Gets or sets the TMDb id.</summary>
    public int TmdbId { get; set; }

    /// <summary>Gets or sets the IMDb id (e.g. "tt1375666").</summary>
    public string ImdbId { get; set; } = string.Empty;
}
