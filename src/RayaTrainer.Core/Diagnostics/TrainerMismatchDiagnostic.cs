using RayaTrainer.Core.Agent;

namespace RayaTrainer.Core.Diagnostics;

/// <summary>
/// Unified mismatch diagnostic record, produced from the agent's extended
/// <see cref="AgentMismatchDiagnosticsPayload"/> or from a host-side
/// <c>PatchMismatchReportWriter</c> pass. Distinguishes Hook mismatches,
/// RuntimePatchSet pre-install byte mismatches, and PatchSet CodeFlow IP conflicts.
/// </summary>
public sealed record TrainerMismatchDiagnostic(
    MismatchKind Kind,
    uint SubjectId,
    uint HookAddress,
    byte[] ExpectedBytes,
    byte[] ActualBytes,
    byte[] DumpBytes,
    string SourceSummary)
{
    /// <summary>
    /// Build a <see cref="TrainerMismatchDiagnostic"/> directly from the agent wire payload.
    /// </summary>
    public static TrainerMismatchDiagnostic FromPayload(AgentMismatchDiagnosticsPayload payload)
    {
        return new TrainerMismatchDiagnostic(
            payload.Kind,
            payload.SubjectId,
            payload.HookAddress,
            payload.ExpectedBytes,
            payload.ActualBytes,
            payload.DumpBytes,
            payload.SourceSummary);
    }
}
