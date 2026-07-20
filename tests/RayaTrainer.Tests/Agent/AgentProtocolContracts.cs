using RayaTrainer.Core.Agent;        // NOTE: namespace doesn't exist yet
using Xunit;

namespace RayaTrainer.Tests.Agent;   // NOTE: namespace doesn't exist yet

public static class AgentProtocolContracts
{
    [Fact]
    public static void Contract_Magic_IsCurrentV11Value()
    {
        Assert.Equal(0x41594152u, AgentProtocol.Magic);  // ASCII "RAYA" (LE)
    }

    [Fact]
    public static void Contract_Version_IsCurrentV11()
    {
        Assert.Equal(11, AgentProtocol.Version);
    }

    [Fact]
    public static void Contract_Fingerprint_EncodesV11AndMagic()
    {
        // High 32 bits = ASCII "RAYA" (LE) = 0x52415941.
        // Low 32 bits encode (Version=11 << 16 | 3) = 0x000B0003. Sub-counter reset to 1
        // when the wire protocol changed (v11 L1 refactor; commands 5/6/7 redefined,
        // capability bits 0x8+0x10 added). Sub-counter bumped 1 -> 2 for the DerivedStateReset
        // PatchSet entry semantics and the Steam English 1.12 frame-rate layout. Sub-counter
        // bumped 2 -> 3 because public v0.0.6 omitted the profile-aware Uprising fast-build
        // handler while sharing the fixed Agent's fingerprint. Older injected Agents must
        // not be reused even though the wire protocol remains v11.
        Assert.Equal(0x52415941000B0003UL, AgentBuildIdentity.Fingerprint);
    }
}
