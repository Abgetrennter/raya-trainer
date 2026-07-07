using System.Buffers.Binary;
using System.Text;

namespace RayaTrainer.Core.Agent;

public sealed record AgentSignatureScanPayload(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    uint EntryCount,
    uint MatchedCount,
    IReadOnlyDictionary<string, uint> Addresses)
{
    private const int HeaderSize = 12;
    private const int EntryHeaderSize = 8;

    public bool IsComplete =>
        StatusCode == AgentStatusCode.Ok &&
        EntryCount == MatchedCount &&
        EntryCount == Addresses.Count;

    public static AgentSignatureScanPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < HeaderSize)
        {
            throw new InvalidDataException(
                $"Agent signature scan payload must be at least {HeaderSize} bytes, actual {payload.Length}.");
        }

        var span = payload.Span;
        var statusCode = (AgentStatusCode)BinaryPrimitives.ReadUInt16LittleEndian(span[..2]);
        var agentVersion = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, 4));
        var matchedCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));
        if (matchedCount > entryCount)
        {
            throw new InvalidDataException("Agent signature scan matched count exceeds entry count.");
        }

        var addresses = new Dictionary<string, uint>(checked((int)entryCount), StringComparer.Ordinal);
        var offset = HeaderSize;
        uint actualMatchedCount = 0;
        for (uint index = 0; index < entryCount; ++index)
        {
            if (payload.Length - offset < EntryHeaderSize)
            {
                throw new InvalidDataException("Agent signature scan payload ended before an entry header.");
            }

            var entry = span.Slice(offset, EntryHeaderSize);
            var address = BinaryPrimitives.ReadUInt32LittleEndian(entry[..4]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(4, 2));
            var reserved = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(6, 2));
            offset += EntryHeaderSize;

            if (nameLength == 0 || reserved != 0 || payload.Length - offset < nameLength)
            {
                throw new InvalidDataException("Agent signature scan entry header is invalid.");
            }

            var name = Encoding.UTF8.GetString(span.Slice(offset, nameLength));
            offset += nameLength;
            if (!addresses.TryAdd(name, address))
            {
                throw new InvalidDataException($"Agent signature scan contains duplicate symbol '{name}'.");
            }

            if (address != 0)
            {
                ++actualMatchedCount;
            }
        }

        if (offset != payload.Length || actualMatchedCount != matchedCount)
        {
            throw new InvalidDataException("Agent signature scan payload length or matched count is inconsistent.");
        }

        return new AgentSignatureScanPayload(
            statusCode,
            agentVersion,
            entryCount,
            matchedCount,
            addresses);
    }

    public static byte[] Encode(
        AgentStatusCode statusCode,
        ushort agentVersion,
        IReadOnlyDictionary<string, uint> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        var encodedNames = addresses.Keys
            .Select(name => (Name: name, Bytes: Encoding.UTF8.GetBytes(name)))
            .ToArray();
        if (encodedNames.Any(entry => entry.Bytes.Length is 0 or > ushort.MaxValue))
        {
            throw new ArgumentException("Signature names must encode to 1..65535 UTF-8 bytes.", nameof(addresses));
        }

        var payloadLength = checked(
            HeaderSize + encodedNames.Sum(entry => EntryHeaderSize + entry.Bytes.Length));
        var payload = new byte[payloadLength];
        var span = payload.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span[..2], (ushort)statusCode);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2, 2), agentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), checked((uint)addresses.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(
            span.Slice(8, 4),
            checked((uint)addresses.Values.Count(address => address != 0)));

        var offset = HeaderSize;
        foreach (var entry in encodedNames)
        {
            var address = addresses[entry.Name];
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), address);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset + 4, 2), checked((ushort)entry.Bytes.Length));
            entry.Bytes.CopyTo(span.Slice(offset + EntryHeaderSize));
            offset += EntryHeaderSize + entry.Bytes.Length;
        }

        return payload;
    }
}
