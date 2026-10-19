using Jellyfin.Plugin.JuxHomepage.Widgets;
using Jellyfin.Plugin.JuxHomepage.Widgets.Personalized;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<ConfigurationChangeListener> _logger;
    private readonly ConfigurationBackup _backup;
    private EventHandler<BasePluginConfiguration>? _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationChangeListener"/> class.
    /// </summary>
    /// <param name="sessionCache">The session cache to clear on configuration change.</param>
    /// <param name="scoringService">The scoring service whose snapshots to clear on configuration change.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="backup">The copy of the configuration written after each admin save (FIAB-07).</param>
    public ConfigurationChangeListener(SessionCache sessionCache, ScoringService scoringService, ILogger<ConfigurationChangeListener> logger, ConfigurationBackup backup)
    {
        _sessionCache = sessionCache;
        _scoringService = scoringService;
        _logger = logger;
        _backup = backup;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = (_, configuration) => OnConfigurationChanged(configuration);

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

    /// <summary>
    /// Drops the cached layouts and scores, and writes the copy of the saved configuration (FIAB-07): every
    /// admin save goes through UpdateConfiguration, which raises ConfigurationChanged; the migration save
    /// of the constructor does not, so a blank configuration never replaces the copy.
    /// </summary>
    /// <param name="configuration">The configuration just saved.</param>
    internal void OnConfigurationChanged(BasePluginConfiguration? configuration) =>
        EventHandlerGuard.Run(_logger, nameof(OnConfigurationChanged), () =>
        {
            _sessionCache.Clear();
            _scoringService.Clear();
            if (configuration is PluginConfiguration saved && !_backup.Save(saved))
            {
                _logger.LogWarning("The copy of the JellyUX configuration was not updated; a reset could only be undone to the previous copy.");
            }
        });
}
