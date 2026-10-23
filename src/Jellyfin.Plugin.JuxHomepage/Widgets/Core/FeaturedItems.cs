using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// What a featured section (KTW-01) reads differently from a row of cards: richer items, and only
/// items with a backdrop at or above the row's minimum rating (KTW-02, D-07). A row of cards is left
/// exactly as it was (<see cref="WidgetPayload.Featured"/> false).
/// <para>
/// Items without a backdrop are filtered out of the whole section, not only its first slides: a
/// banner cannot show them, and a filtered query keeps every page consistent. The filter is
/// <see cref="InternalItemsQuery.ImageTypes"/>, which Jellyfin applies; <c>HasBackdrop</c> is ignored
/// by the 10.11 items query (measured on 10.11.10: 106 items with and without it, 102 with
/// <c>ImageTypes=Backdrop</c>).
/// </para>
/// </summary>
public static class FeaturedItems
{
    /// <summary>Returns the DTO options of the section <paramref name="payload"/> reads.</summary>
    /// <param name="payload">The request.</param>
    /// <returns><see cref="WidgetDtoOptions.Featured"/> for a featured section, otherwise <see cref="WidgetDtoOptions.Standard"/>.</returns>
    public static DtoOptions DtoOptionsFor(WidgetPayload payload) =>
        payload.Featured ? WidgetDtoOptions.Featured() : WidgetDtoOptions.Standard();

    /// <summary>Restricts the items query of a featured section; leaves any other query unchanged.</summary>
    /// <param name="query">The section's items query.</param>
    /// <param name="payload">The request.</param>
    public static void Restrict(InternalItemsQuery query, WidgetPayload payload)
    {
        if (!payload.Featured)
        {
            return;
        }

        query.ImageTypes = [ImageType.Backdrop];
        if (payload.FeaturedMinRating > 0)
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
        !payload.Featured
        || (item.HasImage(ImageType.Backdrop, 0)
            && (payload.FeaturedMinRating <= 0 || item.CommunityRating >= payload.FeaturedMinRating));
}
