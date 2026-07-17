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
        // Low 32 bits encode (Version=11 << 16 | 2) = 0x000B0002. Sub-counter 2
        // adds DerivedStateReset PatchSet semantics without changing the wire version.
        Assert.Equal(0x52415941000B0002UL, AgentBuildIdentity.Fingerprint);
    }
}
