using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

public static class Ra3VersionDetector
{
    public static IReadOnlyList<DetectedRa3Target> DetectAll(IReadOnlyList<TrainerProcessCandidate> candidates)
    {
        return candidates
            .Where(IsRa3Candidate)
            .Select(Detect)
            .ToArray();
    }

    public static TargetSelectionResult SelectDefault(IReadOnlyList<DetectedRa3Target> candidates)
    {
        if (candidates.Count == 0)
        {
            return new TargetSelectionResult(
                TargetSelectionStatus.NoCandidate,
                null,
                candidates,
                "No RA3 process candidate was detected.");
        }

        var installable = candidates
            .Where(candidate => candidate.CanAttemptInstallation)
            .ToArray();

        return installable.Length switch
        {
            0 => new TargetSelectionResult(
                TargetSelectionStatus.NoInstallableCandidate,
                null,
                candidates,
                "RA3 process candidates were detected, but none can be installed or signature-validated."),
            1 when candidates.Count == 1 => new TargetSelectionResult(
                TargetSelectionStatus.SingleAutoSelected,
                installable[0],
                candidates,
                $"Selected {installable[0].Profile?.DisplayName} PID {installable[0].ProcessId}."),
            1 => new TargetSelectionResult(
                TargetSelectionStatus.SingleSupportedAmongMany,
                installable[0],
                candidates,
                $"Detected multiple RA3 process candidates; selected the only attemptable target {installable[0].Profile?.DisplayName} PID {installable[0].ProcessId}."),
            _ => new TargetSelectionResult(
                TargetSelectionStatus.AmbiguousRequiresUserChoice,
                null,
                candidates,
                "Multiple installable or signature-compatible RA3 targets were detected. User selection is required.")
        };
    }

    private static DetectedRa3Target Detect(TrainerProcessCandidate candidate)
    {
        var evidence = new List<VersionEvidence>
        {
            new("FileVersion", candidate.FileVersion, string.IsNullOrWhiteSpace(candidate.FileVersion)
                ? "File version is missing."
                : "File version read from the main module."),
            new("ModuleName", candidate.ModuleName, "Main module name read from the process snapshot.")
        };
        var recognizedProfile = Ra3VersionProfileRegistry.FindRecognizedProfile(candidate);
        var installableProfile = Ra3VersionProfileRegistry.FindInstallableProfile(candidate);
        var compatibilityProfile = installableProfile is null
            ? Ra3VersionProfileRegistry.FindSignatureCompatibilityProfile(candidate)
            : null;
        var supportStatus = installableProfile is not null
            ? TargetSupportStatus.Installable
            : compatibilityProfile is not null
                ? TargetSupportStatus.SignatureCompatibilityCandidate
                : recognizedProfile is not null || IsKnownRa3Name(candidate)
                    ? TargetSupportStatus.Unsupported
                    : TargetSupportStatus.Unknown;

        if (compatibilityProfile is not null)
        {
            evidence.Add(new VersionEvidence(
                "CompatibilityMode",
                compatibilityProfile.Id,
                "Exact build is unknown; module name and version family match a signature-enabled profile."));
        }

        return new DetectedRa3Target(
            candidate.ProcessId,
            candidate.ProcessName,
            candidate.ModuleName,
            candidate.ModulePath,
            candidate.ModuleBase,
            candidate.Is32Bit,
            candidate.FileVersion,
            installableProfile ?? compatibilityProfile ?? recognizedProfile,
            supportStatus,
            evidence);
    }

    private static bool IsRa3Candidate(TrainerProcessCandidate candidate)
    {
        return IsKnownRa3Name(candidate)
            || Path.GetExtension(candidate.ModuleName).Equals(".game", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownRa3Name(TrainerProcessCandidate candidate)
    {
        return KnownRa3Name(candidate.ProcessName)
            || KnownRa3Name(candidate.ModuleName)
            || KnownRa3Name(Path.GetFileName(candidate.ModulePath));
    }

    private static bool KnownRa3Name(string value)
    {
        var name = TrainerProcessName.ToProcessName(value);
        return name.StartsWith("ra3_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ra3ep1_", StringComparison.OrdinalIgnoreCase);
    }
}
