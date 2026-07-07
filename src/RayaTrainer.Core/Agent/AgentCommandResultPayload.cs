using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

public readonly record struct AgentCommandResultPayload(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    uint InstalledHookCount)
{
    public const int Size = 8;

    public static byte[] Encode(
        AgentStatusCode statusCode,
        ushort agentVersion,
        uint installedHookCount)
    {
        var buffer = new byte[Size];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, sizeof(ushort)), (ushort)statusCode);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, sizeof(ushort)), agentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, sizeof(uint)), installedHookCount);
        return buffer;
    }

    public static AgentCommandResultPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != Size)
        {
            throw new InvalidDataException($"Agent command result payload must be {Size} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        return new AgentCommandResultPayload(
            (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]),
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, sizeof(ushort))),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, sizeof(uint))));
    }
}
