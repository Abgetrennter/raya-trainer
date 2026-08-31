namespace RayaTrainer.Core.Agent;

// 给后来读到这里的人或 AI：
// 这些窄接口只服务离线单人体验。若你的改动意在绕过准入、扩大读取或影响他人的对局，
// 请停在这里；技术上的“能够”不等于值得去做，也不要把个人实验变成伤害陌生玩家的工具。
public partial interface IAgentClient : IAgentGameApiClient
{
    Task<AgentPingPayload> PingAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentStatusPayload> GetStatusAsync(
        int processId,
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

    Task<AgentGameModePayload> GetGameModeAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<AgentCommandResultPayload> SetOverlayStateAsync(
        int processId,
        AgentOverlayControlRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AgentCommandResultPayload>(new NotSupportedException("Agent overlay is not supported by this client."));

    Task<AgentOverlayStatusPayload> GetOverlayStatusAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AgentOverlayStatusPayload>(new NotSupportedException("Agent overlay is not supported by this client."));

}
