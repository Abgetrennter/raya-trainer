using RayaTrainer.Core.Patching;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class PeImageTests
{
    [Fact]
    public void MetadataReadsPeHeaderAndSectionTable()
    {
        var image = PeImage.FromBytes(CreatePeImage());

        Assert.Equal(0x14C, image.Metadata.Machine);
        Assert.Equal(0x12345678u, image.Metadata.TimeDateStamp);
        Assert.Equal(0x400000u, image.Metadata.ImageBase);
        Assert.Equal(0x900000, image.Metadata.SizeOfImage);

        var section = Assert.Single(image.Sections);
        Assert.Equal(".text", section.Name);
        Assert.Equal(0x1000, section.VirtualAddress);
        Assert.Equal(0x200, section.VirtualSize);
        Assert.Equal(0x200, section.RawPointer);
        Assert.Equal(0x200, section.RawSize);
    }

    [Fact]
    public void ReadRvaMapsSectionVirtualAddressToRawFileOffset()
    {
        var imageBytes = CreatePeImage();
        imageBytes[0x230] = 0x8B;
        imageBytes[0x231] = 0xCE;
        var image = PeImage.FromBytes(imageBytes);

        var bytes = image.ReadRva(0x1030, 2);

        Assert.Equal(new byte[] { 0x8B, 0xCE }, bytes);
    }

    [Fact]
    public void MapRvaReportsRawOffsetAndContainingSection()
    {
        var image = PeImage.FromBytes(CreatePeImage());

        var mapping = image.MapRva(0x1030, 2);

        Assert.Equal(".text", mapping.Section.Name);
        Assert.Equal(0x230, mapping.RawOffset);
    }

    private static byte[] CreatePeImage()
    {
        var bytes = new byte[0x400];
        WriteUInt32(bytes, 0x3C, 0x80);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        WriteUInt16(bytes, 0x84, 0x14C);
        WriteUInt16(bytes, 0x86, 1);
        WriteUInt32(bytes, 0x88, 0x12345678);
        WriteUInt16(bytes, 0x94, 0xE0);
        var optionalHeader = 0x80 + 24;
        WriteUInt16(bytes, optionalHeader, 0x10B);
        WriteUInt32(bytes, optionalHeader + 28, 0x400000);
        WriteUInt32(bytes, optionalHeader + 56, 0x900000);

        var section = 0x80 + 24 + 0xE0;
        var name = ".text"u8.ToArray();
        Array.Copy(name, 0, bytes, section, name.Length);
        WriteUInt32(bytes, section + 8, 0x200);
        WriteUInt32(bytes, section + 12, 0x1000);
        WriteUInt32(bytes, section + 16, 0x200);
        WriteUInt32(bytes, section + 20, 0x200);
        return bytes;
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
