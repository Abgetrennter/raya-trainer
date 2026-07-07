namespace RayaTrainer.Core.Agent;

public sealed record AgentMemoryReadPayload(
    AgentStatusCode StatusCode,
    ushort AgentVersion,
    uint Address,
    byte[] Bytes)
{
    private const int FixedSize = 12;

    public uint ByteCount => checked((uint)Bytes.Length);

    public static byte[] Encode(
        AgentStatusCode statusCode,
        ushort agentVersion,
        uint address,
        byte[] bytes)
    {
        var payload = new byte[FixedSize + bytes.Length];
        BitConverter.GetBytes((ushort)statusCode).CopyTo(payload, 0);
        BitConverter.GetBytes(agentVersion).CopyTo(payload, 2);
        BitConverter.GetBytes(address).CopyTo(payload, 4);
        BitConverter.GetBytes(checked((uint)bytes.Length)).CopyTo(payload, 8);
        bytes.CopyTo(payload, FixedSize);
        return payload;
    }

    public static AgentMemoryReadPayload ReadFrom(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < FixedSize)
        {
            throw new InvalidDataException($"Agent memory read payload must be at least {FixedSize} bytes.");
        }

        var span = payload.Span;
        var statusCode = (AgentStatusCode)BitConverter.ToUInt16(span[..2]);
        var agentVersion = BitConverter.ToUInt16(span.Slice(2, 2));
        var address = BitConverter.ToUInt32(span.Slice(4, 4));
        var byteCount = BitConverter.ToUInt32(span.Slice(8, 4));
        if (byteCount > payload.Length - FixedSize)
        {
            throw new InvalidDataException("Agent memory read payload length is inconsistent.");
        }

        return new AgentMemoryReadPayload(
            statusCode,
            agentVersion,
            address,
            span.Slice(FixedSize, checked((int)byteCount)).ToArray());
    }
}
