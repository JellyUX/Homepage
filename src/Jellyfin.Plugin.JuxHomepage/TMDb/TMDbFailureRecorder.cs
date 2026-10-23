namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Collects the failures of the TMDb requests made while it is open, for a caller of
/// <see cref="ITMDbApiClient"/>, whose methods answer an empty list rather than throw (FIAB-10.c). Opened
/// and disposed in the same async method, it flows to every request awaited in between and to none
/// outside (<c>TMDbApiClientTests.Recorder_*</c>).
/// </summary>
public sealed class TMDbFailureRecorder : IDisposable
{
    private static readonly AsyncLocal<TMDbFailureRecorder?> Current = new();

    private readonly TMDbFailureRecorder? _outer;

    private TMDbFailureRecorder()
    {
        _outer = Current.Value;
        Current.Value = this;
    }

    /// <summary>Gets the last failure recorded while this recorder was open, or null.</summary>
    public TMDbFailureKind? LastFailure { get; private set; }

    /// <summary>Opens a recorder for the requests that follow, until it is disposed.</summary>
    /// <returns>The open recorder.</returns>
    public static TMDbFailureRecorder Begin() => new();

    /// <inheritdoc/>
    public void Dispose() => Current.Value = _outer;

    /// <summary>Records a failure in the open recorder, if any.</summary>
    /// <param name="kind">Why the request gave no data.</param>
    internal static void Record(TMDbFailureKind kind)
    {
        if (Current.Value is { } recorder)
        {
            recorder.LastFailure = kind;
        }
    }
}
