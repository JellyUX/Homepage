using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// The local library's items of some kinds, keyed by each of their provider ids (PERF-04): built
/// with one library read, it matches external items in memory instead of one library query per
/// item and provider. Provider names and id values are compared ignoring case. When several items
/// carry the same id (versions of one movie, duplicates), the primary version wins, then the lowest
/// item id, so a match does not depend on the order the library returns. Only each item's id and
/// version rank are kept, not the items read.
/// </summary>
public sealed class LibraryProviderIndex
{
    private readonly Dictionary<string, Dictionary<string, Entry>> _byProvider = new(StringComparer.OrdinalIgnoreCase);

    private LibraryProviderIndex(IEnumerable<BaseItem> items)
    {
        foreach (var item in items)
        {
            var entry = new Entry(item.Id, item is not Video { PrimaryVersionId: { Length: > 0 } });
            foreach (var (provider, value) in item.ProviderIds)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!_byProvider.TryGetValue(provider, out var byValue))
                {
                    byValue = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                    _byProvider[provider] = byValue;
                }

                var key = value.Trim();
                if (!byValue.TryGetValue(key, out var kept) || Precedes(entry, kept))
                {
                    byValue[key] = entry;
                }
            }
        }
    }

    /// <summary>Gets the number of items read to build the index.</summary>
    public int ItemCount { get; private init; }

    /// <summary>
    /// Reads the library's items of the given kinds with their provider ids only (no images, no user
    /// data, no other field) and indexes them.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="includeItemTypes">The item kinds to index.</param>
    /// <returns>The index.</returns>
    public static LibraryProviderIndex Build(ILibraryManager libraryManager, BaseItemKind[] includeItemTypes)
    {
        var items = libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = includeItemTypes,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
            {
                Fields = [ItemFields.ProviderIds],
                EnableImages = false,
                EnableUserData = false
            }
        });

        return new LibraryProviderIndex(items) { ItemCount = items.Count };
    }

    /// <summary>Returns the id of the library item carrying the given provider id, if any.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="value">The provider id value.</param>
    /// <returns>The matching item id, or null.</returns>
    public Guid? Find(MetadataProvider provider, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && _byProvider.TryGetValue(provider.ToString(), out var byValue)
        && byValue.TryGetValue(value.Trim(), out var entry)
            ? entry.Id
            : null;

    private static bool Precedes(Entry candidate, Entry kept) =>
        candidate.Primary != kept.Primary ? candidate.Primary : candidate.Id.CompareTo(kept.Id) < 0;

    private readonly record struct Entry(Guid Id, bool Primary);
}
