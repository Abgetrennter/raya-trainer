namespace RayaTrainer.Core.Agent;

// Private diagnostics surface for AgentCommand.GetRuntimeDiagnostics (cmd 68). The Agent owns
// runtime resolution diagnostics (identity, layout, capability readiness, and now asset-runtime
// state); this surfaces the human-readable lines to the host so the Phase 1 asset load result
// (asset.runtime.state / asset.templates.resolved) is observable. Part of the public projection since 2026-08 (the runtime asset session chain surfaces asset-runtime diagnostics).
public sealed partial class AgentNamedPipeClient
{
    public Task<AgentRuntimeDiagnosticsPayload> GetRuntimeDiagnosticsAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetRuntimeDiagnostics,
            [],
            timeout,
            AgentRuntimeDiagnosticsPayload.ReadFrom,
            cancellationToken);
    }
}
