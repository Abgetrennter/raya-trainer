namespace RayaTrainer.Core.Agent;

public static class AgentBuildIdentity
{
    // Shared with RayaTrainer.Agent/AgentProtocol.h. Bump whenever a new Agent build must not
    // reconnect to a host from the previous build, even if the wire protocol is unchanged.
    // v9: high 32 bits = ASCII "RAYA" (LE: 0x52,0x41,0x59,0x41) = 0x52415941 matching Magic.
    //     low 32 bits = (Version=9 << 16) | 6 = 0x00090006.
    // Bumped 1 -> 2 for selected-weapon-effects registry changes; wire protocol unchanged.
    // Bumped 2 -> 3 for selected-unit auto-acquire range; wire protocol unchanged.
    // Bumped 3 -> 4 for idle acquisition and final maximum-range hooks; wire protocol unchanged.
    // Bumped 4 -> 5 for the post-compare idle branch and turret target-angle hook; wire protocol unchanged.
    // Bumped 5 -> 6 for shared turret-angle and full-circle aim-deflection hooks; wire protocol unchanged.
    public const ulong Fingerprint = 0x5241594100090006UL;
}
