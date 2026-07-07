using System.Buffers.Binary;

namespace RayaTrainer.Core.Patching;

public sealed class PeImage
{
    private readonly byte[] _bytes;

    private PeImage(byte[] bytes)
    {
        _bytes = bytes;
        (Metadata, Sections) = ReadHeaders(bytes);
    }

    public PeImageMetadata Metadata { get; }

    public IReadOnlyList<PeSectionInfo> Sections { get; }

    public static PeImage Load(string path)
    {
        return FromBytes(File.ReadAllBytes(path));
    }

    public static PeImage FromBytes(byte[] bytes)
    {
        return new PeImage(bytes.ToArray());
    }

    public byte[] ReadRva(int rva, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var mapping = MapRva(rva, count);

        var result = new byte[count];
        Array.Copy(_bytes, mapping.RawOffset, result, 0, count);
        return result;
    }

    public PeRvaMapping MapRva(int rva, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var section = Sections.FirstOrDefault(item => item.Contains(rva, count));
        if (section is null)
        {
            throw new InvalidOperationException($"RVA 0x{rva:X} is not mapped by any PE section.");
        }

        var rawOffset = checked(section.RawPointer + (rva - section.VirtualAddress));
        if (rawOffset < 0 || rawOffset + count > _bytes.Length)
        {
            throw new InvalidOperationException($"RVA 0x{rva:X} maps outside the PE file.");
        }

        return new PeRvaMapping(rva, rawOffset, section);
    }

    private static (PeImageMetadata Metadata, IReadOnlyList<PeSectionInfo> Sections) ReadHeaders(byte[] bytes)
    {
        var peHeaderOffset = ReadInt32(bytes, 0x3C);
        if (ReadUInt32(bytes, peHeaderOffset) != 0x00004550)
        {
            throw new InvalidOperationException("Invalid PE signature.");
        }

        var machine = ReadUInt16(bytes, peHeaderOffset + 4);
        var sectionCount = ReadUInt16(bytes, peHeaderOffset + 6);
        var timeDateStamp = ReadUInt32(bytes, peHeaderOffset + 8);
        var optionalHeaderSize = ReadUInt16(bytes, peHeaderOffset + 20);
        var optionalHeaderOffset = peHeaderOffset + 24;
        var imageBase = ReadImageBase(bytes, optionalHeaderOffset);
        var sizeOfImage = ReadInt32(bytes, optionalHeaderOffset + 56);
        var metadata = new PeImageMetadata(machine, timeDateStamp, imageBase, sizeOfImage);
        var sectionTableOffset = optionalHeaderOffset + optionalHeaderSize;
        var sections = new List<PeSectionInfo>(sectionCount);
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = sectionTableOffset + index * 40;
            var name = ReadSectionName(bytes, offset);
            var virtualSize = ReadInt32(bytes, offset + 8);
            var virtualAddress = ReadInt32(bytes, offset + 12);
            var rawSize = ReadInt32(bytes, offset + 16);
            var rawPointer = ReadInt32(bytes, offset + 20);
            sections.Add(new PeSectionInfo(
                name,
                virtualAddress,
                virtualSize,
                rawPointer,
                rawSize));
        }

        return (metadata, sections);
    }

    private static ulong ReadImageBase(byte[] bytes, int optionalHeaderOffset)
    {
        var magic = ReadUInt16(bytes, optionalHeaderOffset);
        return magic switch
        {
            0x10B => ReadUInt32(bytes, optionalHeaderOffset + 28),
            0x20B => BinaryPrimitives.ReadUInt64LittleEndian(
                bytes.AsSpan(optionalHeaderOffset + 24, sizeof(ulong))),
            _ => throw new InvalidOperationException($"Unsupported PE optional header magic 0x{magic:X}.")
        };
    }

    private static string ReadSectionName(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, 8);
        var length = 0;
        while (length < 8 && bytes[offset + length] != 0)
        {
            length++;
        }

        return System.Text.Encoding.ASCII.GetString(bytes, offset, length);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
    }

    private static int ReadInt32(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
    }

    private static void EnsureRange(byte[] bytes, int offset, int count)
    {
        if (offset < 0 || offset + count > bytes.Length)
        {
            throw new InvalidOperationException("Unexpected end of PE file.");
        }
    }

}

public sealed record PeImageMetadata(
    ushort Machine,
    uint TimeDateStamp,
    ulong ImageBase,
    int SizeOfImage);

public sealed record PeRvaMapping(int Rva, int RawOffset, PeSectionInfo Section);

public sealed record PeSectionInfo(
    string Name,
    int VirtualAddress,
    int VirtualSize,
    int RawPointer,
    int RawSize)
{
    public bool Contains(int rva, int count)
    {
        return rva >= VirtualAddress
            && rva + count <= VirtualAddress + Math.Max(VirtualSize, RawSize)
            && rva - VirtualAddress + count <= RawSize;
    }
}
