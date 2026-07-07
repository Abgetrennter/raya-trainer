using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

public readonly record struct AgentPingPayload(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    int ProcessId,
    uint ModuleBase,
    uint NativeRuntimeCapabilities,
    ulong BuildFingerprint = AgentBuildIdentity.Fingerprint)
{
    public const int Size = 24;

    public static byte[] Encode(
        AgentStatusCode statusCode,
        ushort agentVersion,
        int processId,
        uint moduleBase,
        uint nativeRuntimeCapabilities = 7,
        ulong buildFingerprint = AgentBuildIdentity.Fingerprint)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process id must be positive.");
        }

        var buffer = new byte[Size];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, sizeof(ushort)), (ushort)statusCode);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, sizeof(ushort)), agentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, sizeof(uint)), unchecked((uint)processId));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, sizeof(uint)), moduleBase);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, sizeof(uint)), nativeRuntimeCapabilities);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16, sizeof(ulong)), buildFingerprint);
        return buffer;
    }

    public static AgentPingPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != Size)
        {
            throw new InvalidDataException($"Agent ping payload must be {Size} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        return new AgentPingPayload(
            (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]),
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, sizeof(ushort))),
            unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, sizeof(uint)))),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, sizeof(uint))),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, sizeof(uint))),
            BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, sizeof(ulong))));
    }
}
