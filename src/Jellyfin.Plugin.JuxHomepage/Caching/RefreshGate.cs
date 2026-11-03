using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JuxHomepage.Caching;

/// <summary>
/// The single refresh slot of a cache service: at most one refresh runs at a time, and a request that finds the slot
/// taken is skipped rather than queued. The slot is reserved by <see cref="TryEnter"/> and released when the body run
/// by <see cref="RunReservedAsync"/> or <see cref="RunReserved"/> ends; the two halves stay separate because the
/// controllers reserve the slot before answering and run the body afterwards.
/// </summary>
public sealed class RefreshGate : IDisposable
{
    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly ILogger _logger;
    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshGate"/> class.
    /// </summary>
    /// <param name="logger">The owning service's logger, which reports a skipped request.</param>
    /// <param name="name">The refresh name used in that report, e.g. "TMDb refresh".</param>
    public RefreshGate(ILogger logger, string name)
    {
        _logger = logger;
        _name = name;
    }

    /// <summary>
    /// Reserves the refresh slot without waiting.
    /// </summary>
    /// <returns>True if the slot was reserved; false if a refresh is already in progress.</returns>
    public bool TryEnter() => _slot.Wait(0);

    /// <summary>
    /// Runs <paramref name="body"/> in the slot reserved by <see cref="TryEnter"/>, and releases it when the body
    /// ends, even on failure.
    /// </summary>
    /// <param name="body">The refresh body.</param>
    /// <returns>The body's completion.</returns>
    public async Task RunReservedAsync(Func<Task> body)
    {
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            _slot.Release();
        }
    }

    /// <summary>
    /// Synchronous form of <see cref="RunReservedAsync"/>, for a body that does not await.
    /// </summary>
    /// <param name="body">The refresh body.</param>
    /// <returns>A completed task, so the caller keeps its asynchronous signature.</returns>
    public Task RunReserved(Action body)
    {
        try
        {
            body();
        }
        finally
        {
            _slot.Release();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs <paramref name="lockedBody"/> when the slot is free, otherwise logs and skips the request. The body
    /// releases the slot (<see cref="RunReservedAsync"/>, <see cref="RunReserved"/>).
    /// </summary>
    /// <param name="lockedBody">The refresh body, run with the slot reserved.</param>
    /// <returns>True if the body ran; false if a refresh was already in progress.</returns>
    public async Task<bool> TryRunAsync(Func<Task> lockedBody)
    {
        if (!TryEnter())
        {
            _logger.LogInformation("{Refresh} already in progress; skipping this request.", _name);
            return false;
        }

        await lockedBody().ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose() => _slot.Dispose();
}
