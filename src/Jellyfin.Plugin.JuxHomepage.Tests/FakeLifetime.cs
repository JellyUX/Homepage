using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>A server lifetime whose start a test signals with <see cref="Start"/>.</summary>
internal sealed class FakeLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _started = new();

    public CancellationToken ApplicationStarted => _started.Token;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void Start() => _started.Cancel();

    public void StopApplication()
    {
    }
}
