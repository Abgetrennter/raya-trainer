using System.IO;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.Services;

public sealed class AgentCompatibilityException : InvalidOperationException
{
    public AgentCompatibilityException(string message)
        : base(message)
    {
    }

    public AgentCompatibilityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class InjectedAgentBackend
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

    public int? TargetProcessId { get; private set; }

    public IReadOnlyList<SkippedPatchHookPlan> LastSkippedHookPlans { get; private set; } = [];

    public AgentStatusPayload? LastStatus => _lastStatus;

    public AgentSignatureScanPayload? LastSignatureScan { get; private set; }

    public IReadOnlyList<string> LastRequiredUnresolvedSignatures { get; private set; } = [];

    public IReadOnlyList<string> LastOptionalUnresolvedSignatures { get; private set; } = [];

    public IReadOnlyList<string> LastOptionalSignatureSymbols { get; private set; } = [];

    private AgentStatusPayload? _lastStatus;
    private bool _supportsDirectGameApi;
    private IReadOnlyDictionary<string, uint>? _scannedAddresses;

    public async Task<AgentStatusPayload> AttachAsync(
        TrainerTarget target,
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

        ReusedExistingAgent = false;
        var ping = await TryPingExistingAgentAsync(processId, cancellationToken).ConfigureAwait(false);
        if (ping is null)
        {
            var injectionResult = _injector.Inject(processId, agentDllPath, timeout);
            if (!injectionResult.Success)
            {
                throw new InvalidOperationException(injectionResult.Message);
            }

            try
            {
                ping = await _client.PingAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                throw new AgentCompatibilityException(
                    "刚注入的 RayaTrainer Agent 与当前修改器版本不匹配。请关闭游戏和修改器，重新启动后再连接。",
                    ex);
            }
        }
        else
        {
            ReusedExistingAgent = true;
        }

        ValidateAgentIdentity(ping.Value.StatusCode, ping.Value.AgentVersion, ping.Value.BuildFingerprint, "Ping");
        ValidateNativeRuntime(ping.Value.NativeRuntimeCapabilities, "Ping");

        var status = await _client.GetStatusAsync(processId, timeout, cancellationToken).ConfigureAwait(false);
        ValidateAgentIdentity(status.StatusCode, status.AgentVersion, status.BuildFingerprint, "status");
        ValidateNativeRuntime(status.NativeRuntimeCapabilities, "status");

        _lastStatus = status;

        _scannedAddresses = null;
        if (profile.SupportsSignatureScanning && !(ReusedExistingAgent && status.InstalledHookCount > 0))
        {
            var signatureScan = await _client.ScanSignaturesAsync(processId, timeout, cancellationToken)
                .ConfigureAwait(false);
            LastSignatureScan = signatureScan;
            _scannedAddresses = ValidateSignatureCatalog(profile, signatureScan);
        }
        else if (ReusedExistingAgent)
        {
            LastSignatureScan = null;
            LastRequiredUnresolvedSignatures = [];
            LastOptionalUnresolvedSignatures = [];
            LastOptionalSignatureSymbols = [];
        }

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

    private static void ValidateAgentIdentity(
        AgentStatusCode statusCode,
        ushort agentVersion,
        ulong buildFingerprint,
        string operation)
    {
        if (statusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException($"Agent {operation} failed: {statusCode}.");
        }

        if (agentVersion != AgentProtocol.Version || buildFingerprint != AgentBuildIdentity.Fingerprint)
        {
            throw new AgentCompatibilityException(
                $"检测到不兼容的 Agent：protocol={agentVersion}，fingerprint=0x{buildFingerprint:X16}；" +
                $"当前需要 protocol={AgentProtocol.Version}，fingerprint=0x{AgentBuildIdentity.Fingerprint:X16}。请重启游戏后再连接。");
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

    public async Task<AgentCommandResultPayload> InstallPatchesAsync(
        TrainerManifest manifest,
        TrainerTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId || _lastStatus is not AgentStatusPayload status)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        // Deliver the per-profile native catalog BEFORE installing hooks, so every native
        // handler immediately sees the correct profile-specific RVAs. If catalog delivery
        // fails there is nothing to roll back.
        await DeliverNativeCatalogAsync(target, timeout, cancellationToken).ConfigureAwait(false);

        var buildResult = AgentPatchPayloadBuilder.BuildWithDiagnostics(
            manifest,
            target,
            status,
            _scannedAddresses);
        LastSkippedHookPlans = buildResult.SkippedHooks;
        var result = await _client.InstallPatchesAsync(processId, buildResult.Request, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (result.StatusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException($"Agent patch install failed: {result.StatusCode}.");
        }

        _lastStatus = status with { InstalledHookCount = result.InstalledHookCount };
        return result;
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

    private async Task DeliverNativeCatalogAsync(
        TrainerTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        var profile = Ra3VersionProfileRegistry.ResolveTargetProfile(target);
        if (profile is null)
        {
            throw new InvalidOperationException("无法识别当前游戏版本，已停止安装以避免使用错误的 Native 地址。");
        }

        var rvas = _scannedAddresses is not null
            ? profile.BuildNativeAgentCatalogRvas(_scannedAddresses)
            : profile.BuildNativeAgentCatalogRvas();

        var catalogResult = await _client.SetNativeCatalogAsync(processId, rvas, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (catalogResult.StatusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException($"Agent native catalog delivery failed: {catalogResult.StatusCode}.");
        }
    }

    private IReadOnlyDictionary<string, uint> ValidateSignatureCatalog(
        Ra3VersionProfile profile,
        AgentSignatureScanPayload payload)
    {
        if (payload.StatusCode != AgentStatusCode.Ok || payload.EntryCount == 0)
        {
            throw new InvalidOperationException(
                $"Agent signature scan failed: status={payload.StatusCode}, entries={payload.EntryCount}.");
        }

        var addresses = new Dictionary<string, uint>(payload.Addresses, StringComparer.OrdinalIgnoreCase);

        // Build the set of symbols that are either unverified or explicitly unused by this
        // profile. Only these entries may scan as zero; every active hook/ref still fails
        // closed. Explicit optional entries cover scanner symbols removed from a profile's
        // bootstrap source (for example Uprising's superseded one-kill caller RVAs).
        var optionalSymbols = profile.Hooks
            .Where(kv => kv.Value.Status != AddressSupportStatus.Verified)
            .Select(kv => kv.Key)
            .Concat(profile.OptionalSignatureSymbols)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        LastOptionalSignatureSymbols = optionalSymbols
            .Where(addresses.ContainsKey)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        LastOptionalUnresolvedSignatures = addresses
            .Where(entry => entry.Value == 0 && optionalSymbols.Contains(entry.Key))
            .Select(entry => entry.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Check that all required scan entries are resolved (non-zero).
        var unresolved = addresses
            .Where(entry => entry.Value == 0 && !optionalSymbols.Contains(entry.Key))
            .Select(entry => entry.Key)
            .ToArray();
        // Record required scan entries that resolved to zero. These will fall back to the
        // profile's fixed RVA in ResolveHookAddress; the install-stage byte check
        // (PatchMismatch) rejects any fixed RVA whose bytes don't match.
        LastRequiredUnresolvedSignatures = unresolved;

        // Check that all Verified hooks and bootstrap refs have non-zero scanned addresses.
        var requiredSymbols = profile.Hooks
            .Where(kv => kv.Value.Status == AddressSupportStatus.Verified)
            .Select(kv => kv.Key)
            ;
        var missing = requiredSymbols
            .Where(symbol => !addresses.TryGetValue(symbol, out var address) || address == 0)
            .ToArray();
        if (missing.Length > 0)
        {
            // Record Verified hooks/refs whose scan missed. Same fallback applies.
            LastRequiredUnresolvedSignatures = LastRequiredUnresolvedSignatures
                .Concat(missing)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return addresses;
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

    /// <summary>
    /// Pulls the last hook mismatch captured by the DLL. Call this right after a failed
    /// <see cref="InstallPatchesAsync"/> that threw with <see cref="AgentStatusCode.PatchMismatch"/>;
    /// the DLL retains the offending hook's expected/actual/dump bytes until the next install.
    /// </summary>
    public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        return _client.GetMismatchDiagnosticsAsync(processId, timeout, cancellationToken);
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
