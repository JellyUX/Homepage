namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Per-module switches for the client scripts JellyUX adds outside the home sections. A disabled
/// module's script tag is not injected into index.html, so it costs nothing on any page.
/// </summary>
public sealed class ModulesConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether the Watchlist, Progress, History and Statistics home
    /// tabs are added.
    /// </summary>
    public bool Tabs { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether cards get the watchlist button and library pages get
    /// infinite scrolling.
    /// </summary>
    public bool CardEnhancements { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a single-season series shows its episodes directly
    /// instead of a lone season card.
    /// </summary>
    public bool SeriesFlatten { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether detail pages show the collections an item belongs to,
    /// and collection pages get sort controls.
    /// </summary>
    public bool Collections { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether detail pages show a library breadcrumb in the header.
    /// </summary>
    public bool Breadcrumb { get; set; } = true;
}
