using System.Net;
using Jellyfin.Plugin.JuxHomepage.Configuration;
using Jellyfin.Plugin.JuxHomepage.Inject;
using Jellyfin.Plugin.JuxHomepage.IO;
using Jellyfin.Plugin.JuxHomepage.Library;
using Jellyfin.Plugin.JuxHomepage.Localization;
using Jellyfin.Plugin.JuxHomepage.Watchlist;
using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Admin;
using Jellyfin.Plugin.JuxHomepage.Widgets.Connected;
using Jellyfin.Plugin.JuxHomepage.Widgets.Native;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using Jellyfin.Plugin.JuxHomepage.Rewards;
using Jellyfin.Plugin.JuxHomepage.TMDb;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage;

/// <summary>
/// Registers plugin services into the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc/>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<FileTransformationDetector>();
        serviceCollection.AddSingleton<SessionCache>(_ => new SessionCache());
        serviceCollection.AddHostedService<ConfigurationChangeListener>();
        serviceCollection.AddHostedService<StartupService>();
        serviceCollection.AddSingleton<IFileSystem, FileSystem>();
        serviceCollection.AddSingleton<IUserConfigurationStore, UserConfigurationStore>();
        serviceCollection.AddSingleton<ILocalizationService, LocalizationService>();
        serviceCollection.AddHttpClient("TMDb", client =>
        {
            client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        serviceCollection.AddSingleton<ITMDbApiClient>(serviceProvider => new TMDbApiClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            () => Plugin.Instance?.Configuration,
            serviceProvider.GetRequiredService<ILogger<TMDbApiClient>>()));
        serviceCollection.AddSingleton<ITMDbCacheService>(serviceProvider => new TMDbCacheService(
            serviceProvider.GetRequiredService<IApplicationPaths>(),
            serviceProvider.GetRequiredService<IFileSystem>(),
            serviceProvider.GetRequiredService<ITMDbApiClient>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            () => Plugin.Instance?.Configuration,
            serviceProvider.GetRequiredService<ILogger<TMDbCacheService>>()));

        // Wikidata (TODO_V2.md Phase 14): no BaseAddress, since SPARQL queries and entity search live
        // on different hosts (query.wikidata.org vs. www.wikidata.org). Timeout is set above
        // Wikidata's own 60s server-side query execution ceiling, so that ceiling -- not ours --
        // is what a slow query actually hits. The User-Agent is set once here (not per request) to
        // satisfy Wikimedia's 2026 access policy: a compliant User-Agent lifts the rate limit from
        // 10 to 200 requests/minute, far more than this plugin's weekly refresh ever needs.
        serviceCollection.AddHttpClient("Wikidata", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(90);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"JellyUX-Homepage/{Plugin.Instance?.Version.ToString() ?? "dev"} (https://github.com/JellyUX/Homepage)");
        })
        // Setting the Accept-Encoding header alone does NOT make HttpClient decompress a
        // gzip/deflate response -- that requires this separate handler setting. Without it, Wikidata
        // (which does compress its responses) sent gzip bytes that ReadFromJsonAsync tried to parse
        // as JSON text and failed on (JsonException: '0x1F' is an invalid start of a value -- 0x1F is
        // the first byte of the gzip magic number), a bug caught live via the real server's logs.
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        serviceCollection.AddSingleton<IWikidataApiClient>(serviceProvider => new WikidataApiClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            serviceProvider.GetRequiredService<ILogger<WikidataApiClient>>()));
        serviceCollection.AddSingleton<IRewardsCacheService>(serviceProvider => new RewardsCacheService(
            serviceProvider.GetRequiredService<IApplicationPaths>(),
            serviceProvider.GetRequiredService<IFileSystem>(),
            serviceProvider.GetRequiredService<IWikidataApiClient>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            () => Plugin.Instance?.Configuration,
            serviceProvider.GetRequiredService<ILogger<RewardsCacheService>>()));
        serviceCollection.AddSingleton<ISeriesProgressCacheService>(serviceProvider => new SeriesProgressCacheService(
            serviceProvider.GetRequiredService<IApplicationPaths>(),
            serviceProvider.GetRequiredService<IFileSystem>(),
            serviceProvider.GetRequiredService<IUserManager>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<IUserDataManager>(),
            serviceProvider.GetRequiredService<ILogger<SeriesProgressCacheService>>()));
        serviceCollection.AddSingleton<IMovieHistoryCacheService>(serviceProvider => new MovieHistoryCacheService(
            serviceProvider.GetRequiredService<IApplicationPaths>(),
            serviceProvider.GetRequiredService<IFileSystem>(),
            serviceProvider.GetRequiredService<IUserManager>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<IUserDataManager>(),
            serviceProvider.GetRequiredService<ILogger<MovieHistoryCacheService>>()));
        serviceCollection.AddSingleton<ICollectionsIndexCacheService>(serviceProvider => new CollectionsIndexCacheService(
            serviceProvider.GetRequiredService<IApplicationPaths>(),
            serviceProvider.GetRequiredService<IFileSystem>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<ILogger<CollectionsIndexCacheService>>()));
        serviceCollection.AddSingleton<IWatchlistService>(serviceProvider => new WatchlistService(
            serviceProvider.GetRequiredService<IUserManager>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<MediaBrowser.Controller.Dto.IDtoService>()));
        serviceCollection.AddHostedService<WatchlistAutoRemoveService>();
        serviceCollection.AddSingleton<ISeriesProgressViewService>(serviceProvider => new SeriesProgressViewService(
            serviceProvider.GetRequiredService<ISeriesProgressCacheService>(),
            serviceProvider.GetRequiredService<IUserManager>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<MediaBrowser.Controller.Dto.IDtoService>(),
            serviceProvider.GetRequiredService<ILogger<SeriesProgressViewService>>()));
        serviceCollection.AddSingleton<IMovieHistoryViewService>(serviceProvider => new MovieHistoryViewService(
            serviceProvider.GetRequiredService<IMovieHistoryCacheService>(),
            serviceProvider.GetRequiredService<IUserManager>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<MediaBrowser.Controller.Dto.IDtoService>(),
            serviceProvider.GetRequiredService<ILogger<MovieHistoryViewService>>()));
        serviceCollection.AddSingleton<IStatisticsService>(serviceProvider => new StatisticsService(
            serviceProvider.GetRequiredService<ISeriesProgressCacheService>(),
            serviceProvider.GetRequiredService<IMovieHistoryCacheService>()));
        serviceCollection.AddSingleton<ScoringService>(serviceProvider => new ScoringService(
            serviceProvider.GetRequiredService<IUserManager>(),
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<IUserDataManager>(),
            () => Plugin.Instance?.Configuration,
            serviceProvider.GetRequiredService<ILogger<ScoringService>>()));
        serviceCollection.AddSingleton<WidgetLayoutResolver>(serviceProvider => new WidgetLayoutResolver(
            serviceProvider.GetRequiredService<IWidgetRegistry>(),
            serviceProvider.GetRequiredService<IUserConfigurationStore>(),
            serviceProvider.GetRequiredService<ILocalizationService>(),
            () => Plugin.Instance?.Configuration,
            serviceProvider.GetRequiredService<ILogger<WidgetLayoutResolver>>()));
        serviceCollection.AddSingleton<WidgetService>(serviceProvider => new WidgetService(
            serviceProvider.GetRequiredService<IWidgetRegistry>(),
            serviceProvider.GetRequiredService<SessionCache>(),
            serviceProvider.GetRequiredService<WidgetLayoutResolver>(),
            () => Plugin.Instance?.Configuration,
            serviceProvider.GetRequiredService<ILogger<WidgetService>>(),
            serviceProvider.GetRequiredService<ScoringService>()));

        serviceCollection.AddSingleton<IWidgetRegistry>(serviceProvider =>
        {
            var registry = new WidgetRegistry();
            var logger = serviceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<WidgetRegistry>();

            // Register native widgets in display order before loading external DLL widgets.
            RegisterWidget<ContinueWatchingWidget>(registry, serviceProvider, logger);
            RegisterWidget<NextUpWidget>(registry, serviceProvider, logger);
            RegisterWidget<RecentlyAddedMoviesWidget>(registry, serviceProvider, logger);
            RegisterWidget<RecentlyAddedShowsWidget>(registry, serviceProvider, logger);
            RegisterWidget<RecentlyAddedEpisodesWidget>(registry, serviceProvider, logger);
            RegisterWidget<MyMediaWidget>(registry, serviceProvider, logger);
            RegisterWidget<WatchlistWidget>(registry, serviceProvider, logger);

            // Register admin widgets. These have no default WidgetConfig rows; the admin
            // adds instances explicitly via the configuration page.
            RegisterWidget<GenreWidget>(registry, serviceProvider, logger);
            RegisterWidget<ActorWidget>(registry, serviceProvider, logger);
            RegisterWidget<DirectorWidget>(registry, serviceProvider, logger);
            RegisterWidget<StudioWidget>(registry, serviceProvider, logger);
            RegisterWidget<CollectionWidget>(registry, serviceProvider, logger);
            RegisterWidget<TagWidget>(registry, serviceProvider, logger);
            RegisterWidget<YearWidget>(registry, serviceProvider, logger);

            // Register personalized widgets. Like admin widgets, these have no default
            // WidgetConfig rows; the admin adds instances explicitly via the configuration page.
            // Unlike admin widgets, the section's value is computed per user from ScoringService
            // rather than chosen by the admin.
            RegisterWidget<FavoriteGenreWidget>(registry, serviceProvider, logger);
            RegisterWidget<FavoriteActorWidget>(registry, serviceProvider, logger);
            RegisterWidget<FavoriteDirectorWidget>(registry, serviceProvider, logger);
            RegisterWidget<BecauseYouWatchedWidget>(registry, serviceProvider, logger);

            // Register connected widgets. Like admin/personalized widgets, these have no default
            // WidgetConfig rows; the admin adds instances explicitly via the configuration page.
            // Each displays TMDb data cross-referenced against the local library by ITMDbCacheService.
            RegisterWidget<TrendingMoviesWidget>(registry, serviceProvider, logger);
            RegisterWidget<TrendingShowsWidget>(registry, serviceProvider, logger);
            RegisterWidget<AiringTodayShowsWidget>(registry, serviceProvider, logger);
            RegisterWidget<TopRatedMoviesWidget>(registry, serviceProvider, logger);
            RegisterWidget<TopRatedShowsWidget>(registry, serviceProvider, logger);
            RegisterWidget<NowPlayingMoviesWidget>(registry, serviceProvider, logger);
            RegisterWidget<DiscoverMoviesWidget>(registry, serviceProvider, logger);
            RegisterWidget<RewardsWidget>(registry, serviceProvider, logger);
            RegisterWidget<SeasonalWidget>(registry, serviceProvider, logger);

            return registry;
        });
    }

    private static void RegisterWidget<TWidget>(
        WidgetRegistry registry,
        IServiceProvider serviceProvider,
        ILogger logger)
        where TWidget : IWidget
    {
        try
        {
            var widget = (IWidget)ActivatorUtilities.CreateInstance(serviceProvider, typeof(TWidget));
            registry.Register(widget);
            logger.LogInformation("Registered widget '{WidgetType}'.", widget.WidgetType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register widget {Type}.", typeof(TWidget).Name);
        }
    }
}
