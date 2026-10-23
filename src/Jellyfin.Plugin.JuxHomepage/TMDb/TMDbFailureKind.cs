namespace Jellyfin.Plugin.JuxHomepage.TMDb;

/// <summary>
/// Why a TMDb request gave no data, shown to the admin next to the list it emptied (FIAB-10.c).
/// </summary>
public enum TMDbFailureKind
{
    /// <summary>TMDb refused the configured API key (HTTP 401).</summary>
    InvalidKey,

    /// <summary>TMDb rate-limited the request (HTTP 429).</summary>
    RateLimited,

    /// <summary>TMDb did not answer within the HTTP client timeout.</summary>
    Timeout,

    /// <summary>TMDb could not be reached or answered with an error, or the circuit breaker is open.</summary>
    Unreachable
}
