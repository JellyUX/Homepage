using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JuxHomepage.Library;

/// <summary>
/// Resolves cached item ids into the items a given user is allowed to see. Cached id lists (TMDb
/// matches, watch history, collection membership) are computed without a user, so every read must
/// re-apply the user's library access and parental rules before returning items or counting them.
/// </summary>
internal static class UserVisibleItems
{
    /// <summary>
    /// Resolves <paramref name="ids"/> in one batched query and keeps, in input order, only the items
    /// <paramref name="user"/> may open on their own (<see cref="BaseItem.IsVisibleStandalone"/>: parental
    /// rating, blocked tags and library access). An <see cref="InternalItemsQuery"/> built with the user
    /// is not enough here: Jellyfin skips the library-access restriction whenever
    /// <see cref="InternalItemsQuery.ItemIds"/> is set (<c>LibraryManager.AddUserToQuery</c>, 10.11).
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="user">The user the items are returned to.</param>
    /// <param name="ids">The cached ids, in the order the caller wants back.</param>
    /// <param name="unresolvedCount">The number of ids that no longer resolve to a library item.</param>
    /// <returns>The visible items, in the order of <paramref name="ids"/>.</returns>
    public static IReadOnlyList<BaseItem> Resolve(
        ILibraryManager libraryManager,
        User user,
        IReadOnlyList<Guid> ids,
        out int unresolvedCount)
    {
        unresolvedCount = 0;
        if (ids.Count == 0)
        {
            return [];
        }

        // GetItemList with ItemIds does not preserve the input order, hence the dictionary.
        var itemsById = libraryManager
            .GetItemList(new InternalItemsQuery { ItemIds = ids.Distinct().ToArray() })
            .ToDictionary(i => i.Id);

        var visible = new List<BaseItem>(ids.Count);
        foreach (var id in ids)
        {
            if (!itemsById.TryGetValue(id, out var item))
            {
                unresolvedCount++;
            }
            else if (item.IsVisibleStandalone(user))
            {
                visible.Add(item);
            }
        }

        return visible;
    }
}
