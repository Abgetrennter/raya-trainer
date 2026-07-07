namespace RayaTrainer.Core.Agent;

public sealed record AgentMemoryReadRequest(uint Address, uint ByteCount)
{
    public byte[] Encode()
    {
        if (ByteCount == 0)
        {
            throw new InvalidDataException("Memory read cannot be empty.");
        }

        var buffer = new byte[8];
        BitConverter.GetBytes(Address).CopyTo(buffer, 0);
        BitConverter.GetBytes(ByteCount).CopyTo(buffer, 4);
        return buffer;
    }
}
