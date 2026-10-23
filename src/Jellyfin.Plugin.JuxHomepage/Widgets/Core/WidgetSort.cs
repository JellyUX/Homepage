using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// The card orders an administrator can pick for a library section (TODO_V4.md 8.1, F-53), stored
/// as <see cref="WidgetConfig.SortBy"/> and <see cref="WidgetConfig.SortOrder"/>, and the orders the
/// Watchlist, History and Progress tabs ask for: one vocabulary for both (QUAL-06.b).
/// </summary>
public static class WidgetSort
{
    /// <summary>Alphabetical order, the order every library section had before sorting was configurable.</summary>
    public const string SortName = "SortName";

    /// <summary>Date the item was added to the library.</summary>
    public const string DateCreated = "DateCreated";

    /// <summary>Release date.</summary>
    public const string PremiereDate = "PremiereDate";

    /// <summary>Community rating.</summary>
    public const string CommunityRating = "CommunityRating";

    /// <summary>Date the user last played the item, the default order of the History and Progress tabs.</summary>
    public const string LastPlayed = "LastPlayed";

    /// <summary>A shuffle drawn once a day per user and section, the same on every load of that day.</summary>
    public const string Random = "Random";

    /// <summary>Ascending order.</summary>
    public const string Ascending = "Ascending";

    /// <summary>Descending order.</summary>
    public const string Descending = "Descending";

    private static readonly Dictionary<string, ItemSortBy> _sortFields = new(StringComparer.Ordinal)
    {
        [SortName] = ItemSortBy.SortName,
        [DateCreated] = ItemSortBy.DateCreated,
        [PremiereDate] = ItemSortBy.PremiereDate,
        [CommunityRating] = ItemSortBy.CommunityRating
    };

    /// <summary>
    /// Returns <paramref name="sortBy"/> when it is a known order, otherwise <see cref="SortName"/>.
    /// </summary>
    /// <param name="sortBy">The stored value.</param>
    /// <returns>A known sort value.</returns>
    public static string NormalizeSortBy(string? sortBy) =>
        sortBy == Random || (sortBy is not null && _sortFields.ContainsKey(sortBy)) ? sortBy : SortName;

    /// <summary>
    /// Returns <paramref name="sortOrder"/> when it is <see cref="Descending"/>, otherwise <see cref="Ascending"/>.
    /// </summary>
    /// <param name="sortOrder">The stored value.</param>
    /// <returns>A known sort order.</returns>
    public static string NormalizeSortOrder(string? sortOrder) =>
        sortOrder == Descending ? Descending : Ascending;

    /// <summary>
    /// Returns whether a query parameter names <paramref name="sort"/>, ignoring case as the tab
    /// endpoints always have.
    /// </summary>
    /// <param name="value">The query parameter.</param>
    /// <param name="sort">A <see cref="WidgetSort"/> constant.</param>
    /// <returns>True when they match.</returns>
    internal static bool Matches(string? value, string sort) =>
        string.Equals(value, sort, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the query order of a non-random sort, ties broken by name so equal dates or ratings
    /// keep a stable order from one load to the next.
    /// </summary>
    /// <param name="sortBy">A sort value other than <see cref="Random"/>.</param>
    /// <param name="sortOrder">The sort order.</param>
    /// <returns>The <c>OrderBy</c> of an items query.</returns>
    internal static (ItemSortBy OrderBy, SortOrder SortOrder)[] ToOrderBy(string? sortBy, string? sortOrder)
    {
        var field = _sortFields.GetValueOrDefault(NormalizeSortBy(sortBy), ItemSortBy.SortName);
        var order = NormalizeSortOrder(sortOrder) == Descending ? SortOrder.Descending : SortOrder.Ascending;
        return field == ItemSortBy.SortName
            ? [(field, order)]
            : [(field, order), (ItemSortBy.SortName, SortOrder.Ascending)];
    }
}
