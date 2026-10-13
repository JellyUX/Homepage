namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// The "see all" targets a section can carry in <see cref="WidgetDescriptor.Route"/> (TODO_V4.md 9.3,
/// F-56). The client turns each one into a native Jellyfin page, with the section's
/// <see cref="WidgetDescriptor.AdditionalData"/> as the value where the target needs one.
/// </summary>
public static class WidgetRoutes
{
    /// <summary>The native Next Up list.</summary>
    public const string NextUp = "nextup";

    /// <summary>The user's movie library, on its Suggestions tab as the native latest movies link.</summary>
    public const string Movies = "movies";

    /// <summary>The user's shows library, on its Suggestions tab as the native latest shows link.</summary>
    public const string TvShows = "tvshows";

    /// <summary>The JellyUX Watchlist tab.</summary>
    public const string Watchlist = "watchlist";

    /// <summary>The native list of a genre, the value being the genre name.</summary>
    public const string Genre = "genre";

    /// <summary>The native list of a studio, the value being the studio name.</summary>
    public const string Studio = "studio";

    /// <summary>The native page of a person, the value being the person name.</summary>
    public const string Person = "person";

    /// <summary>The native list of a tag, the value being the tag.</summary>
    public const string Tag = "tag";

    /// <summary>The native page of an item, the value being its id (a collection).</summary>
    public const string Item = "item";
}
