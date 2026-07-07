namespace RayaTrainer.Core.Agent;

public static class AgentBuildIdentity
{
    // Shared with RayaTrainer.Agent/AgentProtocol.h. Bump whenever a new Agent build must not
    // reconnect to a host from the previous build, even if the wire protocol is unchanged.
    // v9: high 32 bits = ASCII "RAYA" (LE: 0x52,0x41,0x59,0x41) = 0x52415941 matching Magic.
    //     low 32 bits = (Version=9 << 16) | 1 = 0x00090001.
    public const ulong Fingerprint = 0x5241594100090001UL;
}
