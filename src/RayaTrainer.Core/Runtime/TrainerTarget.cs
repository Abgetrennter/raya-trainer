namespace RayaTrainer.Core.Runtime;

public sealed record TrainerTarget(
    string ProcessName,
    nint ModuleBase,
    bool Is32Bit,
    bool VersionSupported,
    int? ProcessId = null,
    string ModulePath = "",
    string FileVersion = "",
    string? VersionProfileId = null,
    bool SignatureCompatibilityMode = false);
