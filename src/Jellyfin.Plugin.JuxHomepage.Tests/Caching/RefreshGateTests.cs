using Jellyfin.Plugin.JuxHomepage.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Caching;

public sealed class RefreshGateTests
{
    [Fact]
    public async Task TryRunAsync_WhileARefreshHoldsTheSlot_SkipsWithoutRunningTheBody()
    {
        using var gate = new RefreshGate(NullLogger.Instance, "Test refresh");
        Assert.True(gate.TryEnter());
        var ran = false;

        var accepted = await gate.TryRunAsync(() => gate.RunReserved(() => ran = true));

        Assert.False(accepted);
        Assert.False(ran);
    }

    [Fact]
    public async Task TryRunAsync_FreeSlot_HoldsTheSlotDuringTheBodyAndReleasesItAfter()
    {
        using var gate = new RefreshGate(NullLogger.Instance, "Test refresh");
        var heldDuringBody = false;

        var accepted = await gate.TryRunAsync(() => gate.RunReservedAsync(() =>
        {
            heldDuringBody = !gate.TryEnter();
            return Task.CompletedTask;
        }));

        Assert.True(accepted);
        Assert.True(heldDuringBody);
        Assert.True(gate.TryEnter());
    }

    [Fact]
    public async Task RunReserved_BodyThrows_ReleasesTheSlot()
    {
        using var gate = new RefreshGate(NullLogger.Instance, "Test refresh");
        Assert.True(gate.TryEnter());

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunReserved(() => throw new InvalidOperationException()));

        Assert.True(gate.TryEnter());
    }
}
