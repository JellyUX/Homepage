using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JuxHomepage;

/// <summary>
/// Runs a hosted service's background work once the server has started, not during its boot, and
/// cancels it when the host stops (F-07, PERF-07). The work is not awaited: it handles its own errors.
/// </summary>
internal sealed class ServerStartedRun : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenRegistration _registration;

    /// <summary>Gets the running work, once the server has started.</summary>
    public Task? Running { get; private set; }

    /// <summary>Starts <paramref name="work"/> when the server has started.</summary>
    /// <param name="lifetime">The host lifetime.</param>
    /// <param name="work">The work, given the token the host stop cancels.</param>
    public void Register(IHostApplicationLifetime lifetime, Func<CancellationToken, Task> work) =>
        _registration = lifetime.ApplicationStarted.Register(() => Running = work(_stopping.Token));

    /// <summary>Cancels the work, as the host stops.</summary>
    public void Stop() => _stopping.Cancel();

    /// <inheritdoc/>
    public void Dispose()
    {
        _registration.Dispose();
        _stopping.Dispose();
    }
}
