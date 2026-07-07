using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

public sealed record AgentMemoryWrite(uint Address, byte[] Bytes);

public sealed record AgentPatchHook(
    uint Address,
    uint NativeHookId,
    uint PatchLength,
    byte[] OriginalBytes);

public sealed record AgentInstallPatchesRequest(
    IReadOnlyList<AgentMemoryWrite> Writes,
    IReadOnlyList<AgentPatchHook> Hooks)
{
    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, checked((uint)Writes.Count));
        foreach (var write in Writes)
        {
            WriteUInt32(stream, write.Address);
            WriteBytesWithLength(stream, write.Bytes);
        }

        WriteUInt32(stream, checked((uint)Hooks.Count));
        foreach (var hook in Hooks)
        {
            WriteUInt32(stream, hook.Address);
            WriteUInt32(stream, hook.NativeHookId);
            WriteUInt32(stream, hook.PatchLength);
            WriteBytesWithLength(stream, hook.OriginalBytes);
        }

        return stream.ToArray();
    }

    public static AgentInstallPatchesRequest Decode(ReadOnlyMemory<byte> payload)
    {
        var reader = new PayloadReader(payload);
        var writeCount = reader.ReadUInt32();
        var writes = new List<AgentMemoryWrite>(checked((int)writeCount));
        for (var index = 0; index < writeCount; index++)
        {
            writes.Add(new AgentMemoryWrite(reader.ReadUInt32(), reader.ReadBytesWithLength()));
        }

        var hookCount = reader.ReadUInt32();
        var hooks = new List<AgentPatchHook>(checked((int)hookCount));
        for (var index = 0; index < hookCount; index++)
        {
            hooks.Add(new AgentPatchHook(
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadBytesWithLength()));
        }

        reader.ThrowIfRemaining();
        return new AgentInstallPatchesRequest(writes, hooks);
    }

    private static void WriteBytesWithLength(Stream stream, byte[] bytes)
    {
        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private ref struct PayloadReader
    {
        private ReadOnlySpan<byte> _remaining;

        public PayloadReader(ReadOnlyMemory<byte> payload)
        {
            _remaining = payload.Span;
        }

        public uint ReadUInt32()
        {
            if (_remaining.Length < sizeof(uint))
            {
                throw new InvalidDataException("Agent install payload ended while reading uint32.");
            }

            var value = BinaryPrimitives.ReadUInt32LittleEndian(_remaining[..sizeof(uint)]);
            _remaining = _remaining[sizeof(uint)..];
            return value;
        }

        public byte[] ReadBytesWithLength()
        {
            var length = checked((int)ReadUInt32());
            if (_remaining.Length < length)
            {
                throw new InvalidDataException("Agent install payload ended while reading bytes.");
            }

            var bytes = _remaining[..length].ToArray();
            _remaining = _remaining[length..];
            return bytes;
        }

        public void ThrowIfRemaining()
        {
            if (_remaining.Length != 0)
            {
                throw new InvalidDataException($"Agent install payload has {_remaining.Length} trailing bytes.");
            }
        }
    }
}
