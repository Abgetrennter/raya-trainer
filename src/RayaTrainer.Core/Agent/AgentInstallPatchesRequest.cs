using System.Buffers.Binary;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Single write byte-patch operation. Kept for backward compat but no longer
/// used in the install-patches wire format (v11 replaces it with PatchSets).
/// </summary>
public sealed record AgentMemoryWrite(uint Address, byte[] Bytes);

/// <summary>
/// One entry within a runtime patch set: an address, its kind (Data/CodeFlow),
/// and the enable/disable byte sequences.
/// </summary>
public sealed record AgentPatchSetEntry(
    uint Address,
    byte Kind,
    byte[] EnableBytes,
    byte[] DisableBytes);

/// <summary>
/// A named runtime patch set: a collection of byte patches that the native agent
/// applies atomically via cmd 6 (SetRuntimePatchSet).
/// </summary>
public sealed record AgentPatchSetPayload(
    uint Id,
    IReadOnlyList<AgentPatchSetEntry> Entries);

public sealed record AgentPatchHook(
    uint Address,
    uint NativeHookId,
    uint PatchLength,
    byte[] OriginalBytes);

/// <summary>
/// v11 install-patches payload: PatchSets + Hooks (no more bare Writes).
/// Wire format:
///   uint32 PatchSetCount
///   per PatchSet:
///     uint32 Id
///     uint32 EntryCount
///     per Entry:
///       uint32 Address (resolved VA)
///       uint8  Kind (0=Data, 1=CodeFlow)
///       uint32 EnableByteCount + bytes
///       uint32 DisableByteCount + bytes
///   uint32 HookCount
///   per Hook: (unchanged)
/// </summary>
public sealed record AgentInstallPatchesRequest(
    IReadOnlyList<AgentPatchSetPayload> PatchSets,
    IReadOnlyList<AgentPatchHook> Hooks)
{
    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, checked((uint)PatchSets.Count));
        foreach (var patchSet in PatchSets)
        {
            WriteUInt32(stream, patchSet.Id);
            WriteUInt32(stream, checked((uint)patchSet.Entries.Count));
            foreach (var entry in patchSet.Entries)
            {
                WriteUInt32(stream, entry.Address);
                stream.WriteByte(entry.Kind);
                WriteBytesWithLength(stream, entry.EnableBytes);
                WriteBytesWithLength(stream, entry.DisableBytes);
            }
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
        var patchSetCount = reader.ReadUInt32();
        var patchSets = new List<AgentPatchSetPayload>(checked((int)patchSetCount));
        for (var ps = 0; ps < patchSetCount; ps++)
        {
            var id = reader.ReadUInt32();
            var entryCount = reader.ReadUInt32();
            var entries = new List<AgentPatchSetEntry>(checked((int)entryCount));
            for (var e = 0; e < entryCount; e++)
            {
                entries.Add(new AgentPatchSetEntry(
                    reader.ReadUInt32(),
                    reader.ReadByte(),
                    reader.ReadBytesWithLength(),
                    reader.ReadBytesWithLength()));
            }

            patchSets.Add(new AgentPatchSetPayload(id, entries));
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
        return new AgentInstallPatchesRequest(patchSets, hooks);
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

        public byte ReadByte()
        {
            if (_remaining.Length < 1)
            {
                throw new InvalidDataException("Agent install payload ended while reading byte.");
            }

            var value = _remaining[0];
            _remaining = _remaining[1..];
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
