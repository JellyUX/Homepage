using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.TMDb;

// Unit-level coverage of LibraryCrossReferencer in isolation (mocking only ILibraryManager, no
// ITMDbApiClient or disk cache involved), extracted from TMDbCacheService in Phase 7.3 of
// TODO_V2.md. TMDbCacheServiceTests.cs's existing cross-referencing tests (ImdbMatchFound,
// NoImdbMatch fallback, NoMatchAtAll, bounded concurrency) remain unchanged and continue to cover
// the same logic end-to-end through the full refresh pipeline -- the two files intentionally
// coexist, one at the integration level, one at the unit level.
public sealed class LibraryCrossReferencerTests
{
    [Fact]
    public async Task CrossReferenceAsync_ImdbMatchFound_SetsLibraryItemIdAndCountsAsMatched()
    {
        var libraryItem = new Movie { Id = Guid.NewGuid(), Name = "Inception" };
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Imdb"))))
            .Returns([libraryItem.Id]);

        var referencer = new LibraryCrossReferencer(libraryManagerMock.Object, NullLogger.Instance);
        var items = new List<TMDbMovie> { new() { Id = 27205, Title = "Inception" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>("tt1375666"),
            [BaseItemKind.Movie],
            CancellationToken.None);

        Assert.Equal(1, matched);
        Assert.Equal(libraryItem.Id, items[0].LibraryItemId);
    }

    [Fact]
    public async Task CrossReferenceAsync_NoImdbId_FallsBackToProviderId()
    {
        var libraryItem = new Movie { Id = Guid.NewGuid(), Name = "Inception" };
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Tmdb")
                     && q.HasAnyProviderId["Tmdb"] == "27205")))
            .Returns([libraryItem.Id]);

        var referencer = new LibraryCrossReferencer(libraryManagerMock.Object, NullLogger.Instance);
        var items = new List<TMDbMovie> { new() { Id = 27205, Title = "Inception" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>(null),
            [BaseItemKind.Movie],
            CancellationToken.None);

        Assert.Equal(1, matched);
        Assert.Equal(libraryItem.Id, items[0].LibraryItemId);
    }

    [Fact]
    public async Task CrossReferenceAsync_NoMatchAtAll_LibraryItemIdStaysNullAndNotCountedAsMatched()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);

        var referencer = new LibraryCrossReferencer(libraryManagerMock.Object, NullLogger.Instance);
        var items = new List<TMDbMovie> { new() { Id = 999, Title = "Unknown" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>(null),
            [BaseItemKind.Movie],
            CancellationToken.None);

        Assert.Equal(0, matched);
        Assert.Null(items[0].LibraryItemId);
    }

    [Fact]
    public async Task CrossReferenceAsync_NoImdbIdAndNullFallbackProvider_DoesNotQueryAnyProviderId()
    {
        // Regression test for TODO_V2.md Phase 14: a Wikidata-backed caller has no MetadataProvider
        // to fall back on (its own Id is a parsed Q-id, not a TMDb id), so passing
        // fallbackProvider: null must skip the provider-id lookup entirely rather than defaulting to
        // MetadataProvider.Tmdb -- which would risk a spurious match against an unrelated item.
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);

        var referencer = new LibraryCrossReferencer(libraryManagerMock.Object, NullLogger.Instance);
        var items = new List<TMDbMovie> { new() { Id = 550, Title = "Fight Club" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>(null),
            [BaseItemKind.Movie],
            CancellationToken.None,
            fallbackProvider: null);

        Assert.Equal(0, matched);
        libraryManagerMock.Verify(
            m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Tmdb"))),
            Times.Never);
    }

    [Fact]
    public async Task CrossReferenceAsync_ManyItems_RunsWithBoundedConcurrency()
    {
        const int ItemCount = 30;
        const int MaxConcurrency = 5;
        var perCallDelay = TimeSpan.FromMilliseconds(50);
        var inFlight = 0;
        var maxObservedInFlight = 0;
        var gate = new object();

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);

        var referencer = new LibraryCrossReferencer(libraryManagerMock.Object, NullLogger.Instance);
        var items = Enumerable.Range(1, ItemCount)
            .Select(i => new TMDbMovie { Id = i, Title = "Movie " + i.ToString(CultureInfo.InvariantCulture) })
            .ToList();

        var matched = await referencer.CrossReferenceAsync(
            items,
            async (_, ct) =>
            {
                lock (gate)
                {
                    inFlight++;
                    maxObservedInFlight = Math.Max(maxObservedInFlight, inFlight);
                }

                await Task.Delay(perCallDelay, ct).ConfigureAwait(false);

                lock (gate)
                {
                    inFlight--;
                }

                return null;
            },
            [BaseItemKind.Movie],
            CancellationToken.None);

        Assert.Equal(0, matched);
        Assert.True(maxObservedInFlight <= MaxConcurrency, $"Observed {maxObservedInFlight} concurrent calls, expected at most {MaxConcurrency}.");
    }

    [Fact]
    public async Task CrossReferenceAsync_LocalTmdbMatch_MakesNoImdbLookup()
    {
        // F-07: an item found locally by its TMDb id costs no HTTP call.
        var libraryItemId = Guid.NewGuid();
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(
                q => q.HasAnyProviderId != null && q.HasAnyProviderId.ContainsKey("Tmdb"))))
            .Returns([libraryItemId]);
        var lookups = 0;
        var referencer = new LibraryCrossReferencer(libraryManagerMock.Object, NullLogger.Instance);
        var items = new List<TMDbMovie> { new() { Id = 27205, Title = "Inception" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) =>
            {
                Interlocked.Increment(ref lookups);
                return Task.FromResult<string?>("tt1375666");
            },
            [BaseItemKind.Movie],
            CancellationToken.None);

        Assert.Equal(1, matched);
        Assert.Equal(libraryItemId, items[0].LibraryItemId);
        Assert.Equal(0, lookups);
    }

    [Fact(Skip = "PERF-04, phase 06: one library query per item, 600 for 400 items; un-skip with in-memory matching.")]
    public async Task CrossReference_400Items_UsesAtMostTwoLibraryQueries()
    {
        // PERF-04: one library query per item (two when an IMDb id is found) costs hundreds of
        // queries per refresh. Matching in memory needs at most one read per id provider. This test
        // bounds the number of library calls only; the matching contract is rewritten with the fix.
        var library = new TestMocks.CountingLibraryManager();
        var referencer = new LibraryCrossReferencer(library.Object, NullLogger.Instance);
        var items = Enumerable.Range(1, 400)
            .Select(id => new TMDbMovie { Id = id, Title = $"Movie {id}" })
            .ToList();

        await referencer.CrossReferenceAsync(
            items,
            (id, _) => Task.FromResult<string?>(id % 2 == 0 ? $"tt{id:D7}" : null),
            [BaseItemKind.Movie],
            CancellationToken.None);

        Assert.True(library.Total <= 2, $"Expected at most 2 library queries, got {library.Total} ({string.Join(", ", library.Counts)}).");
    }
}
