namespace RayaTrainer.Core.Agent;

/// <summary>
/// Client seam for the Reinforcement Preset Console commands (62/63), mirroring the
/// <see cref="IProductControlClient"/> precedent so the App coordinator stays testable
/// without a live pipe.
/// </summary>
public interface IReinforcementPresetConsoleClient
{
    Task<ReplaceReinforcementProjectionResponse> ReplaceReinforcementPresetProjectionAsync(
        int processId,
        ReinforcementPresetProjection projection,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<ReinforcementPresetConsoleState> GetReinforcementPresetConsoleStateAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reinforcement Preset Console client surface (Agent commands 62/63). Requests and
/// responses are framed by <see cref="ReinforcementPresetConsoleWireCodec"/>; the commands
/// are additive, capability-gated behind
/// <see cref="NativeRuntimeCapabilities.ReinforcementPresetConsole"/> and never execute game
/// logic on the pipe worker — command 62 only replaces the Agent-held read-only projection
/// and command 63 only reads the console summary.
/// </summary>
public sealed partial class AgentNamedPipeClient : IReinforcementPresetConsoleClient
{
    public Task<ReplaceReinforcementProjectionResponse> ReplaceReinforcementPresetProjectionAsync(
        int processId,
        ReinforcementPresetProjection projection,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.ReplaceReinforcementPresetProjection,
            ReinforcementPresetConsoleWireCodec.EncodeReplaceProjectionRequest(projection),
            timeout,
            payload => ReinforcementPresetConsoleWireCodec.DecodeReplaceProjectionResponse(payload.Span),
            cancellationToken);
    }

    public Task<ReinforcementPresetConsoleState> GetReinforcementPresetConsoleStateAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetReinforcementPresetConsoleState,
            ReinforcementPresetConsoleWireCodec.EncodeConsoleStateRequest(),
            timeout,
            payload => ReinforcementPresetConsoleWireCodec.DecodeConsoleStateResponse(payload.Span),
            cancellationToken);
    }
}
