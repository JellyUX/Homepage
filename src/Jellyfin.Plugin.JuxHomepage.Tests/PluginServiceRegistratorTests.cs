using System.Reflection;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class PluginServiceRegistratorTests : IDisposable
{
    private static readonly Assembly PluginAssembly = typeof(PluginServiceRegistrator).Assembly;

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "jux-di-" + Guid.NewGuid().ToString("N"));

    private readonly ServiceCollection _services = new();

    private readonly ServiceProvider _provider;

    public PluginServiceRegistratorTests()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.DataPath).Returns(_dataPath);
        paths.SetupGet(p => p.CachePath).Returns(_dataPath);
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataPath);
        paths.SetupGet(p => p.WebPath).Returns(_dataPath);

        // The Jellyfin services the plugin consumes; everything else must come from the registrator.
        _services.AddLogging();
        _services.AddSingleton(paths.Object);
        _services.AddSingleton(Mock.Of<IUserManager>());
        _services.AddSingleton(Mock.Of<ILibraryManager>());
        _services.AddSingleton(Mock.Of<IUserDataManager>());
        _services.AddSingleton(Mock.Of<IDtoService>());
        _services.AddSingleton(Mock.Of<IUserViewManager>());
        _services.AddSingleton(Mock.Of<ITVSeriesManager>());
        _services.AddSingleton(Mock.Of<ICollectionManager>());
        _services.AddSingleton(Mock.Of<IPluginManager>());
        _services.AddSingleton(Mock.Of<IHostApplicationLifetime>());

        new PluginServiceRegistrator().RegisterServices(_services, Mock.Of<IServerApplicationHost>());
        _provider = _services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
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
    public void RegisterServices_EveryJellyUXServiceResolves()
    {
        var ownServices = _services
            .Where(d => d.ServiceType.Assembly == PluginAssembly)
            .Select(d => d.ServiceType)
            .Distinct()
            .ToList();

        Assert.NotEmpty(ownServices);
        foreach (var serviceType in ownServices)
        {
            Assert.NotNull(_provider.GetRequiredService(serviceType));
        }

        Assert.Contains(_provider.GetServices<IHostedService>(), s => s.GetType().Assembly == PluginAssembly);
    }

    [Fact]
    public void RegisterServices_RegistersEveryWidget()
    {
        var widgetTypes = PluginAssembly.GetTypes()
            .Where(t => typeof(IWidget).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToList();

        var registered = _provider.GetRequiredService<IWidgetRegistry>().GetAll().Select(w => w.GetType()).ToList();

        Assert.Equal(widgetTypes.Count, registered.Count);
        Assert.All(widgetTypes, t => Assert.Contains(t, registered));
    }

    [Fact]
    public void RegisterServices_HandsTheScoringServiceToTheWidgetServiceAsPersonalizationState()
    {
        var widgetService = _provider.GetRequiredService<WidgetService>();

        var state = typeof(WidgetService)
            .GetField("_personalizationState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(widgetService);

        Assert.Same(_provider.GetRequiredService<ScoringService>(), state);
    }

    [Fact]
    public void RegisterServices_HandsTheSectionResultsCacheToTheWidgetService()
    {
        var widgetService = _provider.GetRequiredService<WidgetService>();

        var cache = typeof(WidgetService)
            .GetField("_sectionResults", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(widgetService);

        Assert.Same(_provider.GetRequiredService<SectionResultCache>(), cache);
        Assert.Contains(_provider.GetServices<IHostedService>(), s => s is SectionCacheInvalidationListener);
    }

    [Fact]
    public void RegisterServices_HandsTheLikedIdsCacheToTheWatchlistService()
    {
        var watchlist = _provider.GetRequiredService<IWatchlistService>();

        var cache = typeof(WatchlistService)
            .GetField("_likedIds", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(watchlist);

        Assert.Same(_provider.GetRequiredService<LikedItemIdsCache>(), cache);
        Assert.Contains(_provider.GetServices<IHostedService>(), s => s is LikedItemIdsUpdateListener);
    }
}
