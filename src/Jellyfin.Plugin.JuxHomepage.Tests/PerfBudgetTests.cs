using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Controllers;
using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using Jellyfin.Plugin.JuxHomepage.TMDb.Models;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
using Jellyfin.Plugin.JuxHomepage.Widgets.Local;
using Jellyfin.Plugin.JuxHomepage.Widgets.Native;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Counts the ILibraryManager calls of each home-path request against ci/perf-budget.json
/// (PROC-02.c). The services come from the production registrator over a fixed fake library, a
/// fixed clock and a reference configuration with one enabled row per registered widget type.
/// Background caches (scoring snapshot, collections index) are warmed first, so each count is the
/// cost of the request alone. A count must equal its budget: a fix lowers the budget in the same
/// commit, and a raise needs a "Budget:" trailer (checked by ci/static-checks.mjs).
/// </summary>
public sealed class PerfBudgetTests : IDisposable
{
    private const string BudgetPath = "ci/perf-budget.json";
    private const int LibrarySize = 20;
    private const int TmdbListSize = 20;

    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The budget tables: ILibraryManager calls, then IUserDataManager and IDtoService calls (DEC-P06-6).</summary>
    private static readonly string[] Tables = ["endpoints", "userData", "dto"];

    private static readonly Dictionary<string, string> AdminValues = new(StringComparer.Ordinal)
    {
        ["jux.admin.genre"] = "Action",
        ["jux.admin.actor"] = "Actor Name",
        ["jux.admin.director"] = "Director Name",
        ["jux.admin.studio"] = "Studio Name",
        ["jux.admin.collection"] = "6f1c1b2e-0d4b-4a8e-9a37-2f4b8c1d5e60",
        ["jux.admin.tag"] = "tag",
        ["jux.admin.year"] = "2020"
    };

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "jux-budget-" + Guid.NewGuid().ToString("N"));
    private readonly TestMocks.CountingLibraryManager _library = new();
    private readonly TestMocks.CountingService<IUserDataManager> _userData = new();
    private readonly TestMocks.CountingService<IDtoService> _dto = new();
    private readonly User _user = TestMocks.DefaultUser();
    private readonly Movie[] _movies;
    private readonly ServiceProvider _provider;
    private readonly ITestOutputHelper _output;

    public PerfBudgetTests(ITestOutputHelper output)
    {
        _output = output;
        _movies = Enumerable.Range(1, LibrarySize)
            .Select(i => new Movie { Id = new Guid(i, 0, 0, new byte[8]), Name = $"Movie {i}", Genres = ["Action"] })
            .ToArray();
        _library.Inner.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => [.. Page(q)]);
        _library.Inner.Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => new QueryResult<BaseItem>(q.StartIndex ?? 0, LibrarySize, [.. Page(q)]));
        _library.Inner.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => [.. Page(q).Select(m => m.Id)]);
        _library.Inner.Setup(m => m.GetItemById(It.IsAny<Guid>())).Returns((Guid id) => _movies.FirstOrDefault(m => m.Id == id));
        SetUpDtoMappingEachItem(_dto.Inner);

        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.DataPath).Returns(_dataPath);
        paths.SetupGet(p => p.CachePath).Returns(_dataPath);
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataPath);
        paths.SetupGet(p => p.WebPath).Returns(_dataPath);

        var userManager = TestMocks.UserManagerReturning(_user);
        userManager.Setup(m => m.GetUsers()).Returns([_user]);

        var authContext = new Mock<IAuthorizationContext>();
        authContext.Setup(a => a.GetAuthorizationInfo(It.IsAny<HttpContext>()))
            .ReturnsAsync(new AuthorizationInfo { User = _user, IsApiKey = false });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider { UtcNowValue = FixedNow });
        services.AddSingleton(paths.Object);
        services.AddSingleton(userManager.Object);
        services.AddSingleton(_library.Object);
        services.AddSingleton(_userData.Object);
        services.AddSingleton(_dto.Object);
        services.AddSingleton(Mock.Of<IUserViewManager>());
        services.AddSingleton(Mock.Of<ITVSeriesManager>());
        services.AddSingleton(Mock.Of<ICollectionManager>());
        services.AddSingleton(Mock.Of<IPluginManager>());
        services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
        services.AddSingleton(authContext.Object);
        new PluginServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());

        // Registered after the registrator, so they win over the Plugin.Instance accessor and the HTTP client.
        var configuration = ReferenceConfiguration(services.BuildServiceProvider());
        services.AddSingleton<Func<PluginConfiguration?>>(() => configuration);
        services.AddSingleton(FakeTmdbClient());
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    [Fact]
    public async Task HomePath_LibraryCallsPerRequest_MatchTheBudget()
    {
        var measured = await MeasureHomePath();
        var budget = ReadBudget();

        _output.WriteLine(JsonSerializer.Serialize(measured, new JsonSerializerOptions { WriteIndented = true }));

        var differences = Tables
            .SelectMany(table => Differences(table, measured[table], budget.GetValueOrDefault(table) ?? []))
            .ToList();

        Assert.True(differences.Count == 0, $"{BudgetPath} differs from the measured calls:\n" + string.Join('\n', differences));
    }

    private static IEnumerable<string> Differences(string table, IReadOnlyDictionary<string, int> measured, IReadOnlyDictionary<string, int> budget) =>
        measured.Keys.Union(budget.Keys)
            .Order(StringComparer.Ordinal)
            .Select(key => (Key: $"{table}/{key}", Measured: measured.GetValueOrDefault(key, -1), Budget: budget.GetValueOrDefault(key, -1)))
            .Where(d => d.Measured != d.Budget)
            .Select(d => d.Measured < 0 ? $"{d.Key}: in the budget but no longer measured"
                : d.Budget < 0 ? $"{d.Key}: measured {d.Measured}, missing from the budget"
                : d.Measured > d.Budget ? $"{d.Key}: measured {d.Measured} > budget {d.Budget} (raise only with a Budget: trailer)"
                : $"{d.Key}: measured {d.Measured} < budget {d.Budget} (lower the budget in this commit)");

    private async Task<Dictionary<string, SortedDictionary<string, int>>> MeasureHomePath()
    {
        var controller = BuildController();
        var widgetService = _provider.GetRequiredService<WidgetService>();
        await WarmBackgroundCaches(controller);
        widgetService.InvalidateUser(_user.Id);

        var measured = Tables.ToDictionary(t => t, _ => new SortedDictionary<string, int>(StringComparer.Ordinal));

        List<WidgetDescriptor> descriptors = [];
        await Measure(measured, "Sections (layout build)", async () => descriptors = await ReadAllSections(controller));
        await Measure(measured, "Sections (cached layout)", () => controller.GetSections(_user.Id, page: 0));

        foreach (var descriptor in descriptors)
        {
            var key = $"Section/{descriptor.WidgetType}";
            await Measure(
                measured,
                measured["endpoints"].ContainsKey(key) ? $"{key} {descriptor.AdditionalData}" : key,
                () => controller.GetSection(descriptor.WidgetType, _user.Id, descriptor.AdditionalData, descriptor.SectionKey));
        }

        // The same sections once nothing of the layout is cached any more: what a section costs its widget.
        foreach (var descriptor in descriptors)
        {
            var key = $"Section/{descriptor.WidgetType} (uncached)";
            widgetService.InvalidateUser(_user.Id);
            await Measure(
                measured,
                measured["endpoints"].ContainsKey(key) ? $"{key} {descriptor.AdditionalData}" : key,
                () => controller.GetSection(descriptor.WidgetType, _user.Id, descriptor.AdditionalData, descriptor.SectionKey));
        }

        // A type that renders no section on this library is still budgeted, by its first row (DEC-P06-7).
        foreach (var widget in _provider.GetRequiredService<IWidgetRegistry>().GetAll().OrderBy(w => w.WidgetType, StringComparer.Ordinal))
        {
            var key = $"Section/{widget.WidgetType}";
            if (!measured["endpoints"].ContainsKey(key))
            {
                await Measure(measured, key, () => controller.GetSection(widget.WidgetType, _user.Id, AdminValues.GetValueOrDefault(widget.WidgetType), null));
            }
        }

        await Measure(measured, "Watchlist/Ids", () => controller.GetWatchlistIds(_user.Id));
        await Measure(measured, "Collections/IncludedIn", () => controller.GetCollectionsForItem(_movies[0].Id));
        await Measure(
            measured,
            $"TMDb refresh (6 lists of {TmdbListSize} titles)",
            () => _provider.GetRequiredService<ITMDbCacheService>().RefreshAllAsync(null, CancellationToken.None));

        return measured;
    }

    private async Task Measure(Dictionary<string, SortedDictionary<string, int>> measured, string key, Func<Task> request)
    {
        _library.Reset();
        _userData.Reset();
        _dto.Reset();
        await request();
        measured["endpoints"][key] = _library.Total;
        measured["userData"][key] = _userData.Total;
        measured["dto"][key] = _dto.Total;
    }

    private IEnumerable<Movie> Page(InternalItemsQuery query) =>
        _movies
            .Where(m => query.HasAnyProviderId is not { Count: > 0 } ids
                || ids.Any(id => m.ProviderIds.TryGetValue(id.Key, out var value) && value == id.Value))
            .Skip(query.StartIndex ?? 0)
            .Take(query.Limit ?? int.MaxValue);

    /// <summary>
    /// A TMDb client returning 20 titles per list (distinct ids per list, none in the fake library), with
    /// an IMDb id for every even TMDb id.
    /// </summary>
    private static ITMDbApiClient FakeTmdbClient()
    {
        static IReadOnlyList<TMDbMovie> Movies(int first) =>
            [.. Enumerable.Range(first, TmdbListSize).Select(id => new TMDbMovie { Id = id, Title = $"TMDb movie {id}" })];
        static IReadOnlyList<TMDbShow> Shows(int first) =>
            [.. Enumerable.Range(first, TmdbListSize).Select(id => new TMDbShow { Id = id, Name = $"TMDb show {id}" })];
        static string? Imdb(int id) => id % 2 == 0 ? $"tt{id:D7}" : null;

        var client = new Mock<ITMDbApiClient>();
        client.Setup(c => c.GetTrendingMoviesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Movies(1000));
        client.Setup(c => c.GetTopRatedMoviesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Movies(2000));
        client.Setup(c => c.GetNowPlayingMoviesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Movies(3000));
        client.Setup(c => c.GetTrendingShowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Shows(4000));
        client.Setup(c => c.GetAiringTodayAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Shows(5000));
        client.Setup(c => c.GetTopRatedShowsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Shows(6000));
        client.Setup(c => c.GetMovieExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int id, CancellationToken _) => Imdb(id));
        client.Setup(c => c.GetShowExternalIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int id, CancellationToken _) => Imdb(id));
        return client.Object;
    }

    private async Task WarmBackgroundCaches(JuxHomepageController controller)
    {
        await _provider.GetRequiredService<ICollectionsIndexCacheService>().RefreshAsync(CancellationToken.None);

        var scoring = _provider.GetRequiredService<ScoringService>();
        scoring.GetTopGenres(_user.Id, 1);
        await scoring.WhenIdleForTesting();

        Assert.True(scoring.HasSnapshotForTesting(_user.Id), "The scoring snapshot was not computed during warm-up.");
        await ReadAllSections(controller);
    }

    private async Task<List<WidgetDescriptor>> ReadAllSections(JuxHomepageController controller)
    {
        var descriptors = new List<WidgetDescriptor>();
        for (var page = 0; ; page++)
        {
            var result = await controller.GetSections(_user.Id, page);
            var sections = Assert.IsType<SectionsPage>(Assert.IsType<OkObjectResult>(result.Result).Value);
            descriptors.AddRange(sections.Sections);
            if (!sections.HasMore)
            {
                return descriptors;
            }
        }
    }

    private JuxHomepageController BuildController()
    {
        var controller = ActivatorUtilities.CreateInstance<JuxHomepageController>(_provider);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "test")) }
        };
        return controller;
    }

    private static void SetUpDtoMappingEachItem(Mock<IDtoService> mock)
    {
        mock.Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User _, BaseItem _) =>
                [.. items.Select(i => new BaseItemDto { Id = i.Id, Name = i.Name })]);
        mock.Setup(m => m.GetBaseItemDto(
                It.IsAny<BaseItem>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns((BaseItem item, DtoOptions _, User _, BaseItem _) => new BaseItemDto { Id = item.Id, Name = item.Name });
    }

    private static PluginConfiguration ReferenceConfiguration(IServiceProvider services)
    {
        using var disposable = services as IDisposable;
        var rows = new List<WidgetConfig>(NativeWidgetDefaults.Build());
        rows.AddRange(LocalWidgetDefaults.Build());
        rows.AddRange(SeasonalWidgetDefaults.Build());

        var withRow = rows.Select(r => r.WidgetType).ToHashSet(StringComparer.Ordinal);
        foreach (var widget in services.GetRequiredService<IWidgetRegistry>().GetAll())
        {
            if (withRow.Add(widget.WidgetType))
            {
                rows.Add(new WidgetConfig
                {
                    WidgetType = widget.WidgetType,
                    ExtraParams = AdminValues.TryGetValue(widget.WidgetType, out var value)
                        ? [new WidgetExtraParam { Key = "value", Value = value }]
                        : []
                });
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Enabled = true;
            rows[i].MinItems = 1;
            rows[i].Order = i;
        }

        return new PluginConfiguration { Widgets = [.. rows] };
    }

    private static Dictionary<string, Dictionary<string, int>> ReadBudget()
    {
        var path = Path.Combine(RepoPaths.Root(), BudgetPath);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return Tables
            .Where(table => root[table] is JsonObject)
            .ToDictionary(
                table => table,
                table => root[table]!.AsObject().ToDictionary(e => e.Key, e => e.Value!.GetValue<int>(), StringComparer.Ordinal));
    }
}
