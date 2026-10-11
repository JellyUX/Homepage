using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Personalized;

public sealed class PersonalizedWidgetTests
{
    // Real LocalizationService (not a mock) with English entries for every personalized ".format"
    // template, matching the literal English text these widgets used before i18n. Keeps existing
    // assertions ("More Action", "Directed by Christopher Nolan", ...) unchanged.
    private static readonly ILocalizationService TestLocalization = new LocalizationService(
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>
            {
                ["jux.personalized.favorite-genre.format"] = "More {value}",
                ["jux.personalized.favorite-actor.format"] = "Movies with {value}",
                ["jux.personalized.favorite-director.format"] = "Directed by {value}",
                ["jux.personalized.because-you-watched.format"] = "Because you watched {value}"
            }
        });

    // Builds a real ScoringService backed by mocked Jellyfin services, so FavoriteGenreWidget's
    // rank resolution (Resolve) exercises the real scoring pipeline with controlled watch history.
    private static ScoringService BuildScoringService(
        User user,
        IReadOnlyList<BaseItem> watched,
        IReadOnlyList<BaseItem>? watchedSeries = null)
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        // Each watched series is reached through one played episode, as ScoringService does.
        var series = watchedSeries ?? [];
        foreach (var s in series.Where(s => s.Id == Guid.Empty))
        {
            s.Id = Guid.NewGuid();
        }

        var episodes = series.Select(s => (BaseItem)new Episode { Id = Guid.NewGuid(), SeriesId = s.Id }).ToList();

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns(watched);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Episode }))))
            .Returns(episodes);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Series }))))
            .Returns((InternalItemsQuery q) => series.Where(s => q.ItemIds.Contains(s.Id)).ToList());
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IsFavorite == true)))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetPeople(It.IsAny<BaseItem>()))
            .Returns([]);

        return new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration());
    }

    private static FavoriteGenreWidget BuildWidget(
        ScoringService scoringService,
        Mock<IUserManager>? userManagerMock = null,
        Mock<ILibraryManager>? libraryManagerMock = null,
        Mock<IDtoService>? dtoServiceMock = null) =>
        new(
            (userManagerMock ?? new Mock<IUserManager>()).Object,
            (libraryManagerMock ?? new Mock<ILibraryManager>()).Object,
            (dtoServiceMock ?? new Mock<IDtoService>()).Object,
            scoringService,
            TestLocalization);

    // -------------------------------------------------------------------------
    // Descriptor
    // -------------------------------------------------------------------------

    [Fact]
    public void FavoriteGenre_GetDescriptor_HasExpectedProperties()
    {
        var user = new User("test", "Default", "Default");
        var widget = BuildWidget(BuildScoringService(user, []));

        var d = widget.GetDescriptor();

        Assert.Equal("jux.personalized.favorite-genre", d.WidgetType);
        Assert.Equal(WidgetCategory.Personalized, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
        Assert.Null(d.AdditionalData);
    }

    // -------------------------------------------------------------------------
    // Resolve rank resolution (Phase 8 of TODO_V2.md -- replaces the old
    // fan-out-per-scored-value model: each call now resolves exactly one rank).
    // -------------------------------------------------------------------------

    [Fact]
    public void FavoriteGenre_Resolve_RankAvailable_AssignsCorrectValue()
    {
        var user = new User("test", "Default", "Default");
        var action1 = new Movie { Id = Guid.NewGuid(), Name = "A1", Genres = ["Action"] };
        var action2 = new Movie { Id = Guid.NewGuid(), Name = "A2", Genres = ["Action"] };
        var drama1 = new Movie { Id = Guid.NewGuid(), Name = "D1", Genres = ["Drama"] };

        var widget = BuildWidget(BuildScoringService(user, [action1, action2, drama1]));

        var rank1 = widget.Resolve(user.Id, new WidgetInstanceConfig(), 1);
        var rank2 = widget.Resolve(user.Id, new WidgetInstanceConfig(), 2);

        Assert.NotNull(rank1);
        Assert.NotNull(rank2);

        var rank1Descriptor = rank1.GetDescriptor();
        Assert.Equal("Action", rank1Descriptor.AdditionalData);
        Assert.Equal("More Action", rank1Descriptor.DisplayName);

        var rank2Descriptor = rank2.GetDescriptor();
        Assert.Equal("Drama", rank2Descriptor.AdditionalData);
        Assert.Equal("More Drama", rank2Descriptor.DisplayName);
    }

    [Fact]
    public void FavoriteGenre_Resolve_RankBeyondAvailableValues_ReturnsNull()
    {
        var user = new User("test", "Default", "Default");
        var action1 = new Movie { Id = Guid.NewGuid(), Name = "A1", Genres = ["Action"] };
        var drama1 = new Movie { Id = Guid.NewGuid(), Name = "D1", Genres = ["Drama"] };

        // Only 2 distinct genres exist -- rank 3 must resolve to null, not fall back to the
        // last available genre (that would silently recreate the V1 duplicate-section bug).
        var widget = BuildWidget(BuildScoringService(user, [action1, drama1]));

        var resolved = widget.Resolve(user.Id, new WidgetInstanceConfig(), 3);

        Assert.Null(resolved);
    }

    [Fact]
    public void FavoriteGenre_Resolve_NoWatchHistory_ReturnsNull()
    {
        var user = new User("test", "Default", "Default");
        var widget = BuildWidget(BuildScoringService(user, []));

        var resolved = widget.Resolve(user.Id, new WidgetInstanceConfig(), 1);

        Assert.Null(resolved);
    }

    // -------------------------------------------------------------------------
    // GetItemsAsync / ApplyFilter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FavoriteGenre_GetItemsAsync_AppliesGenreFilterAndExcludesWatchedByDefault()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([]);

        var widget = BuildWidget(
            BuildScoringService(user, []),
            userManagerMock,
            libraryManagerMock,
            dtoServiceMock);

        await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, AdditionalData = "Action" },
            CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal(["Action"], capturedQuery!.Genres);
        Assert.True(capturedQuery.IsPlayed == false);
    }

    [Fact]
    public async Task FavoriteGenre_GetItemsAsync_ExcludeWatchedFalse_DoesNotFilterIsPlayed()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([]);

        var widget = BuildWidget(
            BuildScoringService(user, []),
            userManagerMock,
            libraryManagerMock,
            dtoServiceMock);

        await widget.GetItemsAsync(
            new WidgetPayload
            {
                UserId = user.Id,
                AdditionalData = "Action",
                ExtraParams = new Dictionary<string, string> { ["excludeWatched"] = "false" }
            },
            CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Null(capturedQuery!.IsPlayed);
    }

    [Fact]
    public async Task FavoriteGenre_EmptyAdditionalData_ReturnsEmpty()
    {
        var user = new User("test", "Default", "Default");
        var widget = BuildWidget(BuildScoringService(user, []));

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, AdditionalData = null },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task FavoriteGenre_UserNotFound_ReturnsEmpty()
    {
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var widget = BuildWidget(
            BuildScoringService(new User("test", "Default", "Default"), []),
            userManagerMock);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = Guid.NewGuid(), AdditionalData = "Action" },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
    }

    // -------------------------------------------------------------------------
    // FavoriteActorWidget / FavoriteDirectorWidget
    // -------------------------------------------------------------------------

    [Fact]
    public void FavoriteActor_GetDescriptor_HasExpectedProperties()
    {
        var user = new User("test", "Default", "Default");
        var widget = new FavoriteActorWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            BuildScoringService(user, []),
            TestLocalization);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.personalized.favorite-actor", d.WidgetType);
        Assert.Equal(WidgetCategory.Personalized, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
    }

    [Fact]
    public void FavoriteDirector_GetDescriptor_HasExpectedProperties()
    {
        var user = new User("test", "Default", "Default");
        var widget = new FavoriteDirectorWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            BuildScoringService(user, []),
            TestLocalization);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.personalized.favorite-director", d.WidgetType);
        Assert.Equal(WidgetCategory.Personalized, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
    }

    [Fact]
    public async Task FavoriteActor_GetItemsAsync_AppliesPersonAndPersonTypeFilter()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([]);

        var widget = new FavoriteActorWidget(
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            BuildScoringService(user, []),
            TestLocalization);

        await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, AdditionalData = "Brad Pitt" },
            CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal("Brad Pitt", capturedQuery!.Person);
        Assert.Equal(["Actor"], capturedQuery.PersonTypes);
    }

    [Fact]
    public void FavoriteDirector_Resolve_UsesTopDirectorsNotActors()
    {
        var user = new User("test", "Default", "Default");
        var film1 = new Movie { Id = Guid.NewGuid(), Name = "F1", Genres = [] };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
            .Returns([film1]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IsFavorite != true && q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Series }))))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(
                q => q.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Episode }))))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IsFavorite == true)))
            .Returns([]);
        libraryManagerMock
            .Setup(m => m.GetPeople(It.IsAny<BaseItem>()))
            .Returns([new PersonInfo { Name = "Christopher Nolan", Type = PersonKind.Director }]);

        var scoringService = new ScoringService(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IUserDataManager>().Object,
            () => new PluginConfiguration());

        var widget = new FavoriteDirectorWidget(
            userManagerMock.Object,
            libraryManagerMock.Object,
            new Mock<IDtoService>().Object,
            scoringService,
            TestLocalization);

        var resolved = widget.Resolve(user.Id, new WidgetInstanceConfig(), 1);

        Assert.NotNull(resolved);
        var descriptor = resolved.GetDescriptor();
        Assert.Equal("Christopher Nolan", descriptor.AdditionalData);
        Assert.Equal("Directed by Christopher Nolan", descriptor.DisplayName);
    }

    // -------------------------------------------------------------------------
    // BecauseYouWatchedWidget
    // -------------------------------------------------------------------------

    [Fact]
    public void BecauseYouWatched_GetDescriptor_HasExpectedProperties()
    {
        var user = new User("test", "Default", "Default");
        var widget = new BecauseYouWatchedWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            BuildScoringService(user, []),
            TestLocalization);

        var d = widget.GetDescriptor();

        Assert.Equal("jux.personalized.because-you-watched", d.WidgetType);
        Assert.Equal(WidgetCategory.Personalized, d.Category);
        Assert.Equal(WidgetViewMode.Portrait, d.ViewMode);
    }

    [Fact]
    public void BecauseYouWatched_Resolve_NoRecentHistory_ReturnsNull()
    {
        var user = new User("test", "Default", "Default");
        var widget = new BecauseYouWatchedWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            BuildScoringService(user, []),
            TestLocalization);

        var resolved = widget.Resolve(user.Id, new WidgetInstanceConfig(), 1);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task BecauseYouWatched_GetItemsAsync_SharesGenresAndExcludesReferenceFilm()
    {
        var user = new User("test", "Default", "Default");
        var reference = new Movie { Id = Guid.NewGuid(), Name = "Inception", Genres = ["Sci-Fi", "Action"] };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemById(reference.Id)).Returns(reference);
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([]);

        var widget = new BecauseYouWatchedWidget(
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            BuildScoringService(user, []),
            TestLocalization);

        await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, AdditionalData = reference.Id.ToString() },
            CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal(reference.Genres, capturedQuery!.Genres);
        Assert.Equal([reference.Id], capturedQuery.ExcludeItemIds);
    }

    [Fact]
    public async Task BecauseYouWatched_ReferenceFilmNotFound_DoesNotThrow()
    {
        var user = new User("test", "Default", "Default");
        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemById(It.IsAny<Guid>())).Returns((BaseItem?)null);
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([]);

        var widget = new BecauseYouWatchedWidget(
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            BuildScoringService(user, []),
            TestLocalization);

        var result = await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, AdditionalData = Guid.NewGuid().ToString() },
            CancellationToken.None);

        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public async Task BecauseYouWatched_GetItemsAsync_SeriesReference_RecommendsOnlySeries()
    {
        var user = new User("test", "Default", "Default");
        var reference = new Series { Id = Guid.NewGuid(), Name = "Breaking Bad", Genres = ["Drama"] };

        var userManagerMock = new Mock<IUserManager>();
        userManagerMock.Setup(m => m.GetUserById(user.Id)).Returns(user);

        InternalItemsQuery? capturedQuery = null;
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock.Setup(m => m.GetItemById(reference.Id)).Returns(reference);
        libraryManagerMock
            .Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>([]));

        var dtoServiceMock = new Mock<IDtoService>();
        dtoServiceMock
            .Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([]);

        var widget = new BecauseYouWatchedWidget(
            userManagerMock.Object,
            libraryManagerMock.Object,
            dtoServiceMock.Object,
            BuildScoringService(user, []),
            TestLocalization);

        await widget.GetItemsAsync(
            new WidgetPayload { UserId = user.Id, AdditionalData = reference.Id.ToString() },
            CancellationToken.None);

        Assert.NotNull(capturedQuery);
        Assert.Equal([BaseItemKind.Series], capturedQuery!.IncludeItemTypes);
        Assert.Equal(reference.Genres, capturedQuery.Genres);
    }

    [Fact]
    public void BecauseYouWatched_Resolve_ScopeMovies_ExcludesSeriesOnlyHistory()
    {
        var user = new User("test", "Default", "Default");
        var series = new Series { Name = "A Series", Genres = ["Drama"] };

        var scoringService = BuildScoringService(user, watched: [], watchedSeries: [series]);
        var widget = new BecauseYouWatchedWidget(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            scoringService,
            TestLocalization);

        var config = new WidgetInstanceConfig
        {
            ExtraParams = new Dictionary<string, string> { ["scope"] = "movies" }
        };

        var resolved = widget.Resolve(user.Id, config, 1);

        Assert.Null(resolved);
    }
}
