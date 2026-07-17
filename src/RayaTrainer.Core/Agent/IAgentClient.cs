namespace RayaTrainer.Core.Agent;

public interface IAgentClient : IAgentGameApiClient
{
    Task<AgentPingPayload> PingAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentStatusPayload> GetStatusAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentCommandResultPayload> InstallPatchesAsync(
        int processId,
        AgentInstallPatchesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentCommandResultPayload> RestorePatchesAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cmd 5: SetFeatureStates — writes one or more native feature state values.
    /// Replaces the old SetToggle/TriggerAction/WriteResourceValues commands.
    /// </summary>
    Task<AgentCommandResultPayload> SetFeatureStatesAsync(
        int processId,
        SetFeatureStatesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cmd 6: SetRuntimePatchSet — enables or disables a named runtime patch set
    /// (collection of byte patches applied as a single atomic unit).
    /// </summary>
    Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(
        int processId,
        uint patchSetId,
        bool enable,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cmd 7: GetFeatureStates — reads a snapshot of all current native feature states
    /// from the injected DLL. Returns the observed state map for client-side caching.
    /// </summary>
    Task<FeatureStatesResponse> GetFeatureStatesAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentMemoryReadPayload> ReadMemoryAsync(
        int processId,
        AgentMemoryReadRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers the per-profile native agent catalog (game-module RVAs) to the injected DLL.
    /// Must be called after <see cref="InstallPatchesAsync"/> and before any DirectGameApi
    /// command on non-legacy profiles; the DLL rejects DirectGameApi while the catalog is
    /// missing or incomplete.
    /// </summary>
    Task<AgentCommandResultPayload> SetNativeCatalogAsync(
        int processId,
        IReadOnlyList<uint> rvas,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the last hook mismatch captured by the DLL during a failed
    /// <see cref="InstallPatchesAsync"/>. When the payload's <see cref="AgentMismatchDiagnosticsPayload.HasMismatch"/>
    /// is true, the returned bytes (expected/actual/dump) describe the offending hook site
    /// and can be fed to <c>PatchMismatchReportWriter</c> just like the external-memory
    /// backend's skipped-hook report.
    /// </summary>
    Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentSignatureScanPayload> ScanSignaturesAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentGameModePayload> GetGameModeAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

}
