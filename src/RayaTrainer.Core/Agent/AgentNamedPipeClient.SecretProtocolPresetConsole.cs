namespace RayaTrainer.Core.Agent;

/// <summary>
/// Client seam for the Secret Protocol Preset Console commands (64/65), mirroring the
/// <see cref="IReinforcementPresetConsoleClient"/> precedent so the App coordinator stays
/// testable without a live pipe.
/// </summary>
public interface ISecretProtocolPresetConsoleClient
{
    Task<ReplaceSecretProtocolProjectionResponse> ReplaceSecretProtocolPresetProjectionAsync(
        int processId,
        SecretProtocolPresetProjection projection,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<SecretProtocolPresetConsoleState> GetSecretProtocolPresetConsoleStateAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Secret Protocol Preset Console client surface (Agent commands 64/65). Requests and
/// responses are framed by <see cref="SecretProtocolPresetConsoleWireCodec"/>; the commands
/// are additive, capability-gated behind
/// <see cref="NativeRuntimeCapabilities.SecretProtocolPresetConsole"/> and never execute game
/// logic on the pipe worker — command 64 only replaces the Agent-held read-only projection
/// and command 65 only reads the console summary.
/// </summary>
public sealed partial class AgentNamedPipeClient : ISecretProtocolPresetConsoleClient
{
    public Task<ReplaceSecretProtocolProjectionResponse> ReplaceSecretProtocolPresetProjectionAsync(
        int processId,
        SecretProtocolPresetProjection projection,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.ReplaceSecretProtocolPresetProjection,
            SecretProtocolPresetConsoleWireCodec.EncodeReplaceProjectionRequest(projection),
            timeout,
            payload => SecretProtocolPresetConsoleWireCodec.DecodeReplaceProjectionResponse(payload.Span),
            cancellationToken);
    }

    public Task<SecretProtocolPresetConsoleState> GetSecretProtocolPresetConsoleStateAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetSecretProtocolPresetConsoleState,
            SecretProtocolPresetConsoleWireCodec.EncodeConsoleStateRequest(),
            timeout,
            payload => SecretProtocolPresetConsoleWireCodec.DecodeConsoleStateResponse(payload.Span),
            cancellationToken);
    }
}
