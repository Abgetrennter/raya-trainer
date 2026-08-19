using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Command 70 response: the current selection's engine ObjectIDs as published by the Match
/// Context snapshot. Variable-length wire shape: StatusCode(u16) + Count(u32) + ObjectIds(u32
/// x Count). The WPF Product Intent route uses these IDs to compose Captured bindings; an
/// empty list means nothing is selected (or no snapshot has been published yet).
/// </summary>
public sealed record AgentSelectedObjectIdsPayload(
    AgentStatusCode StatusCode,
    IReadOnlyList<uint> ObjectIds)
{
    // Mirrors the Agent's kMatchContextSelectedObjectMax bound so a corrupt count cannot
    // allocate unbounded memory.
    public const uint MaxObjectIds = 4096;

    public static AgentSelectedObjectIdsPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        if (span.Length < 6)
        {
            throw new InvalidDataException(
                $"Agent selected object ids payload must be at least 6 bytes, actual {span.Length}.");
        }

        var statusCode = (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(2, 4));
        if (count > MaxObjectIds)
        {
            throw new InvalidDataException(
                $"Agent selected object ids count {count} exceeds {MaxObjectIds}.");
        }

        var expectedLength = 6 + checked((int)count) * 4;
        if (span.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Agent selected object ids payload must be {expectedLength} bytes for {count} ids, actual {span.Length}.");
        }

        var objectIds = new uint[count];
        for (var index = 0; index < objectIds.Length; index++)
        {
            objectIds[index] = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(6 + index * 4, 4));
        }

        return new AgentSelectedObjectIdsPayload(statusCode, objectIds);
    }
}
