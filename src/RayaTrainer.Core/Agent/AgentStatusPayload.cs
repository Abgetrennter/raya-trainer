using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

public readonly record struct AgentStatusPayload(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    int ProcessId,
    uint ModuleBase,
    uint InstalledHookCount,
    uint NativeRuntimeCapabilities = 7,
    uint GameThreadTick = 0,
    ulong BuildFingerprint = AgentBuildIdentity.Fingerprint)
{
    public const int Size = 32;

    public static byte[] Encode(
        AgentStatusCode statusCode,
        ushort agentVersion,
        int processId,
        uint moduleBase,
        uint installedHookCount,
        uint nativeRuntimeCapabilities = 7,
        uint gameThreadTick = 0,
        ulong buildFingerprint = AgentBuildIdentity.Fingerprint)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process id must be positive.");
        }

        var buffer = new byte[Size];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), (ushort)statusCode);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), agentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), unchecked((uint)processId));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), moduleBase);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), installedHookCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, 4), nativeRuntimeCapabilities);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20, 4), gameThreadTick);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(24, 8), buildFingerprint);
        return buffer;
    }

    public static AgentStatusPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != Size)
        {
            throw new InvalidDataException($"Agent status payload must be {Size} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        return new AgentStatusPayload(
            (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]),
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2)),
            unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, 4))),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20, 4)),
            BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(24, 8)));
    }
}
