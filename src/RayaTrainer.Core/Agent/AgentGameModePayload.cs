using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

public readonly record struct AgentGameModePayload(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    int GameMode)
{
    public const int Size = 8;

    public static AgentGameModePayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != Size)
        {
            throw new InvalidDataException(
                $"Agent game mode payload must be {Size} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        return new AgentGameModePayload(
            (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]),
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2)),
            BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));
    }

    public static byte[] Encode(AgentStatusCode statusCode, ushort agentVersion, int gameMode)
    {
        var payload = new byte[Size];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)statusCode);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), agentVersion);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), gameMode);
        return payload;
    }
}
