using System.Diagnostics;
using System.IO;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Host.Services;

public sealed partial class InjectedAgentBackend
{
    private static readonly TimeSpan ExistingAgentProbeTimeout = TimeSpan.FromMilliseconds(50);
    private readonly IAgentInjector _injector;
    private readonly IAgentClient _client;

    public InjectedAgentBackend()
        : this(new AgentInjector(), new AgentNamedPipeClient())
    {
    }

    public InjectedAgentBackend(IAgentInjector injector, IAgentClient client)
    {
        _injector = injector;
        _client = client;
    }

    public bool IsConnected { get; private set; }

    public bool ReusedExistingAgent { get; private set; }

    /// <summary>
    /// True when takeover reused a running Agent whose <see cref="AgentBuildIdentity.BuildId"/>
    /// differs from this host's expected build (same protocol major). The connection is allowed
    /// (plan §7.3); the UI can surface a "game still loads an older Agent" hint.
    /// </summary>
    public bool AgentBuildDiffersFromHost { get; private set; }

    public int? TargetProcessId { get; private set; }

    public AgentStatusPayload? LastStatus => _lastStatus;

    public AgentOverlayStatusPayload? LastOverlayStatus { get; private set; }

    private AgentStatusPayload? _lastStatus;
    private bool _supportsDirectGameApi;

    public async Task<AgentStatusPayload> AttachAsync(
        TrainerTarget target,
        TrainerManifest manifest,
        string agentDllPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (target.ProcessId is not int processId)
        {
            throw new InvalidOperationException("无法确定目标进程 PID。");
        }

        var profile = Ra3VersionProfileRegistry.ResolveTargetProfile(target)
            ?? throw new InvalidOperationException("无法确认目标版本配置，当前不会注入 DLL Agent。");
        if (!profile.SupportsAgentBackend)
        {
            throw new InvalidOperationException(
                $"已识别 {profile.DisplayName}，但该版本尚未启用 DLL Agent。");
        }

        _supportsDirectGameApi = profile.SupportsDirectGameApi;
        LastOverlayStatus = null;

        ReusedExistingAgent = false;
        AgentBuildDiffersFromHost = false;
        var ping = await TryPingExistingAgentAsync(processId, cancellationToken).ConfigureAwait(false);
        if (ping is null)
        {
            var injectionResult = _injector.Inject(processId, agentDllPath, timeout);
            if (!injectionResult.Success)
            {
                throw new InvalidOperationException(injectionResult.Message);
            }

        }
        else
        {
            ReusedExistingAgent = true;
        }

        try
        {
            if (ping is null || ping.Value.StatusCode == AgentStatusCode.Pending)
            {
                ping = await WaitForAgentReadyAsync(processId, timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new AgentCompatibilityException(
                "RayaTrainer Agent 与当前修改器版本不匹配。请关闭游戏和修改器，重新启动后再连接。",
                ex);
        }

        ValidateAgentIdentity(ping.Value.StatusCode, ping.Value.AgentVersion, ping.Value.BuildFingerprint, "Ping");
        ValidateNativeRuntime(ping.Value.NativeRuntimeCapabilities, "Ping");

        // GetStatusAsync provides live InstalledHookCount/GameThreadTick. Identity + native
        // runtime capability were already enforced on the Ping path above; the same Agent speaks
        // both, so re-validating here would only duplicate the 4× check without catching a new
        // failure mode.
        var status = await _client.GetStatusAsync(processId, timeout, cancellationToken).ConfigureAwait(false);

        _lastStatus = status;

        // Agent-owned runtime (plan §6): the Agent self-resolves identity/addresses and installs its
        // own core hooks at process init. Attach no longer scans signatures or attests hook layouts —
        // the live hook sites are already the Agent's JMPs, so any host-side re-read would fail. The
        // App just injects/handshakes and observes the Agent's reported runtime state.
        IsConnected = true;
        TargetProcessId = processId;
        return status;
    }

    private async Task<AgentPingPayload?> TryPingExistingAgentAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client
                .PingAsync(processId, ExistingAgentProbeTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException ex)
        {
            throw new AgentCompatibilityException(
                "检测到游戏中已有不兼容版本的 RayaTrainer Agent。请重启游戏后再连接。",
                ex);
        }
    }

    private async Task<AgentPingPayload> WaitForAgentReadyAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - elapsed.Elapsed;
            var probeTimeout = remaining < TimeSpan.FromMilliseconds(500)
                ? remaining
                : TimeSpan.FromMilliseconds(500);

            try
            {
                var ping = await _client.PingAsync(processId, probeTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (ping.StatusCode != AgentStatusCode.Pending)
                {
                    return ping;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The Pipe may not have been scheduled yet. Keep the single attach attempt alive.
            }
            catch (TimeoutException)
            {
                // Same as above: retry within the overall initialization deadline.
            }
            catch (IOException)
            {
                // A Pipe instance can be recreated between probes; retry without reinjecting.
            }

            remaining = timeout - elapsed.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(
                        remaining < TimeSpan.FromMilliseconds(100)
                            ? remaining
                            : TimeSpan.FromMilliseconds(100),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw new AgentCompatibilityException(
            $"Agent 初始化超过 {timeout.TotalSeconds:0} 秒仍未完成；未执行重复注入，请重启游戏后重试。");
    }

    private void ValidateAgentIdentity(
        AgentStatusCode statusCode,
        ushort agentVersion,
        ulong buildId,
        string operation)
    {
        if (statusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException($"Agent {operation} failed: {statusCode}.");
        }

        // Agent-owned runtime takeover rules (plan §7.3): refuse only on a wire-incompatible
        // protocol major. A matching major with a different BuildId means the game still loads a
        // different build of the same wire contract; the connection is allowed and per-feature
        // gaps surface through capability negotiation, but the stale-build hint is recorded.
        switch (AgentBuildIdentity.EvaluateTakeover(agentVersion, buildId))
        {
            case AgentTakeoverDecision.IncompatibleProtocol:
                throw new AgentCompatibilityException(
                    $"检测到不兼容的 Agent：protocol={agentVersion}；" +
                    $"当前需要 protocol={AgentProtocol.Version}。请重启游戏后再连接。");
            case AgentTakeoverDecision.DifferentBuild:
                AgentBuildDiffersFromHost = true;
                break;
            case AgentTakeoverDecision.Compatible:
            default:
                break;
        }
    }

    private static void ValidateNativeRuntime(uint capabilities, string operation)
    {
        var required = (uint)NativeRuntimeCapabilities.Required;
        if ((capabilities & required) != required)
        {
            throw new AgentCompatibilityException(
                $"Agent {operation} 缺少 Native runtime capability：actual=0x{capabilities:X8}，required=0x{required:X8}。请重启游戏后再连接。 ");
        }
    }

    public async Task<AgentCommandResultPayload> RestorePatchesAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。.");
        }

        var result = await _client.RestorePatchesAsync(processId, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (result.StatusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException($"Agent patch restore failed: {result.StatusCode}.");
        }

        if (_lastStatus is AgentStatusPayload status)
        {
            _lastStatus = status with { InstalledHookCount = result.InstalledHookCount };
        }

        return result;
    }

    public async Task<AgentCommandResultPayload> SetOverlayStateAsync(
        bool enabled,
        bool visible,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        var result = await _client
            .SetOverlayStateAsync(
                processId,
                new AgentOverlayControlRequest(enabled, visible),
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.AgentVersion != AgentProtocol.Version)
        {
            throw new AgentCompatibilityException(
                $"Overlay 命令返回了不兼容的 Agent 协议版本 {result.AgentVersion}。");
        }

        return result;
    }

    public async Task<AgentOverlayStatusPayload> GetOverlayStatusAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        LastOverlayStatus = await _client
            .GetOverlayStatusAsync(processId, timeout, cancellationToken)
            .ConfigureAwait(false);
        return LastOverlayStatus.Value;
    }

    public async Task<AgentStatusPayload> GetStatusAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        _lastStatus = await _client.GetStatusAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
        return _lastStatus.Value;
    }

    public ITrainerFeatureController CreateFeatureController(AgentStatusPayload status)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        return new AgentFeatureController(_client, processId, status, _supportsDirectGameApi);
    }

}
