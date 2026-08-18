namespace RayaTrainer.Core.Diagnostics;

/// <summary>
/// Unified mismatch diagnostic record used by private runtime diagnostics. Distinguishes Hook mismatches,
/// RuntimePatchSet pre-install byte mismatches, and PatchSet CodeFlow IP conflicts.
/// </summary>
public sealed partial record TrainerMismatchDiagnostic(
    MismatchKind Kind,
    uint SubjectId,
    uint HookAddress,
    byte[] ExpectedBytes,
    byte[] ActualBytes,
    byte[] DumpBytes,
    string SourceSummary);
