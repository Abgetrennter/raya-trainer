using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class Ra3VersionDetectorTests
{
    [Fact]
    public void SelectDefaultAutoSelectsSingleInstallableTarget()
    {
        var target = Candidate(100, GameTarget.ProcessName, GameTarget.ExpectedVersion);

        var result = Ra3VersionDetector.SelectDefault(Ra3VersionDetector.DetectAll([target]));

        Assert.Equal(TargetSelectionStatus.SingleAutoSelected, result.Status);
        Assert.Equal(100, result.SelectedTarget?.ProcessId);
        Assert.Equal(TargetSupportStatus.Installable, result.SelectedTarget?.SupportStatus);
    }

    [Fact]
    public void SelectDefaultAutoSelectsOnlyInstallableTargetAmongMany()
    {
        var installable = Candidate(100, GameTarget.ProcessName, GameTarget.ExpectedVersion);
        var unsupported = Candidate(101, "ra3_1.13.game", "2.0.9999.0");

        var result = Ra3VersionDetector.SelectDefault(Ra3VersionDetector.DetectAll([unsupported, installable]));

        Assert.Equal(TargetSelectionStatus.SingleSupportedAmongMany, result.Status);
        Assert.Equal(100, result.SelectedTarget?.ProcessId);
        Assert.Contains("multiple", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectDefaultRequiresUserChoiceWhenMultipleTargetsAreInstallable()
    {
        var first = Candidate(100, GameTarget.ProcessName, GameTarget.ExpectedVersion);
        var second = Candidate(101, GameTarget.ProcessName, GameTarget.ExpectedVersion);

        var result = Ra3VersionDetector.SelectDefault(Ra3VersionDetector.DetectAll([first, second]));

        Assert.Equal(TargetSelectionStatus.AmbiguousRequiresUserChoice, result.Status);
        Assert.Null(result.SelectedTarget);
    }

    [Fact]
    public void SelectDefaultDoesNotAttachWhenOnlyUnsupportedOrUnknownTargetsExist()
    {
        var unsupported = Candidate(100, "ra3_1.13.game", "2.0.9999.0");
        var unknown = Candidate(101, "custom.game", "");

        var result = Ra3VersionDetector.SelectDefault(Ra3VersionDetector.DetectAll([unsupported, unknown]));

        Assert.Equal(TargetSelectionStatus.NoInstallableCandidate, result.Status);
        Assert.Null(result.SelectedTarget);
    }

    [Fact]
    public void DetectAllBindsDetectedTargetToOriginalProcessId()
    {
        var detected = Assert.Single(Ra3VersionDetector.DetectAll([
            Candidate(1234, GameTarget.ProcessName, GameTarget.ExpectedVersion)
        ]));

        var target = detected.ToTrainerTarget();

        Assert.Equal(1234, target.ProcessId);
        Assert.True(target.VersionSupported);
        Assert.Equal("ra3_1.12", target.VersionProfileId);
    }

    [Fact]
    public void DetectAllMarksRecognizedRa3113ProfileInstallable()
    {
        var detected = Assert.Single(Ra3VersionDetector.DetectAll([
            Candidate(1234, "ra3_1.13.game", "1.13.0.0")
        ]));

        var target = detected.ToTrainerTarget();

        Assert.Equal(TargetSupportStatus.Installable, detected.SupportStatus);
        Assert.Equal("ra3_1.13", detected.Profile?.Id);
        Assert.True(target.VersionSupported);
        Assert.Equal("ra3_1.13", target.VersionProfileId);
    }

    [Fact]
    public void DetectAllMarksUnknownBuildInKnownFamilyAsSignatureCompatibilityCandidate()
    {
        var detected = Assert.Single(Ra3VersionDetector.DetectAll([
            Candidate(1234, GameTarget.ProcessName, "1.12.9999.99999")
        ]));

        var target = detected.ToTrainerTarget();

        Assert.Equal(TargetSupportStatus.SignatureCompatibilityCandidate, detected.SupportStatus);
        Assert.Equal(Ra3VersionProfileRegistry.Ra3112.Id, detected.Profile?.Id);
        Assert.True(detected.CanAttemptInstallation);
        Assert.Contains("签名兼容候选", detected.DisplayName, StringComparison.Ordinal);
        Assert.True(target.VersionSupported);
        Assert.True(target.SignatureCompatibilityMode);
        Assert.Equal(
            TargetSelectionStatus.SingleAutoSelected,
            Ra3VersionDetector.SelectDefault([detected]).Status);
    }

    [Fact]
    public void DetectAllDoesNotOfferCompatibilityAcrossVersionFamiliesOrArchitectures()
    {
        var differentFamily = Assert.Single(Ra3VersionDetector.DetectAll([
            Candidate(1234, GameTarget.ProcessName, "2.0.0.0")
        ]));
        var x64Candidate = Candidate(1235, GameTarget.ProcessName, "1.12.9999.0") with { Is32Bit = false };
        var wrongArchitecture = Assert.Single(Ra3VersionDetector.DetectAll([x64Candidate]));

        Assert.Equal(TargetSupportStatus.Unsupported, differentFamily.SupportStatus);
        Assert.Equal(TargetSupportStatus.Unsupported, wrongArchitecture.SupportStatus);
        Assert.False(differentFamily.CanAttemptInstallation);
        Assert.False(wrongArchitecture.CanAttemptInstallation);
    }

    private static TrainerProcessCandidate Candidate(int processId, string moduleName, string fileVersion)
    {
        return new TrainerProcessCandidate(
            processId,
            Path.GetFileNameWithoutExtension(moduleName),
            moduleName,
            @$"D:\Games\RA3\Data\{moduleName}",
            0x400000,
            true,
            fileVersion);
    }
}
