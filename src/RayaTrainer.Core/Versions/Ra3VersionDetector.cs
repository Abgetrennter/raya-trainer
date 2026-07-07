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
            .Where(candidate => candidate.SupportStatus == TargetSupportStatus.Installable)
            .ToArray();

        return installable.Length switch
        {
            0 => new TargetSelectionResult(
                TargetSelectionStatus.NoInstallableCandidate,
                null,
                candidates,
                "RA3 process candidates were detected, but none are installable."),
            1 when candidates.Count == 1 => new TargetSelectionResult(
                TargetSelectionStatus.SingleAutoSelected,
                installable[0],
                candidates,
                $"Selected {installable[0].Profile?.DisplayName} PID {installable[0].ProcessId}."),
            1 => new TargetSelectionResult(
                TargetSelectionStatus.SingleSupportedAmongMany,
                installable[0],
                candidates,
                $"Detected multiple RA3 process candidates; selected the only installable target {installable[0].Profile?.DisplayName} PID {installable[0].ProcessId}."),
            _ => new TargetSelectionResult(
                TargetSelectionStatus.AmbiguousRequiresUserChoice,
                null,
                candidates,
                "Multiple installable RA3 targets were detected. User selection is required.")
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
        var supportStatus = installableProfile is not null
            ? TargetSupportStatus.Installable
            : recognizedProfile is not null || IsKnownRa3Name(candidate)
                ? TargetSupportStatus.Unsupported
                : TargetSupportStatus.Unknown;

        return new DetectedRa3Target(
            candidate.ProcessId,
            candidate.ProcessName,
            candidate.ModuleName,
            candidate.ModulePath,
            candidate.ModuleBase,
            candidate.Is32Bit,
            candidate.FileVersion,
            installableProfile ?? recognizedProfile,
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
