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
    // v10: low 32 bits = (Version=10 << 16) | 1 = 0x000A0001. Protocol bumped 9 -> 10 for
    // object-level unit upgrade grant (commands 46/47, Native catalog EntryCount 40 -> 41,
    // dispatcher Values 8 -> 24). Sub-counter reset to 1 because the wire protocol changed.
    // v10 sub-counter 1 -> 2 for per-GameObject weapon flags and the object-registration
    // initializer hook. Wire protocol is unchanged, but an older injected Agent is incompatible.
    // v10 sub-counter 2 -> 3 for the profile-aware StructureUnpackUpdate fast-build field.
    // Wire protocol is unchanged, but an older injected Agent corrupts the Uprising field.
    public const ulong Fingerprint = 0x52415941000A0003UL;
}
