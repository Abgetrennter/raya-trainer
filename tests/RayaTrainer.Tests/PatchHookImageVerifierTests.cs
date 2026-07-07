using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class PatchHookImageVerifierTests
{
    [Fact]
    public void VerifyComparesManifestRestoreBytesWithImageBytes()
    {
        var imageBytes = CreatePeImage();
        imageBytes[0x230] = 0x8B;
        imageBytes[0x231] = 0x50;
        imageBytes[0x232] = 0x3C;
        imageBytes[0x233] = 0x8B;
        imageBytes[0x234] = 0xCE;
        var image = PeImage.FromBytes(imageBytes);
        var manifest = new PatchManifest([
            new PatchHook(
                Address: "ra3_1.12.game+1030",
                SectionTitle: "Player God Mode Code",
                PatchAssembly: ["jmp MustCode+41e"],
                TrampolineTarget: "MustCode+41e",
                ReturnLabel: "_BackPlayerGodMode",
                EnableFlags: ["iEnable+16"],
                OriginalAssembly: ["mov edx,[eax+3c]", "mov ecx,esi"])
        ]);

        var result = Assert.Single(PatchHookImageVerifier.Verify(manifest, image));

        Assert.Equal(0x1030, result.Rva);
        Assert.Equal(0x230, result.RawOffset);
        Assert.Equal(".text", result.SectionName);
        Assert.True(result.Matches);
        Assert.Equal(new byte[] { 0x8B, 0x50, 0x3C, 0x8B, 0xCE }, result.ExpectedBytes);
        Assert.Equal(new byte[] { 0x8B, 0x50, 0x3C, 0x8B, 0xCE }, result.ImageBytes);
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
