using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// One committed ascension policy row in wire form: the (attributeType, valueBits) pair the
/// Agent reverse-mapped from the policy's frozen-table InstanceId, plus the scope mask and
/// faction exactly as submitted (ascension native AscensionScope / AscensionFaction values).
/// </summary>
public sealed record AscensionPolicyReadbackEntry(
    uint AttributeType,
    uint ValueBits,
    uint ScopeMask,
    uint Faction);

/// <summary>
/// Command 71 response: the committed ascension policy table as published by the
/// AscensionPolicyRuntime. Variable-length wire shape: StatusCode(u16) + Count(u32) + Count x
/// (AttributeType u32, ValueBits u32, ScopeMask u32, Faction u32), little-endian. An empty
/// table means no policies are committed (or the ascension runtime is not registered).
/// </summary>
public sealed record AgentAscensionPoliciesPayload(
    AgentStatusCode StatusCode,
    IReadOnlyList<AscensionPolicyReadbackEntry> Entries)
{
    // Mirrors the Agent's commit-time entry ceiling (ascension kMaxTotalEntries) so a corrupt
    // count cannot allocate unbounded memory.
    public const uint MaxEntries = 512;

    public static AgentAscensionPoliciesPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        if (span.Length < 6)
        {
            throw new InvalidDataException(
                $"Agent ascension policies payload must be at least 6 bytes, actual {span.Length}.");
        }

        var statusCode = (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(2, 4));
        if (count > MaxEntries)
        {
            throw new InvalidDataException(
                $"Agent ascension policies payload carries {count} entries, above the {MaxEntries} ceiling.");
        }

        var expectedLength = 6 + checked((int)count) * 16;
        if (span.Length < expectedLength)
        {
            throw new InvalidDataException(
                $"Agent ascension policies payload must be at least {expectedLength} bytes for {count} entries, actual {span.Length}.");
        }

        var entries = new AscensionPolicyReadbackEntry[count];
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 6 + index * 16;
            entries[index] = new AscensionPolicyReadbackEntry(
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 12, 4)));
        }

        return new AgentAscensionPoliciesPayload(statusCode, entries);
    }
}
