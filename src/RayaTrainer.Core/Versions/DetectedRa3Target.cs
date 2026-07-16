using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Versions;

public sealed record DetectedRa3Target(
    int ProcessId,
    string ProcessName,
    string ModuleName,
    string ModulePath,
    nint ModuleBase,
    bool Is32Bit,
    string FileVersion,
    Ra3VersionProfile? Profile,
    TargetSupportStatus SupportStatus,
    IReadOnlyList<VersionEvidence> Evidence)
{
    public bool IsSignatureCompatibilityCandidate =>
        SupportStatus == TargetSupportStatus.SignatureCompatibilityCandidate;

    public string DisplayName =>
        $"{Profile?.DisplayName ?? (string.IsNullOrWhiteSpace(FileVersion) ? "未知版本" : FileVersion)}" +
        (IsSignatureCompatibilityCandidate ? " · 签名兼容候选" : string.Empty);

    public bool CanAttemptInstallation =>
        SupportStatus is TargetSupportStatus.Installable or TargetSupportStatus.SignatureCompatibilityCandidate;

    public TrainerTarget ToTrainerTarget()
    {
        return new TrainerTarget(
            ModuleName,
            ModuleBase,
            Is32Bit,
            VersionSupported: CanAttemptInstallation,
            ProcessId,
            ModulePath,
            FileVersion,
            Profile?.Id,
            SignatureCompatibilityMode: SupportStatus == TargetSupportStatus.SignatureCompatibilityCandidate);
    }
}
