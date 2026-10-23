using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// What a featured section (KTW-01) reads differently from a row of cards: richer items, its titles
/// with a backdrop first, and only titles at or above the row's minimum rating (KTW-02, D-07). A row of
/// cards is left exactly as it was (<see cref="WidgetPayload.Featured"/> false).
/// <para>
/// A title without a backdrop is moved after those that have one, not dropped: the banner shows the
/// first ones (its slides), and a section with fewer than 3 of them shows all its cards, counted
/// against MinItems like any row (WidgetLayoutResolverTests
/// BuildDescriptors_FeaturedSectionWithFewBackdrops_KeepsItsTitlesForTheCards).
/// </para>
/// </summary>
public static class FeaturedItems
{
    /// <summary>Returns the DTO options of the section <paramref name="payload"/> reads.</summary>
    /// <param name="payload">The request.</param>
    /// <returns><see cref="WidgetDtoOptions.Featured"/> for a featured section, otherwise <see cref="WidgetDtoOptions.Standard"/>.</returns>
    public static DtoOptions DtoOptionsFor(WidgetPayload payload) =>
        payload.Featured ? WidgetDtoOptions.Featured() : WidgetDtoOptions.Standard();

    /// <summary>
    /// Applies the minimum rating of a featured section to its items query; leaves any other query unchanged.
    /// </summary>
    /// <param name="query">The section's items query.</param>
    /// <param name="payload">The request.</param>
    public static void Restrict(InternalItemsQuery query, WidgetPayload payload)
    {
        if (payload.Featured && payload.FeaturedMinRating > 0)
        {
            // The higher bound wins: Top Rated keeps its own floor below the row's.
            query.MinCommunityRating = Math.Max(query.MinCommunityRating ?? 0, payload.FeaturedMinRating);
        }
    }

    /// <summary>
    /// Returns whether a featured section keeps <paramref name="item"/>: the in-memory form of
    /// <see cref="Restrict"/>, for sections that page a list instead of querying.
    /// </summary>
    /// <param name="item">A candidate item.</param>
    /// <param name="payload">The request.</param>
    /// <returns>True for every item of a row of cards.</returns>
    public static bool Keeps(BaseItem item, WidgetPayload payload) =>
        !payload.Featured || payload.FeaturedMinRating <= 0 || item.CommunityRating >= payload.FeaturedMinRating;

    /// <summary>
    /// Returns the items of a featured section with those that have a backdrop first, each group in its
    /// own order; the items of a row of cards as they are.
    /// </summary>
    /// <param name="items">The items, in the section's order.</param>
    /// <param name="payload">The request.</param>
    /// <returns>The items to show.</returns>
    public static IReadOnlyList<BaseItem> BackdropsFirst(IReadOnlyList<BaseItem> items, WidgetPayload payload) =>
        payload.Featured
            ? [.. items.Where(HasBackdrop), .. items.Where(item => !HasBackdrop(item))]
            : items;

    private static bool HasBackdrop(BaseItem item) => item.HasImage(ImageType.Backdrop, 0);
}
