using System.Buffers.Binary;
using System.IO.Pipes;
using System.Threading;

namespace RayaTrainer.Core.Agent;

public sealed partial class AgentNamedPipeClient : IAgentClient
{
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private int _sequenceId;

    public Task<AgentPingPayload> PingAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(processId, AgentCommand.Ping, [], timeout, AgentPingPayload.ReadFrom, cancellationToken);
    }

    public Task<AgentStatusPayload> GetStatusAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(processId, AgentCommand.GetStatus, [], timeout, AgentStatusPayload.ReadFrom, cancellationToken);
    }

    public Task<AgentCommandResultPayload> InstallPatchesAsync(
        int processId,
        AgentInstallPatchesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.InstallPatches,
            request.Encode(),
            timeout,
            AgentCommandResultPayload.ReadFrom,
            cancellationToken);
    }

    public Task<AgentCommandResultPayload> RestorePatchesAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.RestorePatches,
            [],
            timeout,
            AgentCommandResultPayload.ReadFrom,
            cancellationToken);
    }

    // L4: wire to cmd 5
    public Task<AgentCommandResultPayload> SetFeatureStatesAsync(
        int processId,
        SetFeatureStatesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.SetFeatureStates,
            request.Encode(),
            timeout,
            AgentCommandResultPayload.ReadFrom,
            cancellationToken);
    }

    // L5: wire to cmd 6 — SetRuntimePatchSet
    public Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(
        int processId,
        uint patchSetId,
        bool enable,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), patchSetId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), enable ? 1u : 0u);
        return SendCommandAsync(
            processId,
            AgentCommand.SetRuntimePatchSet,
            payload,
            timeout,
            AgentCommandResultPayload.ReadFrom,
            cancellationToken);
    }

    // L4: wire to cmd 7
    public Task<FeatureStatesResponse> GetFeatureStatesAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetFeatureStates,
            [],
            timeout,
            FeatureStatesResponse.ReadFrom,
            cancellationToken);
    }

    public Task<AgentMemoryReadPayload> ReadMemoryAsync(
        int processId,
        AgentMemoryReadRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.ReadMemory,
            request.Encode(),
            timeout,
            AgentMemoryReadPayload.ReadFrom,
            cancellationToken);
    }

    public Task<AgentCommandResultPayload> SetNativeCatalogAsync(
        int processId,
        IReadOnlyList<uint> rvas,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.SetNativeCatalog,
            NativeAgentCatalog.Encode(rvas),
            timeout,
            AgentCommandResultPayload.ReadFrom,
            cancellationToken);
    }

    public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetMismatchDiagnostics,
            [],
            timeout,
            AgentMismatchDiagnosticsPayload.ReadFrom,
            cancellationToken);
    }

    public Task<AgentSignatureScanPayload> ScanSignaturesAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.ScanSignatures,
            [],
            timeout,
            AgentSignatureScanPayload.ReadFrom,
            cancellationToken);
    }

    public Task<AgentGameModePayload> GetGameModeAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            processId,
            AgentCommand.GetGameMode,
            [],
            timeout,
            AgentGameModePayload.ReadFrom,
            cancellationToken);
    }

    private async Task<T> SendCommandAsync<T>(
        int processId,
        AgentCommand command,
        byte[] requestPayload,
        TimeSpan timeout,
        Func<ReadOnlyMemory<byte>, T> parsePayload,
        CancellationToken cancellationToken)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process id must be positive.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (requestPayload.Length > AgentProtocol.MaxPayloadLength)
        {
            throw new InvalidDataException($"Agent request payload length {requestPayload.Length} exceeds limit {AgentProtocol.MaxPayloadLength}.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var effectiveCancellationToken = timeoutSource.Token;

        await _commandGate.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);
        try
        {
            return await SendCommandCoreAsync(
                    processId,
                    command,
                    requestPayload,
                    parsePayload,
                    effectiveCancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<T> SendCommandCoreAsync<T>(
        int processId,
        AgentCommand command,
        byte[] requestPayload,
        Func<ReadOnlyMemory<byte>, T> parsePayload,
        CancellationToken effectiveCancellationToken)
    {
        var sequenceId = unchecked((uint)Interlocked.Increment(ref _sequenceId));

        await using var pipe = new NamedPipeClientStream(
            ".",
            AgentPipeName.ForProcessId(processId),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(effectiveCancellationToken).ConfigureAwait(false);

        var requestHeader = new AgentProtocolHeader(
            AgentProtocol.Magic,
            AgentProtocol.Version,
            command,
            sequenceId,
            checked((uint)requestPayload.Length));
        var requestHeaderBuffer = new byte[AgentProtocol.HeaderSize];
        requestHeader.WriteTo(requestHeaderBuffer);
        await pipe.WriteAsync(requestHeaderBuffer, effectiveCancellationToken).ConfigureAwait(false);
        if (requestPayload.Length > 0)
        {
            await pipe.WriteAsync(requestPayload, effectiveCancellationToken).ConfigureAwait(false);
        }
        await pipe.FlushAsync(effectiveCancellationToken).ConfigureAwait(false);

        var responseHeaderBuffer = new byte[AgentProtocol.HeaderSize];
        await pipe.ReadExactlyAsync(responseHeaderBuffer, effectiveCancellationToken).ConfigureAwait(false);
        var responseHeader = AgentProtocolHeader.ReadFrom(responseHeaderBuffer);
        AgentProtocol.Validate(responseHeader);
        if (responseHeader.Command != command)
        {
            throw new InvalidDataException($"Agent response command mismatch. Expected {command}, actual {responseHeader.Command}.");
        }

        if (responseHeader.SequenceId != sequenceId)
        {
            throw new InvalidDataException($"Agent response sequence mismatch. Expected {sequenceId}, actual {responseHeader.SequenceId}.");
        }

        var responsePayload = new byte[responseHeader.PayloadLength];
        if (responsePayload.Length > 0)
        {
            await pipe.ReadExactlyAsync(responsePayload, effectiveCancellationToken).ConfigureAwait(false);
        }

        return parsePayload(responsePayload);
    }
}
