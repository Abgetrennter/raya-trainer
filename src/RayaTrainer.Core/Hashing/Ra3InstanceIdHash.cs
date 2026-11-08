using System.Text;

namespace RayaTrainer.Core.Hashing;

public static class Ra3InstanceIdHash
{
    public static uint Compute(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var bytes = Encoding.ASCII.GetBytes(content.ToLowerInvariant());
        return Compute(bytes);
    }

    private static uint Compute(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        var hash = (uint)bytes.Length;
        var index = 0;
        var remainder = bytes.Length & 3;
        var blocks = bytes.Length >> 2;

        for (var i = 0; i < blocks; i++)
        {
            hash += ReadUInt16(bytes, index);
            hash ^= (ReadUInt16(bytes, index + 2) ^ (hash << 5)) << 11;
            hash += hash >> 11;
            index += 4;
        }

        switch (remainder)
        {
            case 1:
                hash += bytes[index];
                hash ^= hash << 10;
                hash += hash >> 1;
                break;
            case 2:
                hash += ReadUInt16(bytes, index);
                hash ^= hash << 11;
                hash += hash >> 17;
                break;
            case 3:
                hash += ReadUInt16(bytes, index);
                hash ^= hash << 16;
                hash ^= (uint)bytes[index + 2] << 18;
                hash += hash >> 11;
                break;
        }

        hash ^= hash << 3;
        hash += hash >> 5;
        hash ^= hash << 2;
        hash += hash >> 15;
        hash ^= hash << 10;

        return hash;
    }

    private static uint ReadUInt16(ReadOnlySpan<byte> bytes, int index) =>
        (uint)(bytes[index] | (bytes[index + 1] << 8));
}
