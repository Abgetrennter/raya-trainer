namespace RayaTrainer.Core.Diagnostics;

/// <summary>
/// Discriminator for mismatch diagnostic kinds reported by the agent's
/// <c>GetMismatchDiagnostics</c> (cmd 34) extended v11 payload.
/// </summary>
public enum MismatchKind : byte
{
    /// <summary>
    /// Mismatch captured during hook installation: the original bytes at the hook
    /// address did not match the expected baseline. SubjectId is the native hook id,
    /// or 0 if unknown.
    /// </summary>
    Hook = 0,

    /// <summary>
    /// Mismatch captured during runtime PatchSet pre-install verification: the current
    /// bytes at a PatchSet entry address did not match the expected DisableBytes.
    /// SubjectId is the PatchSetId.
    /// </summary>
    RuntimePatchSet = 1,

    /// <summary>
    /// CodeFlow IP conflict captured during PatchSet enable/restore: a suspended thread's
    /// instruction pointer fell within the ±16 byte guard zone of a CodeFlow entry address.
    /// SubjectId is the PatchSetId.
    /// </summary>
    PatchSetIpConflict = 2,
}
