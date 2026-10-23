using System.Globalization;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;

namespace Jellyfin.Plugin.JuxHomepage.TMDb.Models;

/// <summary>
/// Filter parameters for TMDb's <c>/discover/movie</c> endpoint, as configured by the administrator
/// for a single Discover widget instance. Stored round-trip via a widget config row's
/// <c>ExtraParams</c> (see <see cref="FromExtraParams"/>), the same mechanism admin widgets use for
/// their single "value" parameter.
/// </summary>
public sealed class TMDbDiscoverFilter
{
    /// <summary>Gets or sets the TMDb genre ids to filter by, if any.</summary>
    public IReadOnlyList<int>? GenreIds { get; set; }

    /// <summary>Gets or sets the TMDb person ids to filter by, if any.</summary>
    public IReadOnlyList<int>? PersonIds { get; set; }

    /// <summary>Gets or sets the TMDb keyword ids to filter by, if any.</summary>
    public IReadOnlyList<int>? KeywordIds { get; set; }

    /// <summary>Gets or sets the TMDb company ids to filter by, if any.</summary>
    public IReadOnlyList<int>? CompanyIds { get; set; }

    /// <summary>Gets or sets the TMDb <c>sort_by</c> value (e.g. "popularity.desc").</summary>
    public string SortBy { get; set; } = "popularity.desc";

    /// <summary>Gets or sets the primary release year to filter by, if any.</summary>
    public int? PrimaryReleaseYear { get; set; }

    /// <summary>Gets or sets the minimum average vote to filter by, if any.</summary>
    public double? VoteAverageGte { get; set; }

    /// <summary>
    /// Gets or sets the minimum vote count to filter by. Defaults to 50 (mirrors the intent of
    /// TMDb's own <c>top_rated</c> threshold) to avoid surfacing movies with statistically
    /// insignificant ratings.
    /// </summary>
    public int VoteCountGte { get; set; } = 50;

    /// <summary>Gets or sets the number of TMDb result pages (20 items each) to fetch, clamped 1-5.</summary>
    public int Pages { get; set; } = 1;

    /// <summary>
    /// Reconstructs a <see cref="TMDbDiscoverFilter"/> from a Discover widget instance's
    /// configured extra parameters (as forwarded by <see cref="Widgets.WidgetService"/>). Missing
    /// keys fall back to this class's defaults.
    /// </summary>
    /// <param name="extraParams">The instance's configured extra parameters.</param>
    /// <returns>The reconstructed filter.</returns>
    public static TMDbDiscoverFilter FromExtraParams(IReadOnlyDictionary<string, string> extraParams)
    {
        var filter = new TMDbDiscoverFilter
        {
            GenreIds = ParseIntList(extraParams, DiscoverMoviesWidget.GenreIdsParam),
            PersonIds = ParseIntList(extraParams, DiscoverMoviesWidget.PersonIdsParam),
            KeywordIds = ParseIntList(extraParams, DiscoverMoviesWidget.KeywordIdsParam),
            CompanyIds = ParseIntList(extraParams, DiscoverMoviesWidget.CompanyIdsParam)
        };

        if (extraParams.TryGetValue(DiscoverMoviesWidget.SortByParam, out var sortBy) && !string.IsNullOrWhiteSpace(sortBy))
        {
            filter.SortBy = sortBy;
        }

        if (extraParams.TryGetValue(DiscoverMoviesWidget.YearParam, out var yearText)
            && int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            filter.PrimaryReleaseYear = year;
        }

        if (extraParams.TryGetValue(DiscoverMoviesWidget.MinRatingParam, out var minRatingText)
            && double.TryParse(minRatingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minRating))
        {
            filter.VoteAverageGte = minRating;
        }

        if (extraParams.TryGetValue(DiscoverMoviesWidget.MinVotesParam, out var minVotesText)
            && int.TryParse(minVotesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minVotes))
        {
            filter.VoteCountGte = minVotes;
        }

        if (extraParams.TryGetValue(DiscoverMoviesWidget.PagesParam, out var pagesText)
            && int.TryParse(pagesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pages))
        {
            filter.Pages = Math.Clamp(pages, 1, 5);
        }

        return filter;
    }

    private static IReadOnlyList<int>? ParseIntList(IReadOnlyDictionary<string, string> extraParams, string key)
    {
        if (!extraParams.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var ids = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        return ids.Count > 0 ? ids : null;
    }
}
