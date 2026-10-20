using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.TMDb;

// Unit-level coverage of LibraryCrossReferencer and LibraryProviderIndex in isolation (mocking only
// ILibraryManager, no ITMDbApiClient or disk cache involved). TMDbCacheServiceTests.cs covers the
// same matching end-to-end through the full refresh pipeline.
public sealed class LibraryCrossReferencerTests
{
    [Fact]
    public async Task CrossReferenceAsync_ImdbMatchFound_SetsLibraryItemIdAndCountsAsMatched()
    {
        var libraryItem = MovieWith(MetadataProvider.Imdb, "tt1375666");
        var referencer = Referencer(libraryItem);
        var items = new List<TMDbMovie> { new() { Id = 27205, Title = "Inception" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>("tt1375666"),
            referencer.BuildIndex([BaseItemKind.Movie]),
            CancellationToken.None);

        Assert.Equal(1, matched);
        Assert.Equal(libraryItem.Id, items[0].LibraryItemId);
    }

    [Fact]
    public async Task CrossReferenceAsync_NoImdbId_FallsBackToProviderId()
    {
        var libraryItem = MovieWith(MetadataProvider.Tmdb, "27205");
        var referencer = Referencer(libraryItem);
        var items = new List<TMDbMovie> { new() { Id = 27205, Title = "Inception" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>(null),
            referencer.BuildIndex([BaseItemKind.Movie]),
            CancellationToken.None);

        Assert.Equal(1, matched);
        Assert.Equal(libraryItem.Id, items[0].LibraryItemId);
    }

    [Fact]
    public async Task CrossReferenceAsync_NoMatchAtAll_LibraryItemIdStaysNullAndNotCountedAsMatched()
    {
        var referencer = Referencer(MovieWith(MetadataProvider.Tmdb, "1"));
        var items = new List<TMDbMovie> { new() { Id = 999, Title = "Unknown" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>("tt0000999"),
            referencer.BuildIndex([BaseItemKind.Movie]),
            CancellationToken.None);

        Assert.Equal(0, matched);
        Assert.Null(items[0].LibraryItemId);
    }

    [Fact]
    public async Task CrossReferenceAsync_NoImdbIdAndNullFallbackProvider_DoesNotMatchOnTheProviderId()
    {
        // Regression test for TODO_V2.md Phase 14: a Wikidata-backed caller has no MetadataProvider
        // to fall back on (its own Id is a parsed Q-id, not a TMDb id), so passing
        // fallbackProvider: null must skip the provider-id lookup entirely rather than defaulting to
        // MetadataProvider.Tmdb -- which would risk a spurious match against an unrelated item.
        var referencer = Referencer(MovieWith(MetadataProvider.Tmdb, "550"));
        var items = new List<TMDbMovie> { new() { Id = 550, Title = "Fight Club" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) => Task.FromResult<string?>(null),
            referencer.BuildIndex([BaseItemKind.Movie]),
            CancellationToken.None,
            fallbackProvider: null);

        Assert.Equal(0, matched);
        Assert.Null(items[0].LibraryItemId);
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

        var referencer = Referencer();
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
            referencer.BuildIndex([BaseItemKind.Movie]),
            CancellationToken.None);

        Assert.Equal(0, matched);
        Assert.True(maxObservedInFlight <= MaxConcurrency, $"Observed {maxObservedInFlight} concurrent calls, expected at most {MaxConcurrency}.");
    }

    [Fact]
    public async Task CrossReferenceAsync_LocalTmdbMatch_MakesNoImdbLookup()
    {
        // F-07: an item found locally by its TMDb id costs no HTTP call.
        var libraryItem = MovieWith(MetadataProvider.Tmdb, "27205");
        var lookups = 0;
        var referencer = Referencer(libraryItem);
        var items = new List<TMDbMovie> { new() { Id = 27205, Title = "Inception" } };

        var matched = await referencer.CrossReferenceAsync(
            items,
            (_, _) =>
            {
                Interlocked.Increment(ref lookups);
                return Task.FromResult<string?>("tt1375666");
            },
            referencer.BuildIndex([BaseItemKind.Movie]),
            CancellationToken.None);

        Assert.Equal(1, matched);
        Assert.Equal(libraryItem.Id, items[0].LibraryItemId);
        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task CrossReference_400Items_UsesAtMostTwoLibraryQueries()
    {
        // PERF-04: one library query per item (two when an IMDb id is found) cost hundreds of
        // queries per refresh. The index reads the library once, then every item is matched in memory.
        var library = new TestMocks.CountingLibraryManager();
        library.Inner.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns([MovieWith(MetadataProvider.Imdb, "tt0000002"), MovieWith(MetadataProvider.Tmdb, "3")]);
        var referencer = new LibraryCrossReferencer(library.Object, NullLogger.Instance);
        var items = Enumerable.Range(1, 400)
            .Select(id => new TMDbMovie { Id = id, Title = $"Movie {id}" })
            .ToList();

        var matched = await referencer.CrossReferenceAsync(
            items,
            (id, _) => Task.FromResult<string?>(id % 2 == 0 ? $"tt{id:D7}" : null),
            referencer.BuildIndex([BaseItemKind.Movie]),
            CancellationToken.None);

        Assert.Equal(2, matched);
        Assert.True(library.Total <= 2, $"Expected at most 2 library queries, got {library.Total} ({string.Join(", ", library.Counts)}).");
    }

    [Fact]
    public void BuildIndex_ReadsProviderIdsOnly_WithoutImagesOrUserData()
    {
        InternalItemsQuery? query = null;
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => query = q)
            .Returns([]);

        new LibraryCrossReferencer(library.Object, NullLogger.Instance).BuildIndex([BaseItemKind.Series]);

        Assert.NotNull(query);
        Assert.Equal([BaseItemKind.Series], query.IncludeItemTypes);
        Assert.True(query.Recursive);
        Assert.Equal([ItemFields.ProviderIds], query.DtoOptions.Fields);
        Assert.False(query.DtoOptions.EnableImages);
        Assert.False(query.DtoOptions.EnableUserData);
    }

    [Fact]
    public void Find_ProviderAndValueInAnotherCase_StillMatches()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        movie.ProviderIds["IMDB"] = " TT1375666 ";
        var index = Referencer(movie).BuildIndex([BaseItemKind.Movie]);

        Assert.Equal(movie.Id, index.Find(MetadataProvider.Imdb, "tt1375666"));
    }

    [Fact]
    public void Find_TwoVersionsShareTheId_ReturnsThePrimaryVersionWhateverTheOrder()
    {
        var primary = MovieWith(MetadataProvider.Tmdb, "27205");
        var alternate = MovieWith(MetadataProvider.Tmdb, "27205");
        alternate.PrimaryVersionId = primary.Id.ToString("N", CultureInfo.InvariantCulture);

        Assert.Equal(primary.Id, Referencer(primary, alternate).BuildIndex([BaseItemKind.Movie]).Find(MetadataProvider.Tmdb, "27205"));
        Assert.Equal(primary.Id, Referencer(alternate, primary).BuildIndex([BaseItemKind.Movie]).Find(MetadataProvider.Tmdb, "27205"));
    }

    [Fact]
    public void Find_TwoDuplicatesShareTheId_ReturnsTheLowestItemIdWhateverTheOrder()
    {
        var first = new Movie { Id = new Guid("00000000-0000-0000-0000-000000000001") };
        var second = new Movie { Id = new Guid("00000000-0000-0000-0000-000000000002") };
        first.SetProviderId(MetadataProvider.Imdb, "tt1375666");
        second.SetProviderId(MetadataProvider.Imdb, "tt1375666");

        Assert.Equal(first.Id, Referencer(second, first).BuildIndex([BaseItemKind.Movie]).Find(MetadataProvider.Imdb, "tt1375666"));
    }

    private static Movie MovieWith(MetadataProvider provider, string value)
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        movie.SetProviderId(provider, value);
        return movie;
    }

    private static LibraryCrossReferencer Referencer(params BaseItem[] libraryItems)
    {
        var library = new Mock<ILibraryManager>();
        library.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([.. libraryItems]);
        return new LibraryCrossReferencer(library.Object, NullLogger.Instance);
    }
}
