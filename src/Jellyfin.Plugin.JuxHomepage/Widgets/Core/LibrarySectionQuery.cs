using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Builds the items query every library section starts from: the user's whole library, real items
/// only, and no total count (F-05: the count alone cost 29 to 600 ms per section). The section then
/// adds its own filter.
/// </summary>
public static class LibrarySectionQuery
{
    /// <summary>
    /// Creates the base items query of a library section.
    /// </summary>
    /// <param name="user">The user the items are returned to; their library access applies.</param>
    /// <param name="dtoOptions">The DTO options of the returned items.</param>
    /// <param name="itemTypes">The item types of the section, or null for every type.</param>
    /// <returns>A new query, recursive, without missing items nor total count.</returns>
    public static InternalItemsQuery Create(User user, DtoOptions dtoOptions, BaseItemKind[]? itemTypes = null)
    {
        var query = new InternalItemsQuery(user)
        {
            Recursive = true,
            IsMissing = false,
            EnableTotalRecordCount = false,
            DtoOptions = dtoOptions
        };

        if (itemTypes is not null)
        {
            query.IncludeItemTypes = itemTypes;
        }

        return query;
    }
}
