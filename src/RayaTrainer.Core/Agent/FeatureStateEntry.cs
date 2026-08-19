using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// A single native feature state entry: (StateId, Value) pair.
/// Wire format matches AgentPipeServer.cpp WriteFeatureStatesResponse.
/// </summary>
public readonly record struct FeatureStateEntry(uint StateId, uint Value)
{
    public static FeatureStateEntry ReadFrom(ReadOnlySpan<byte> buffer, int offset)
    {
        var stateId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, 4));
        var value = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset + 4, 4));
        return new FeatureStateEntry(stateId, value);
    }

    public void WriteTo(Span<byte> buffer, int offset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset, 4), StateId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset + 4, 4), Value);
    }

    public static int WireSize => 8;

    public byte[] Encode()
    {
        var bytes = new byte[WireSize];
        WriteTo(bytes, 0);
        return bytes;
    }
}

/// <summary>
/// Response from cmd 7 (GetFeatureStates). Wire format:
///   uint16 StatusCode
///   uint16 AgentVersion
///   uint32 EntryCount
///   repeat EntryCount times: { uint32 StateId, uint32 Value }
/// See AgentPipeServer.cpp WriteFeatureStatesResponse for canonical writer.
/// </summary>
public readonly record struct FeatureStatesResponse(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    IReadOnlyList<FeatureStateEntry> Entries)
{
    public static FeatureStatesResponse ReadFrom(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        var offset = 0;
        var statusCode = (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        offset += 2;
        var agentVersion = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        offset += 2;
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        var entries = new List<FeatureStateEntry>(checked((int)entryCount));
        for (var i = 0; i < entryCount; i++)
        {
            entries.Add(FeatureStateEntry.ReadFrom(span, offset));
            offset += FeatureStateEntry.WireSize;
        }

        return new FeatureStatesResponse(statusCode, agentVersion, entries);
    }
}

/// <summary>
/// Request payload for cmd 5 (SetFeatureStates). Wire format:
///   uint32 Count
///   repeat Count times: { uint32 StateId, uint32 Value }
/// See AgentPipeServer.cpp HandleSetFeatureStates for canonical reader.
/// </summary>
public readonly record struct SetFeatureStatesRequest(IReadOnlyList<(uint StateId, uint Value)> States)
{
    public byte[] Encode()
    {
        var count = States.Count;
        var buffer = new byte[4 + count * FeatureStateEntry.WireSize];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], unchecked((uint)count));

        var offset = 4;
        foreach (var (stateId, value) in States)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), stateId);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset + 4, 4), value);
            offset += FeatureStateEntry.WireSize;
        }

        return buffer;
    }
}
