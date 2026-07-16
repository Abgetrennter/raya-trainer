using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class Ra3VersionProfileRegistryTests
{
    [Fact]
    public void FindByFileVersionReturnsRa3112Profile()
    {
        var profile = Ra3VersionProfileRegistry.FindByFileVersion(GameTarget.ExpectedVersion);

        Assert.NotNull(profile);
        Assert.Equal("ra3_1.12", profile.Id);
        Assert.Equal(GameTarget.ProcessName, profile.ProcessName);
    }

    [Fact]
    public void FindByFileVersionReturnsNullForUnknownBuild()
    {
        Assert.Null(Ra3VersionProfileRegistry.FindByFileVersion("1.12.9999.99999"));
    }

    [Fact]
    public void FindSignatureCompatibilityProfileRequiresKnownFamilyModuleAndX86()
    {
        var compatible = Candidate("ra3_1.12.game", "1.12.9999.99999", is32Bit: true);
        var wrongFamily = Candidate("ra3_1.12.game", "2.0.0.0", is32Bit: true);
        var wrongModule = Candidate("custom.game", "1.12.9999.99999", is32Bit: true);
        var wrongArchitecture = Candidate("ra3_1.12.game", "1.12.9999.99999", is32Bit: false);

        Assert.Same(Ra3VersionProfileRegistry.Ra3112,
            Ra3VersionProfileRegistry.FindSignatureCompatibilityProfile(compatible));
        Assert.Null(Ra3VersionProfileRegistry.FindSignatureCompatibilityProfile(wrongFamily));
        Assert.Null(Ra3VersionProfileRegistry.FindSignatureCompatibilityProfile(wrongModule));
        Assert.Null(Ra3VersionProfileRegistry.FindSignatureCompatibilityProfile(wrongArchitecture));
    }

    [Theory]
    [InlineData("1.13.0.0", "ra3_1.13", "RA3 1.13")]
    [InlineData("1.13.3444.25830", "ra3_1.13", "RA3 1.13")]
    [InlineData("1.0.3313.38400", "ra3_uprising_1.0", "RA3 Uprising 1.0")]
    [InlineData("1.01.0.0", "ra3_uprising_1.1", "RA3 Uprising 1.1")]
    public void FindByFileVersionReturnsKnownNon112Profiles(
        string fileVersion,
        string expectedId,
        string expectedDisplayName)
    {
        var profile = Ra3VersionProfileRegistry.FindByFileVersion(fileVersion);

        Assert.NotNull(profile);
        Assert.Equal(expectedId, profile.Id);
        Assert.Equal(expectedDisplayName, profile.DisplayName);
    }

    [Theory]
    [InlineData("ra3_1.12.game", true)]
    [InlineData("ra3_1.12", true)]
    [InlineData("ra3_1.13.game", false)]
    [InlineData("ra3ep1_1.0.game", false)]
    [InlineData("custom.game", false)]
    public void ProcessNameMatchDoesNotAcceptArbitraryGameExecutables(string processName, bool expected)
    {
        Assert.Equal(expected, Ra3VersionProfileRegistry.Ra3112.MatchesProcessName(processName));
    }

    [Fact]
    public void Ra3113IsPatchInstallableWithAgentAndDirectGameApiBackends()
    {
        Assert.True(Ra3VersionProfileRegistry.Ra3113.IsPatchInstallable);
        Assert.True(Ra3VersionProfileRegistry.Ra3113.SupportsAgentBackend);
        Assert.True(Ra3VersionProfileRegistry.Ra3113.SupportsDirectGameApi);
        Assert.True(Ra3VersionProfileRegistry.Ra3113.SupportsSignatureScanning);
        Assert.True(Ra3VersionProfileRegistry.Ra3112.SupportsSignatureScanning);
    }

    [Fact]
    public void UprisingProfilesAreInstallableWithSignatureScanning()
    {
        // 1.1 is wired for install/agent/DirectGameApi (verified RVA catalog + dedicated
        // Both profiles use independently verified RVA catalogs and the Uprising source set.
        Assert.True(Ra3VersionProfileRegistry.Uprising11.IsPatchInstallable);
        Assert.True(Ra3VersionProfileRegistry.Uprising11.SupportsAgentBackend);
        Assert.True(Ra3VersionProfileRegistry.Uprising11.SupportsDirectGameApi);
        Assert.True(Ra3VersionProfileRegistry.Uprising11.SupportsSignatureScanning);
        Assert.True(Ra3VersionProfileRegistry.Uprising10.IsPatchInstallable);
        Assert.True(Ra3VersionProfileRegistry.Uprising10.SupportsAgentBackend);
        Assert.True(Ra3VersionProfileRegistry.Uprising10.SupportsDirectGameApi);
        Assert.True(Ra3VersionProfileRegistry.Uprising10.SupportsSignatureScanning);
    }

    [Fact]
    public void ResolveTargetProfileKeepsLegacyCompatibilityWithoutExplicitProfileId()
    {
        var target = new TrainerTarget("ra3_1.12.game", 0x400000, true, true);

        Assert.Same(Ra3VersionProfileRegistry.Ra3112, Ra3VersionProfileRegistry.ResolveTargetProfile(target));
    }

    [Fact]
    public void ResolveTargetProfileDoesNotFallbackForUnknownExplicitProfileId()
    {
        var target = new TrainerTarget(
            "ra3_1.12.game",
            0x400000,
            true,
            true,
            VersionProfileId: "ra3_unknown");

        Assert.Null(Ra3VersionProfileRegistry.ResolveTargetProfile(target));
    }

    private static TrainerProcessCandidate Candidate(string moduleName, string version, bool is32Bit)
    {
        return new TrainerProcessCandidate(
            1234,
            Path.GetFileNameWithoutExtension(moduleName),
            moduleName,
            @$"D:\Games\RA3\Data\{moduleName}",
            0x400000,
            is32Bit,
            version);
    }
}
