using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// What a section's items depend on (<see cref="IWidget.Sources"/>, REV-04.c, DR-032): a library change or a
/// rewritten external list drops only the cached pages of the sections that name its source.
/// </summary>
public static class SectionSources
{
    /// <summary>Movies of the library.</summary>
    public const string Movies = "Movies";

    /// <summary>Series of the library, with their seasons and episodes.</summary>
    public const string Series = "Series";

    /// <summary>Collections (box sets) of the library.</summary>
    public const string Collections = "Collections";

    /// <summary>The libraries themselves (folders and views).</summary>
    public const string Libraries = "Libraries";

    /// <summary>The TMDb lists of <c>TMDbCacheService</c>.</summary>
    public const string TMDb = "TMDb";

    /// <summary>The award lists of <c>RewardsCacheService</c>.</summary>
    public const string Rewards = "Rewards";

    /// <summary>The library sources most sections read: movies and series.</summary>
    public static readonly IReadOnlyList<string> MoviesAndSeries = [Movies, Series];

    /// <summary>A TMDb list matched against the library's movies and series.</summary>
    public static readonly IReadOnlyList<string> TMDbList = [Movies, Series, TMDb];

    /// <summary>
    /// The source an added or removed library item changes, or null when no section reads its kind (people,
    /// music, photos...).
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <returns>The source, or null.</returns>
    public static string? OfItemKind(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Movie => Movies,
        BaseItemKind.Series or BaseItemKind.Season or BaseItemKind.Episode => Series,
        BaseItemKind.BoxSet => Collections,
        BaseItemKind.CollectionFolder or BaseItemKind.UserView or BaseItemKind.AggregateFolder => Libraries,
        _ => null,
    };
}
