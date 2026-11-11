using RayaTrainer.Core.Agent.Generated;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Automatic Agent build identity (Agent-owned runtime migration, plan §7). The manual
/// <c>Fingerprint</c> sub-counter and its rotation history are deleted: identity is now derived
/// from generated, deterministic sources so a behavior change no longer forces a hand-edited
/// hex literal.
/// <list type="bullet">
/// <item><see cref="ProtocolMajor"/> is bumped only for wire-incompatible changes.</item>
/// <item><see cref="CatalogContractHash"/> is emitted verbatim by the Runtime Catalog generator
/// (identical on the C++ and C# sides).</item>
/// <item><see cref="BuildId"/> is the wire-comparable build identity. Same-<see cref="ProtocolMajor"/>
/// takeover is allowed even when the running Agent reports a different <see cref="BuildId"/>;
/// per-feature gaps are surfaced through capability/schema negotiation, not a global refusal.</item>
/// </list>
/// </summary>
public static class AgentBuildIdentity
{
    /// <summary>Wire-incompatible protocol version. Mirrors <see cref="AgentProtocol.Version"/>.</summary>
    public const ushort ProtocolMajor = AgentProtocol.Version;

    /// <summary>Additive surface revision. Capabilities/schema remain the authority for features.</summary>
    public const ushort ProtocolMinor = 0;

    /// <summary>
    /// Deterministic SHA-256-derived identity of the normalized Runtime Catalog, generated into
    /// both <c>RuntimeCatalogMetadata</c> (C#) and <c>kRuntimeCatalogContractHash</c> (C++).
    /// </summary>
    public const ulong CatalogContractHash = RuntimeCatalogMetadata.CatalogContractHash;

    /// <summary>
    /// Wire-comparable build identity published by the Agent in Ping/Status. Generated from the
    /// catalog contract plus checked-in native source inputs, so behavior-only changes are visible
    /// without editing or test-locking a fingerprint literal.
    /// </summary>
    public const ulong BuildId = RuntimeCatalogMetadata.BuildId;

    /// <summary>
    /// Takeover disposition for an already-injected Agent (plan §7.3): a mismatched
    /// <see cref="ProtocolMajor"/> is refused (restart the game); a matching major with a
    /// different <see cref="BuildId"/> is allowed but flagged; an exact match is compatible.
    /// </summary>
    public static AgentTakeoverDecision EvaluateTakeover(ushort remoteProtocolMajor, ulong remoteBuildId)
    {
        if (remoteProtocolMajor != ProtocolMajor)
        {
            return AgentTakeoverDecision.IncompatibleProtocol;
        }

        return remoteBuildId != BuildId
            ? AgentTakeoverDecision.DifferentBuild
            : AgentTakeoverDecision.Compatible;
    }
}

/// <summary>Result of <see cref="AgentBuildIdentity.EvaluateTakeover"/>.</summary>
public enum AgentTakeoverDecision
{
    /// <summary>Same protocol major and build id; reuse the Agent as-is.</summary>
    Compatible,

    /// <summary>Same protocol major, different build id; reuse allowed, surface a stale-build hint.</summary>
    DifferentBuild,

    /// <summary>Wire-incompatible protocol major; refuse and require a game restart.</summary>
    IncompatibleProtocol,
}
