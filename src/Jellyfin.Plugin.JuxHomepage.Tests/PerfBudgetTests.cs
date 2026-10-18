using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Controllers;
using Jellyfin.Plugin.JuxHomepage.Library;
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

    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

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
        _library.Inner.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(() => [.. _movies]);
        _library.Inner.Setup(m => m.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(() => new QueryResult<BaseItem>(0, LibrarySize, [.. _movies]));
        _library.Inner.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns(() => [.. _movies.Select(m => m.Id)]);
        _library.Inner.Setup(m => m.GetItemById(It.IsAny<Guid>())).Returns((Guid id) => _movies.FirstOrDefault(m => m.Id == id));

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
        services.AddSingleton(Mock.Of<IUserDataManager>());
        services.AddSingleton(DtoServiceMappingEachItem());
        services.AddSingleton(Mock.Of<IUserViewManager>());
        services.AddSingleton(Mock.Of<ITVSeriesManager>());
        services.AddSingleton(Mock.Of<ICollectionManager>());
        services.AddSingleton(Mock.Of<IPluginManager>());
        services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
        services.AddSingleton(authContext.Object);
        new PluginServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());

        // Registered after the registrator, so it wins over the Plugin.Instance accessor.
        var configuration = ReferenceConfiguration(services.BuildServiceProvider());
        services.AddSingleton<Func<PluginConfiguration?>>(() => configuration);
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

        var differences = measured.Keys.Union(budget.Keys)
            .Order(StringComparer.Ordinal)
            .Select(key => (Key: key, Measured: measured.GetValueOrDefault(key, -1), Budget: budget.GetValueOrDefault(key, -1)))
            .Where(d => d.Measured != d.Budget)
            .Select(d => d.Measured < 0 ? $"{d.Key}: in the budget but no longer measured"
                : d.Budget < 0 ? $"{d.Key}: measured {d.Measured}, missing from the budget"
                : d.Measured > d.Budget ? $"{d.Key}: measured {d.Measured} > budget {d.Budget} (raise only with a Budget: trailer)"
                : $"{d.Key}: measured {d.Measured} < budget {d.Budget} (lower the budget in this commit)")
            .ToList();

        Assert.True(differences.Count == 0, $"{BudgetPath} differs from the measured calls:\n" + string.Join('\n', differences));
    }

    private async Task<SortedDictionary<string, int>> MeasureHomePath()
    {
        var controller = BuildController();
        var widgetService = _provider.GetRequiredService<WidgetService>();
        await WarmBackgroundCaches(controller);
        widgetService.InvalidateUser(_user.Id);

        var measured = new SortedDictionary<string, int>(StringComparer.Ordinal);

        _library.Reset();
        var descriptors = await ReadAllSections(controller);
        measured["Sections (layout build)"] = _library.Total;

        _library.Reset();
        await controller.GetSections(_user.Id, page: 0);
        measured["Sections (cached layout)"] = _library.Total;

        foreach (var descriptor in descriptors)
        {
            _library.Reset();
            await controller.GetSection(descriptor.WidgetType, _user.Id, descriptor.AdditionalData, descriptor.SectionKey);
            var key = $"Section/{descriptor.WidgetType}";
            measured[measured.ContainsKey(key) ? $"{key} {descriptor.AdditionalData}" : key] = _library.Total;
        }

        _library.Reset();
        await controller.GetWatchlistIds(_user.Id);
        measured["Watchlist/Ids"] = _library.Total;

        _library.Reset();
        await controller.GetCollectionsForItem(_movies[0].Id);
        measured["Collections/IncludedIn"] = _library.Total;

        return measured;
    }

    private async Task WarmBackgroundCaches(JuxHomepageController controller)
    {
        await _provider.GetRequiredService<ICollectionsIndexCacheService>().RefreshAsync(CancellationToken.None);

        var scoring = _provider.GetRequiredService<ScoringService>();
        scoring.GetTopGenres(_user.Id, 1);
        for (var attempt = 0; attempt < 200 && !scoring.HasSnapshotForTesting(_user.Id); attempt++)
        {
            await Task.Delay(25);
        }

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

    private static IDtoService DtoServiceMappingEachItem()
    {
        var mock = new Mock<IDtoService>();
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
        return mock.Object;
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

    private static Dictionary<string, int> ReadBudget()
    {
        var path = Path.Combine(RepoPaths.Root(), BudgetPath);
        var endpoints = JsonNode.Parse(File.ReadAllText(path))!["endpoints"]!.AsObject();
        return endpoints.ToDictionary(e => e.Key, e => e.Value!.GetValue<int>(), StringComparer.Ordinal);
    }
}
