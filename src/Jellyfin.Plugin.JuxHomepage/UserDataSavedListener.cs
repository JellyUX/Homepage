using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage;

/// <summary>
/// A hosted service reacting to <see cref="IUserDataManager.UserDataSaved"/>: subscribes on start,
/// unsubscribes on stop, and runs each reaction through <see cref="EventHandlerGuard"/>, so a failure
/// is logged instead of failing the server operation that saved the data.
/// </summary>
public abstract class UserDataSavedListener : IHostedService
{
    private readonly ILogger _logger;
    private EventHandler<UserDataSaveEventArgs>? _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataSavedListener"/> class.
    /// </summary>
    /// <param name="userDataManager">Jellyfin user data manager.</param>
    /// <param name="logger">The logger of the listening class.</param>
    protected UserDataSavedListener(IUserDataManager userDataManager, ILogger logger)
    {
        UserDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>Gets the Jellyfin user data manager.</summary>
    protected IUserDataManager UserDataManager { get; }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = (_, e) => EventHandlerGuard.Run(_logger, GetType().Name, () => OnUserDataSaved(e));
        UserDataManager.UserDataSaved += _handler;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler is not null)
        {
            UserDataManager.UserDataSaved -= _handler;
        }

        return Task.CompletedTask;
    }

    /// <summary>Reacts to one user data save.</summary>
    /// <param name="e">The save.</param>
    protected abstract void OnUserDataSaved(UserDataSaveEventArgs e);
}
