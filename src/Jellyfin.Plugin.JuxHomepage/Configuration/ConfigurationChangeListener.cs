using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JuxHomepage.Configuration;

/// <summary>
/// Hosted service that invalidates the session cache and the scoring snapshots whenever the plugin
/// configuration is saved. Subscribes to the plugin's <c>ConfigurationChanged</c> event in
/// <see cref="StartAsync"/> so that any call to <see cref="Plugin.UpdateConfiguration"/> -- including
/// saves from the Jellyfin admin dashboard -- immediately clears the cached home screen layouts and
/// personalized picks for all users.
/// </summary>
public sealed class ConfigurationChangeListener : IHostedService
{
    private readonly SessionCache _sessionCache;
    private readonly ScoringService _scoringService;
    private EventHandler<BasePluginConfiguration>? _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationChangeListener"/> class.
    /// </summary>
    /// <param name="sessionCache">The session cache to clear on configuration change.</param>
    /// <param name="scoringService">The scoring service whose snapshots to clear on configuration change.</param>
    public ConfigurationChangeListener(SessionCache sessionCache, ScoringService scoringService)
    {
        _sessionCache = sessionCache;
        _scoringService = scoringService;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = (_, _) => OnConfigurationChanged();

        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += _handler;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not null && _handler is not null)
        {
            Plugin.Instance.ConfigurationChanged -= _handler;
        }

        return Task.CompletedTask;
    }

    /// <summary>Clears both caches; internal so tests can drive it without a live plugin instance.</summary>
    internal void OnConfigurationChanged()
    {
        _sessionCache.Clear();
        _scoringService.Clear();
    }
}
