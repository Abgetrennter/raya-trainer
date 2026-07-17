using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentStatusPayloadTests
{
    [Fact]
    public void StatusPayloadRoundTripsNativeRuntimeState()
    {
        var payload = AgentStatusPayload.Encode(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            processId: 1234,
            moduleBase: 0x400000,
            installedHookCount: 24,
            nativeRuntimeCapabilities: 0x1F,
            gameThreadTick: 9);

        Assert.Equal(AgentStatusPayload.Size, payload.Length);
        var parsed = AgentStatusPayload.ReadFrom(payload);

        Assert.Equal(24u, parsed.InstalledHookCount);
        Assert.Equal(0x1Fu, parsed.NativeRuntimeCapabilities);
        Assert.Equal(9u, parsed.GameThreadTick);
        Assert.Equal(AgentBuildIdentity.Fingerprint, parsed.BuildFingerprint);
    }
}
