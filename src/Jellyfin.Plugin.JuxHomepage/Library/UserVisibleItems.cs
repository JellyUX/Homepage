using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
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
        out int unresolvedCount) =>
        Check(libraryManager, user, ids, new InternalItemsQuery(), out unresolvedCount);

    /// <summary>
    /// Returns, in input order, the ids of <paramref name="ids"/> that <paramref name="user"/> may open on
    /// their own, checked like <see cref="Resolve"/> but from a lean read: no field, image nor user data
    /// (PERF-03, DR-007). A long cached list (the whole watch history) is checked and counted this way, and
    /// only its page is read again by <see cref="LoadPage"/> to be projected. The lean items never leave
    /// this method: projected, they would lack the fields and user data of a full read.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="user">The user the items are returned to.</param>
    /// <param name="ids">The cached ids, in the order the caller wants back.</param>
    /// <param name="unresolvedCount">The number of ids that no longer resolve to a library item.</param>
    /// <returns>The visible ids, in the order of <paramref name="ids"/>.</returns>
    public static IReadOnlyList<Guid> VisibleIds(
        ILibraryManager libraryManager,
        User user,
        IReadOnlyList<Guid> ids,
        out int unresolvedCount)
    {
        var lean = new InternalItemsQuery { DtoOptions = new DtoOptions(false) { EnableImages = false, EnableUserData = false } };
        return [.. Check(libraryManager, user, ids, lean, out unresolvedCount).Select(item => item.Id)];
    }

    /// <summary>
    /// Reads the items of a page of ids <see cref="VisibleIds"/> returned, with the same query a single
    /// read used (so the projected bytes do not change), in the order of <paramref name="pageIds"/>. An item
    /// removed between the two reads is left out.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="pageIds">The visible ids of the page, in the order the caller wants back.</param>
    /// <returns>The items of the page, ready to be projected.</returns>
    public static IReadOnlyList<BaseItem> LoadPage(ILibraryManager libraryManager, IReadOnlyList<Guid> pageIds)
    {
        if (pageIds.Count == 0)
        {
            return [];
        }

        var itemsById = libraryManager
            .GetItemList(new InternalItemsQuery { ItemIds = pageIds.Distinct().ToArray() })
            .ToDictionary(i => i.Id);
        var page = new List<BaseItem>(pageIds.Count);
        foreach (var id in pageIds)
        {
            if (itemsById.TryGetValue(id, out var item))
            {
                page.Add(item);
            }
        }

        return page;
    }

    // Reads the ids with the given query and keeps, in input order, the items the user may open on their own.
    private static List<BaseItem> Check(
        ILibraryManager libraryManager,
        User user,
        IReadOnlyList<Guid> ids,
        InternalItemsQuery query,
        out int unresolvedCount)
    {
        unresolvedCount = 0;
        if (ids.Count == 0)
        {
            return [];
        }

        // GetItemList with ItemIds does not preserve the input order, hence the dictionary.
        query.ItemIds = ids.Distinct().ToArray();
        var itemsById = libraryManager.GetItemList(query).ToDictionary(i => i.Id);

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
