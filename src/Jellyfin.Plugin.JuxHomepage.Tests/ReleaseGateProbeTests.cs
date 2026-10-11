using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

public sealed class ReleaseGateProbeTests
{
    [Fact]
    public void DeliberatelyFails() => Assert.Fail("Release gate probe: this pull request must stay red.");
}
