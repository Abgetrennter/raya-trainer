using System.IO;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.Services;

public sealed class InjectedAgentBackend
{
    private static readonly TimeSpan ExistingAgentProbeTimeout = TimeSpan.FromMilliseconds(50);
    private const ulong MaximumModuleSpan = 0x02000000;
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

    /// <summary>
    /// Native hook IDs that were successfully included in the last InstallPatchesAsync request.
    /// Populated after a successful install. Empty before first install or after restore.
    /// </summary>
    public IReadOnlyCollection<uint> InstalledNativeHookIds { get; private set; } = Array.Empty<uint>();

    /// <summary>
    /// PatchSet IDs that were successfully registered in the last install.
    /// L5 populates this. Currently a stub for L4 composite capability scaffolding.
    /// </summary>
    public IReadOnlyCollection<uint> PatchSetsRegistered { get; private set; } = Array.Empty<uint>();

    /// <summary>
    /// PatchSet IDs omitted from the install request because the live process did not
    /// match their fixed-RVA disabled-byte baseline. Hooks remain independently installable.
    /// </summary>
    public IReadOnlyCollection<uint> SkippedPatchSetIds { get; private set; } = Array.Empty<uint>();

    /// <summary>
    /// Maximum time to wait for the native catalog delivery (SetNativeCatalogAsync)
    /// to complete. Default is 5 seconds. Must be set before calling
    /// <see cref="DeliverNativeCatalogAsync"/>.
    /// </summary>
    public TimeSpan CatalogDeliveryTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public AgentStatusPayload? LastStatus => _lastStatus;

    public AgentSignatureScanPayload? LastSignatureScan { get; private set; }

    public IReadOnlyList<string> LastRequiredUnresolvedSignatures { get; private set; } = [];

    public IReadOnlyList<string> LastOptionalUnresolvedSignatures { get; private set; } = [];

    public IReadOnlyList<string> LastOptionalSignatureSymbols { get; private set; } = [];

    public bool SignatureCompatibilityValidated { get; private set; }

    /// <summary>
    /// Whether the native address catalog was successfully delivered to the agent.
    /// Set to true after <see cref="DeliverNativeCatalogAsync"/> completes successfully,
    /// reset to false on <see cref="AttachAsync"/>.
    /// </summary>
    public bool IsNativeCatalogDelivered => _nativeCatalogDelivered;

    private AgentStatusPayload? _lastStatus;
    private bool _supportsDirectGameApi;
    private IReadOnlyDictionary<string, uint>? _scannedAddresses;
    private IReadOnlyDictionary<string, byte[]>? _attestedHookBytes;
    private bool _nativeCatalogDelivered;

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

        if (target.SignatureCompatibilityMode &&
            (!target.Is32Bit ||
             !profile.SupportsSignatureScanning ||
             !profile.MatchesProcessName(target.ProcessName) ||
             !profile.MatchesFileVersionFamily(target.FileVersion)))
        {
            throw new AgentCompatibilityException(
                "签名兼容候选不满足同版本族、同模块名、x86 和签名扫描门禁，已拒绝注入。");
        }

        _supportsDirectGameApi = profile.SupportsDirectGameApi;
        SignatureCompatibilityValidated = false;
        _nativeCatalogDelivered = false;
        PatchSetsRegistered = Array.Empty<uint>();
        SkippedPatchSetIds = Array.Empty<uint>();

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

        if (target.SignatureCompatibilityMode && ReusedExistingAgent && status.InstalledHookCount > 0)
        {
            throw new AgentCompatibilityException(
                "签名兼容候选中检测到已经安装的 Hook，无法校验原始指令。请重启游戏后再连接。");
        }

        _scannedAddresses = null;
        _attestedHookBytes = null;
        if (profile.SupportsSignatureScanning && !(ReusedExistingAgent && status.InstalledHookCount > 0))
        {
            var signatureScan = await _client.ScanSignaturesAsync(processId, timeout, cancellationToken)
                .ConfigureAwait(false);
            LastSignatureScan = signatureScan;
            _scannedAddresses = ValidateSignatureCatalog(profile, signatureScan, target.SignatureCompatibilityMode);
            _attestedHookBytes = await ValidateScannedHookLayoutsAsync(
                    manifest,
                    profile,
                    target,
                    processId,
                    _scannedAddresses,
                    requireAllHooks: target.SignatureCompatibilityMode,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            if (target.SignatureCompatibilityMode)
            {
                SignatureCompatibilityValidated = true;
            }
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
        if (!_nativeCatalogDelivered)
        {
            throw new InvalidOperationException(
                "NATIVE_CATALOG_PENDING: Native catalog has not been delivered. " +
                "Call DeliverNativeCatalogAsync before InstallPatchesAsync.");
        }

        if (TargetProcessId is not int processId || _lastStatus is not AgentStatusPayload status)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        var buildResult = AgentPatchPayloadBuilder.BuildWithDiagnostics(
            manifest,
            target,
            status,
            _scannedAddresses,
            _attestedHookBytes);
        LastSkippedHookPlans = buildResult.SkippedHooks;
        var request = await FilterUnsupportedPatchSetsAsync(
                processId,
                buildResult.Request,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        var result = await _client.InstallPatchesAsync(processId, request, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (result.StatusCode != AgentStatusCode.Ok)
        {
            if (result.StatusCode == AgentStatusCode.PatchMismatch)
            {
                // Silently query diagnostics for richer reporting downstream.
                // This populates LastMismatchDiagnostic with kind/subject discrimination.
                await GetMismatchDiagnosticsAsync(timeout, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Agent patch install failed: {result.StatusCode}.");
        }

        // Track the native hook IDs that were successfully included in the install request
        InstalledNativeHookIds = request.Hooks
            .Select(h => h.NativeHookId)
            .Order()
            .ToArray();

        // L5: record the PatchSet IDs that were registered with this install
        PatchSetsRegistered = request.PatchSets
            .Select(p => p.Id)
            .Order()
            .ToArray();

        _lastStatus = status with { InstalledHookCount = result.InstalledHookCount };
        return result;
    }

    private async Task<AgentInstallPatchesRequest> FilterUnsupportedPatchSetsAsync(
        int processId,
        AgentInstallPatchesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (request.PatchSets.Count == 0)
        {
            SkippedPatchSetIds = Array.Empty<uint>();
            return request;
        }

        var supported = new List<AgentPatchSetPayload>(request.PatchSets.Count);
        var skipped = new List<uint>();
        foreach (var patchSet in request.PatchSets)
        {
            var matchesDisabledBaseline = true;
            foreach (var entry in patchSet.Entries)
            {
                var read = await _client.ReadMemoryAsync(
                        processId,
                        new AgentMemoryReadRequest(entry.Address, checked((uint)entry.DisableBytes.Length)),
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read.StatusCode != AgentStatusCode.Ok ||
                    read.AgentVersion != AgentProtocol.Version ||
                    read.Address != entry.Address ||
                    read.Bytes.Length != entry.DisableBytes.Length)
                {
                    throw new InvalidOperationException(
                        $"无法验证当前游戏构建的可选 PatchSet {patchSet.Id}，已停止安装以避免写入未知地址。");
                }

                if (entry.Kind != (byte)PatchSetEntryKind.DerivedStateReset &&
                    !read.Bytes.SequenceEqual(entry.DisableBytes))
                {
                    matchesDisabledBaseline = false;
                    break;
                }
            }

            if (matchesDisabledBaseline)
            {
                supported.Add(patchSet);
            }
            else
            {
                skipped.Add(patchSet.Id);
            }
        }

        SkippedPatchSetIds = skipped.Order().ToArray();
        return request with { PatchSets = supported };
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

    /// <summary>
    /// Delivers the per-profile native agent catalog to the injected DLL.
    /// Must be called after <see cref="AttachAsync"/> and before
    /// <see cref="InstallPatchesAsync"/>. Uses <see cref="CatalogDeliveryTimeout"/>
    /// as the maximum time to wait for the delivery to complete.
    /// On success, sets <see cref="_nativeCatalogDelivered"/> to true.
    /// On timeout or failure, throws <see cref="NativeCatalogDeliveryException"/>.
    /// </summary>
    public async Task DeliverNativeCatalogAsync(
        TrainerTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId)
        {
            throw new NativeCatalogDeliveryException("Agent 尚未连接。");
        }

        var profile = Ra3VersionProfileRegistry.ResolveTargetProfile(target);
        if (profile is null)
        {
            throw new NativeCatalogDeliveryException("无法识别当前游戏版本，已停止安装以避免使用错误的 Native 地址。");
        }

        var rvas = _scannedAddresses is not null
            ? profile.BuildNativeAgentCatalogRvas(
                _scannedAddresses,
                requireScannedAddresses: target.SignatureCompatibilityMode,
                actualModuleBaseVa: checked((uint)target.ModuleBase.ToInt64()))
            : profile.BuildNativeAgentCatalogRvas();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CatalogDeliveryTimeout);

        try
        {
            var catalogResult = await _client
                .SetNativeCatalogAsync(processId, rvas, timeout, timeoutCts.Token)
                .ConfigureAwait(false);
            if (catalogResult.StatusCode != AgentStatusCode.Ok)
            {
                throw new NativeCatalogDeliveryException(
                    $"Agent native catalog delivery failed: {catalogResult.StatusCode}.");
            }

            _nativeCatalogDelivered = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NativeCatalogDeliveryException(
                "Agent 已注入但地址表未送达，请重新连接修改器。");
        }
        catch (Exception ex) when (ex is not NativeCatalogDeliveryException and not OperationCanceledException)
        {
            // Wrap unexpected exceptions from SetNativeCatalogAsync into
            // NativeCatalogDeliveryException. Timeout/caller-triggered
            // OperationCanceledException propagates as-is.
            throw new NativeCatalogDeliveryException(
                "Agent native catalog delivery failed.", ex);
        }
    }

    private IReadOnlyDictionary<string, uint> ValidateSignatureCatalog(
        Ra3VersionProfile profile,
        AgentSignatureScanPayload payload,
        bool requireAllActiveSymbols)
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
        // Exact profiles may fall back to a fixed RVA and rely on the install-stage byte
        // check. Signature-compatibility candidates reject this list below.
        LastRequiredUnresolvedSignatures = unresolved;

        // Verified hooks are always required. Compatibility candidates additionally require
        // every active address-class native ref so no fixed RVA can enter their catalog.
        IEnumerable<string> requiredSymbols = profile.Hooks
            .Where(kv => kv.Value.Status == AddressSupportStatus.Verified)
            .Select(kv => kv.Key);
        if (requireAllActiveSymbols)
        {
            requiredSymbols = requiredSymbols.Concat(profile.NativeAgentRefs
                .Where(kv => kv.Value.Status == AddressSupportStatus.Verified && kv.Value.Rva is not null)
                .Select(kv => kv.Key)
                .SelectMany(name => NativeAgentRefSignatureMapping.TryGetSignatureKey(name, out var signatureKey)
                    ? [signatureKey]
                    : Array.Empty<string>()));
        }

        var requiredSymbolSet = requiredSymbols
            .Where(symbol => !optionalSymbols.Contains(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missing = requiredSymbolSet
            .Where(symbol => !addresses.TryGetValue(symbol, out var address) || address == 0)
            .ToArray();
        if (missing.Length > 0)
        {
            // Record verified hooks/refs whose scan missed. Exact profiles may fall back;
            // compatibility candidates reject them below.
            LastRequiredUnresolvedSignatures = LastRequiredUnresolvedSignatures
                .Concat(missing)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (requireAllActiveSymbols && LastRequiredUnresolvedSignatures.Count > 0)
        {
            throw new AgentCompatibilityException(
                "签名兼容校验未通过：以下必需地址未能唯一定位，未安装任何 Patch：" +
                string.Join(", ", LastRequiredUnresolvedSignatures) + "。");
        }

        return addresses;
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> ValidateScannedHookLayoutsAsync(
        TrainerManifest manifest,
        Ra3VersionProfile profile,
        TrainerTarget target,
        int processId,
        IReadOnlyDictionary<string, uint> scannedAddresses,
        bool requireAllHooks,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var plans = PatchHookPlanner.CreateSupportedPlans(
            manifest.PatchManifest,
            profile,
            includeUnlistedHooks: false).Plans;
        if (plans.Count == 0)
        {
            if (requireAllHooks)
            {
                throw new AgentCompatibilityException("签名兼容校验没有可验证的 Hook，已停止安装。");
            }

            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }

        var actualModuleBase = checked((ulong)target.ModuleBase.ToInt64());
        var attestedBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            var key = string.IsNullOrWhiteSpace(plan.ReturnLabel) ? plan.Address : plan.ReturnLabel;
            if (!profile.Hooks.TryGetValue(key, out var profileHook) || profileHook.Rva is not int expectedRva)
            {
                throw new AgentCompatibilityException(
                    $"签名地址校验未通过：Hook {key} 缺少已知版本指令基线，未安装任何 Patch。");
            }

            if (!scannedAddresses.TryGetValue(key, out var scannedAddress) || scannedAddress == 0)
            {
                if (requireAllHooks)
                {
                    throw new AgentCompatibilityException(
                        $"签名兼容校验未通过：Hook {key} 未能唯一定位，未安装任何 Patch。");
                }

                continue;
            }

            if (scannedAddress < actualModuleBase || scannedAddress - actualModuleBase >= MaximumModuleSpan)
            {
                throw new AgentCompatibilityException(
                    $"签名兼容校验未通过：Hook {key} 的扫描地址超出目标模块，未安装任何 Patch。");
            }

            var expectedLiveAddress = checked(actualModuleBase + (uint)expectedRva);
            if (!requireAllHooks && scannedAddress == expectedLiveAddress)
            {
                continue;
            }

            var read = await _client.ReadMemoryAsync(
                    processId,
                    new AgentMemoryReadRequest(scannedAddress, checked((uint)plan.OriginalBytes.Length)),
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read.StatusCode != AgentStatusCode.Ok ||
                read.AgentVersion != AgentProtocol.Version ||
                read.Address != scannedAddress ||
                read.Bytes.Length != plan.OriginalBytes.Length)
            {
                throw new AgentCompatibilityException(
                    $"签名地址校验未通过：无法读取 Hook {key} 的完整原始指令，未安装任何 Patch。");
            }

            var verification = SignatureCompatibilityVerifier.Verify(
                plan.OriginalBytes,
                checked((ulong)(profile.ModuleBaseVa + expectedRva)),
                read.Bytes,
                scannedAddress,
                checked((ulong)profile.ModuleBaseVa),
                actualModuleBase);
            if (!verification.Compatible)
            {
                throw new AgentCompatibilityException(
                    $"签名地址校验未通过：Hook {key} 的代码布局已变化（{verification.Reason}），未安装任何 Patch。");
            }

            attestedBytes[key] = read.Bytes.ToArray();
        }

        return attestedBytes;
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
    /// The last mismatch diagnostic fetched from the agent after a <c>PatchMismatch</c>
    /// install result. Populated by <see cref="InstallPatchesAsync"/> when the agent
    /// reports a non-Ok status; null when no mismatch has been captured yet.
    /// </summary>
    public TrainerMismatchDiagnostic? LastMismatchDiagnostic { get; private set; }

    /// <summary>
    /// Pulls the last mismatch diagnostic captured by the DLL. Call this right after a failed
    /// <see cref="InstallPatchesAsync"/> that threw with <see cref="AgentStatusCode.PatchMismatch"/>;
    /// the DLL retains the offending hook's expected/actual/dump bytes until the next install.
    /// This method also populates <see cref="LastMismatchDiagnostic"/> on success.
    /// </summary>
    public async Task<TrainerMismatchDiagnostic?> GetMismatchDiagnosticsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (TargetProcessId is not int processId)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }

        try
        {
            var payload = await _client
                .GetMismatchDiagnosticsAsync(processId, timeout, cancellationToken)
                .ConfigureAwait(false);

            if (payload.HasMismatch)
            {
                var diag = TrainerMismatchDiagnostic.FromPayload(payload);
                LastMismatchDiagnostic = diag;
                return diag;
            }

            LastMismatchDiagnostic = null;
            return null;
        }
        catch
        {
            LastMismatchDiagnostic = null;
            return null;
        }
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
