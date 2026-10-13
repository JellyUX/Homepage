using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.JuxHomepage.Watchlist;

/// <summary>
/// The name filter of the tab search (TODO_V4.md 8.5, F-55): case and accents are ignored, like
/// Jellyfin's own <c>NameContains</c>, so a search finds the same titles in every tab.
/// </summary>
internal static class NameSearch
{
    /// <summary>The longest search term kept; longer input is cut.</summary>
    public const int MaxTermLength = 100;

    /// <summary>
    /// Returns the trimmed term, cut to <see cref="MaxTermLength"/>, or null when nothing is left to
    /// search for.
    /// </summary>
    /// <param name="searchTerm">The raw query parameter.</param>
    /// <returns>The term to filter by, or null.</returns>
    public static string? Normalize(string? searchTerm)
    {
        var term = searchTerm?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return null;
        }

        return term.Length > MaxTermLength ? term[..MaxTermLength] : term;
    }

    /// <summary>
    /// The term for <c>InternalItemsQuery.NameContains</c>, which Jellyfin compares with the item's
    /// <c>CleanName</c> (lower case, no accents) without cleaning the term itself: a raw "Colère"
    /// matched nothing, "colere" matched (8096, 2026-10-13).
    /// </summary>
    /// <param name="searchTerm">The raw query parameter.</param>
    /// <returns>The folded term, or null when nothing is left to search for.</returns>
    public static string? ForNameContains(string? searchTerm)
    {
        var term = Normalize(searchTerm);
        return term is null ? null : Fold(term);
    }

    /// <summary>Keeps the entries whose name contains <paramref name="term"/>, in their order.</summary>
    /// <typeparam name="T">The cached entry type.</typeparam>
    /// <param name="entries">The entries to filter.</param>
    /// <param name="nameOf">The name of an entry.</param>
    /// <param name="term">A term from <see cref="Normalize"/>; null keeps every entry.</param>
    /// <returns>The matching entries.</returns>
    public static IReadOnlyList<T> Filter<T>(IReadOnlyList<T> entries, Func<T, string?> nameOf, string? term)
    {
        if (term is null)
        {
            return entries;
        }

        var folded = Fold(term);
        return entries.Where(e => Fold(nameOf(e) ?? string.Empty).Contains(folded, StringComparison.Ordinal)).ToList();
    }

    private static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
