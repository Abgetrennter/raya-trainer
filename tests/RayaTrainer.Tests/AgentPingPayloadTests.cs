using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentPingPayloadTests
{
    [Fact]
    public void PingPayloadRoundTripsBuildFingerprint()
    {
        var bytes = AgentPingPayload.Encode(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            processId: 1234,
            moduleBase: 0x400000,
            buildFingerprint: AgentBuildIdentity.Fingerprint);

        var payload = AgentPingPayload.ReadFrom(bytes);

        Assert.Equal(AgentPingPayload.Size, bytes.Length);
        Assert.Equal(1234, payload.ProcessId);
        Assert.Equal(0x400000u, payload.ModuleBase);
        Assert.Equal(AgentBuildIdentity.Fingerprint, payload.BuildFingerprint);
    }
}
