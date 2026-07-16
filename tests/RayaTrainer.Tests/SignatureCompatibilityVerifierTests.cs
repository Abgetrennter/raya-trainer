using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class SignatureCompatibilityVerifierTests
{
    [Fact]
    public void AllowsRelativeTargetsAndAbsoluteModuleAddressesToDrift()
    {
        byte[] expected = [0xE8, 0x10, 0x00, 0x00, 0x00, 0xA1, 0x00, 0x20, 0x40, 0x00];
        byte[] actual = [0xE8, 0x80, 0x00, 0x00, 0x00, 0xA1, 0x00, 0x30, 0x50, 0x00];

        var result = SignatureCompatibilityVerifier.Verify(
            expected, 0x401000, actual, 0x502000, 0x400000, 0x500000);

        Assert.True(result.Compatible, result.Reason);
    }

    [Fact]
    public void RejectsStructureOffsetDrift()
    {
        byte[] expected = [0x8B, 0x41, 0x28];
        byte[] actual = [0x8B, 0x41, 0x2C];

        var result = SignatureCompatibilityVerifier.Verify(
            expected, 0x401000, actual, 0x501000, 0x400000, 0x500000);

        Assert.False(result.Compatible);
        Assert.Contains("偏移", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOpcodeOrRegisterDrift()
    {
        byte[] expected = [0x8B, 0x41, 0x28];
        byte[] actual = [0x8B, 0x51, 0x28];

        var result = SignatureCompatibilityVerifier.Verify(
            expected, 0x401000, actual, 0x501000, 0x400000, 0x500000);

        Assert.False(result.Compatible);
        Assert.Contains("寄存器", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsRelativeBranchThatMovesOutsideGameModule()
    {
        byte[] expected = [0xE8, 0x10, 0x00, 0x00, 0x00];
        byte[] actual = [0xE8, 0x00, 0x00, 0x00, 0x70];

        var result = SignatureCompatibilityVerifier.Verify(
            expected, 0x401000, actual, 0x501000, 0x400000, 0x500000);

        Assert.False(result.Compatible);
        Assert.Contains("越出模块", result.Reason, StringComparison.Ordinal);
    }
}
