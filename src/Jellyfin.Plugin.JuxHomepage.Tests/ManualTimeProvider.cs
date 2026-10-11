namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Minimal <see cref="TimeProvider"/> test double: <see cref="TimeProvider.GetUtcNow"/> is virtual
/// precisely so it can be overridden like this for deterministic tests, without a real delay or an
/// external package.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>Gets or sets the time returned by <see cref="GetUtcNow"/>.</summary>
    public DateTimeOffset UtcNowValue { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets UTC as the local time zone, so day boundaries do not depend on the test machine.</summary>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => UtcNowValue;
}
