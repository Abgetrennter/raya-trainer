using RayaTrainer.Core.Agent;        // NOTE: namespace doesn't exist yet
using Xunit;

namespace RayaTrainer.Tests.Agent;   // NOTE: namespace doesn't exist yet

public static class AgentProtocolContracts
{
    [Fact]
    public static void Contract_Magic_IsCurrentV9Value()
    {
        Assert.Equal(0x41594152u, AgentProtocol.Magic);  // ASCII "RAYA" (LE)
    }

    [Fact]
    public static void Contract_Version_IsCurrentV9()
    {
        Assert.Equal(9, AgentProtocol.Version);
    }

    [Fact]
    public static void Contract_Fingerprint_EncodesV9AndMagic()
    {
        // High 32 bits = ASCII "RAYA" (LE) = 0x52415941.
        // Low 32 bits encode (Version=9 << 16 | 1) = 0x00090001.
        Assert.Equal(0x5241594100090001UL, AgentBuildIdentity.Fingerprint);
    }
}
