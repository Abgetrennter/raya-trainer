using RayaTrainer.Core.Patching;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class PatchEncodingTests
{
    [Fact]
    public void EncodeNearJumpPadsWithNops()
    {
        var patch = X86PatchEncoder.EncodeNearJumpWithNops(source: 0x1000, target: 0x2000, totalLength: 8);

        Assert.Equal(new byte[] { 0xE9, 0xFB, 0x0F, 0x00, 0x00, 0x90, 0x90, 0x90 }, patch);
    }

    [Fact]
    public void EncodeNearJumpRejectsTooShortPatch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            X86PatchEncoder.EncodeNearJumpWithNops(source: 0x1000, target: 0x2000, totalLength: 4));
    }

    [Fact]
    public void OriginalByteParserEncodesRestoreAssembly()
    {
        var bytes = OriginalByteParser.Parse([
            "mov edx,[eax+28]",
            "mov eax,[edx+20]"
        ]);

        Assert.Equal(new byte[] { 0x8B, 0x50, 0x28, 0x8B, 0x42, 0x20 }, bytes);
    }

    [Fact]
    public void OriginalByteParserParsesIntelSuffixHexMemoryDisplacements()
    {
        var bytes = OriginalByteParser.Parse([
            "mov edx,[eax+28h]",
            "fld dword ptr [esi+000001BCh]"
        ]);

        Assert.Equal(new byte[]
        {
            0x8B, 0x50, 0x28,
            0xD9, 0x86, 0xBC, 0x01, 0x00, 0x00
        }, bytes);
    }

    [Fact]
    public void OriginalByteParserEncodesSseRestoreAssembly()
    {
        var bytes = OriginalByteParser.Parse([
            "movss [ebp+00000260],xmm0"
        ]);

        Assert.Equal(new byte[] { 0xF3, 0x0F, 0x11, 0x85, 0x60, 0x02, 0x00, 0x00 }, bytes);
    }

    [Fact]
    public void OriginalByteParserPreservesRa3RegisterComparisonEncoding()
    {
        var bytes = OriginalByteParser.Parse([
            "add edx,[eax+04]",
            "cmp edx,edi"
        ]);

        Assert.Equal(new byte[] { 0x03, 0x50, 0x04, 0x3B, 0xD7 }, bytes);
    }

    [Fact]
    public void OriginalByteParserPreservesRa3RegisterMoveEncoding()
    {
        var bytes = OriginalByteParser.Parse([
            "mov edx,[eax+3c]",
            "mov ecx,esi"
        ]);

        Assert.Equal(new byte[] { 0x8B, 0x50, 0x3C, 0x8B, 0xCE }, bytes);
    }
}
