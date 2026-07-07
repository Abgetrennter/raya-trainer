namespace RayaTrainer.Core.Agent;

public sealed class AgentMemoryWriteRequest
{
    public AgentMemoryWriteRequest(IEnumerable<AgentMemoryWriteOperation> writes)
    {
        Writes = writes
            .Select(write => write with { Bytes = write.Bytes.ToArray() })
            .ToArray();
    }

    public IReadOnlyList<AgentMemoryWriteOperation> Writes { get; }

    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((uint)Writes.Count));
        foreach (var write in Writes)
        {
            if (write.Bytes.Length == 0)
            {
                throw new InvalidDataException("Memory write cannot be empty.");
            }

            writer.Write(write.Address);
            writer.Write((uint)write.AddressMode);
            writer.Write(checked((uint)write.Bytes.Length));
            writer.Write(write.Bytes);
        }

        return stream.ToArray();
    }
}
