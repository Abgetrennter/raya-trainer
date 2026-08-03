using System.Buffers.Binary;
using System.Collections.Immutable;

namespace RayaTrainer.Core.Agent;

/// <summary>Readiness of one capability. Mirrors native <c>resolution::CapabilityState</c>.</summary>
public enum AgentCapabilityState : byte
{
    Ready = 0,
    Degraded = 1,
    Unavailable = 2,
}

/// <summary>Structured reason a capability is not Ready. Mirrors native <c>resolution::CapabilityReason</c>.</summary>
public enum AgentCapabilityReason : byte
{
    Ready = 0,
    MissingSymbol = 1,
    MissingHook = 2,
    MissingLayoutProof = 3,
    MissingBaseDependency = 4,
    OptionalDegraded = 5,
}

/// <summary>One capability's readiness record within a snapshot.</summary>
public readonly record struct AgentCapabilityRecord(
    string CapabilityId,
    AgentCapabilityState State,
    AgentCapabilityReason Reason);

/// <summary>
/// Decoded <see cref="AgentCommand.GetCapabilitySnapshot"/> response (plan §5). Per-capability
/// readiness with a structured reason, produced by the Agent's CapabilityRegistry from the frozen
/// resolution snapshot. Wire layout: u16 status, u8 runtime-blocked, u32 generation, u32 count, then
/// one record each (u16-length id, u8 state, u8 reason). Mirrors the native <c>EncodeCapabilitySnapshot</c>.
/// </summary>
public readonly record struct AgentCapabilitySnapshot(
    AgentStatusCode StatusCode,
    bool RuntimeBlocked,
    uint ResolutionGeneration,
    ImmutableArray<AgentCapabilityRecord> Capabilities)
{
    public const int MinimumSize = 11;

    public static AgentCapabilitySnapshot ReadFrom(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        if (span.Length < MinimumSize)
        {
            throw new InvalidDataException(
                $"Agent capability snapshot must be at least {MinimumSize} bytes, actual {span.Length}.");
        }

        var statusCode = (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2));
        bool runtimeBlocked = span[2] != 0;
        uint generation = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(3, 4));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(7, 4));

        int offset = 11;
        var records = ImmutableArray.CreateBuilder<AgentCapabilityRecord>((int)count);
        for (uint i = 0; i < count; i++)
        {
            string id = AgentRuntimeStatusPayload.ReadString(span, ref offset);
            if (offset + 2 > span.Length)
            {
                throw new InvalidDataException("Agent capability snapshot truncated reading state/reason.");
            }

            var state = (AgentCapabilityState)span[offset];
            var reason = (AgentCapabilityReason)span[offset + 1];
            offset += 2;
            records.Add(new AgentCapabilityRecord(id, state, reason));
        }

        return new AgentCapabilitySnapshot(statusCode, runtimeBlocked, generation, records.ToImmutable());
    }
}
