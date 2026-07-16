using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerProcessLocatorTests
{
    [Fact]
    public void FindMatchesTargetByUserProvidedFullModulePath()
    {
        var targetPath = @"D:\Games\RA3\Data\ra3_1.12.game";
        var locator = new TrainerProcessLocator(() => [
            new TrainerProcessCandidate(
                ProcessId: 42,
                ProcessName: "UnexpectedHostName",
                ModuleName: "ra3_1.12.game",
                ModulePath: targetPath,
                ModuleBase: 0x400000,
                Is32Bit: true,
                FileVersion: "1.12.3444.25830")
        ]);

        var target = locator.Find(targetPath);

        Assert.NotNull(target);
        Assert.Equal(42, target.ProcessId);
        Assert.Equal((nint)0x400000, target.ModuleBase);
        Assert.True(target.VersionSupported);
    }

    [Fact]
    public void FindMatchesTargetByModuleNameWhenProcessNameContainsDifferentSuffix()
    {
        var locator = new TrainerProcessLocator(() => [
            new TrainerProcessCandidate(
                ProcessId: 43,
                ProcessName: "ra3_1",
                ModuleName: "ra3_1.12.game",
                ModulePath: @"D:\Games\RA3\Data\ra3_1.12.game",
                ModuleBase: 0x500000,
                Is32Bit: true,
                FileVersion: "1.12.3444.25830")
        ]);

        var target = locator.Find("ra3_1.12.game");

        Assert.NotNull(target);
        Assert.Equal(43, target.ProcessId);
        Assert.True(target.VersionSupported);
    }

    [Fact]
    public void FindMarksKnownVersionFamilyAsSignatureCompatibilityCandidate()
    {
        var locator = new TrainerProcessLocator(() => [
            new TrainerProcessCandidate(
                ProcessId: 44,
                ProcessName: "ra3_1.12",
                ModuleName: "ra3_1.12.game",
                ModulePath: @"D:\Games\RA3\Data\ra3_1.12.game",
                ModuleBase: 0x500000,
                Is32Bit: true,
                FileVersion: "1.12.9999.99999")
        ]);

        var target = locator.Find("ra3_1.12.game");

        Assert.NotNull(target);
        Assert.True(target.VersionSupported);
        Assert.True(target.SignatureCompatibilityMode);
        Assert.Equal("ra3_1.12", target.VersionProfileId);
    }

    [Fact]
    public void FindReturnsInstallableUprising10Profile()
    {
        var locator = new TrainerProcessLocator(() => [
            new TrainerProcessCandidate(
                ProcessId: 46,
                ProcessName: "ra3ep1_1.0",
                ModuleName: "ra3ep1_1.0.game",
                ModulePath: @"E:\SteamLibrary\steamapps\common\Command and Conquer Red Alert 3 Uprising\Data\ra3ep1_1.0.game",
                ModuleBase: 0x400000,
                Is32Bit: true,
                FileVersion: "1.0.3313.38400")
        ]);

        var target = locator.Find("ra3ep1_1.0.game");

        Assert.NotNull(target);
        Assert.True(target.VersionSupported);
        Assert.Equal("ra3_uprising_1.0", target.VersionProfileId);
    }

    [Fact]
    public void FindDoesNotTreatUserProvidedFullModulePathAsVersionSupport()
    {
        var targetPath = @"D:\Games\RA3\Data\ra3_1.12.game";
        var locator = new TrainerProcessLocator(() => [
            new TrainerProcessCandidate(
                ProcessId: 45,
                ProcessName: "UnexpectedHostName",
                ModuleName: "ra3_1.12.game",
                ModulePath: targetPath,
                ModuleBase: 0x500000,
                Is32Bit: true,
                FileVersion: "")
        ]);

        var target = locator.Find(targetPath);

        Assert.NotNull(target);
        Assert.False(target.VersionSupported);
    }
}
