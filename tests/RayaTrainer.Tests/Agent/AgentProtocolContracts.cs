using RayaTrainer.Core.Agent;        // NOTE: namespace doesn't exist yet
using Xunit;

namespace RayaTrainer.Tests.Agent;   // NOTE: namespace doesn't exist yet

public static class AgentProtocolContracts
{
    [Fact]
    public static void Contract_Magic_IsCurrentV10Value()
    {
        Assert.Equal(0x41594152u, AgentProtocol.Magic);  // ASCII "RAYA" (LE)
    }

    [Fact]
    public static void Contract_Version_IsCurrentV10()
    {
        Assert.Equal(10, AgentProtocol.Version);
    }

    [Fact]
    public static void Contract_Fingerprint_EncodesV10AndMagic()
    {
        // High 32 bits = ASCII "RAYA" (LE) = 0x52415941.
        // Low 32 bits encode (Version=10 << 16 | 3) = 0x000A0003. Sub-counter 3 marks
        // the profile-aware StructureUnpackUpdate fast-build field fix.
        Assert.Equal(0x52415941000A0003UL, AgentBuildIdentity.Fingerprint);
    }
}
