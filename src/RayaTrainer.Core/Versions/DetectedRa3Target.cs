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
    public TrainerTarget ToTrainerTarget()
    {
        return new TrainerTarget(
            ModuleName,
            ModuleBase,
            Is32Bit,
            VersionSupported: SupportStatus == TargetSupportStatus.Installable,
            ProcessId,
            ModulePath,
            FileVersion,
            Profile?.Id);
    }
}
