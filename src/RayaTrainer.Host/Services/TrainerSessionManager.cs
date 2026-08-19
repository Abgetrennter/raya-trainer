using System.Linq;
using System.IO;
using RayaTrainer.Core.Agent;
using RayaTrainer.Host.Services;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Errors;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Host.Services;

public sealed partial class TrainerSessionManager : ITrainerSessionService, ITrainerDiagnosticsSource, IProductControlSessionHost, IReinforcementProjectionPublisher, ISecretProtocolProjectionPublisher, IDisposable
{
    private readonly Func<InjectedAgentBackend> _agentBackendFactory;
    private readonly Func<string> _agentDllPathProvider;
    private InjectedAgentBackend? _agentBackend;
    private TrainerTarget? _agentTarget;
    private int? _targetProcessId;
    private bool _arePatchesInstalled;
    private ITrainerFeatureController? _featureController;
    // One-shot gate: the versioned AttributeModifier bundle is loaded once per Agent attach when the
    // Agent advertises the RuntimeAssetInjection capability (0x800). Reset on detach.
    private bool _runtimeAssetLoadRequested;
    // One-shot gate: surface the Agent's asset.* diagnostics lines once after the load is requested.
    private bool _runtimeAssetDiagnosticsCaptured;
    private readonly Dictionary<string, string> _unavailableFeatureReasons = new(StringComparer.Ordinal);
    private readonly ForegroundWindowProcess _foregroundWindowProcess = new();
    private readonly TrainerDiagnosticState _diagnosticState = new();
    private TrainerManifest? _manifest;
    private TrainerTarget? _currentTarget;
    private CancellationTokenSource? _overlayMonitorCancellation;
    // Desired in-game overlay visibility. Authoritative source for the App F10 hotkey
    // toggle so we never round-trip a read before flipping. Set true when the overlay is
    // enabled, false when it is stopped.
    private bool _overlayVisible;
    // Product Control Plane (I4). The manager owns the session the same way it owns
    // _agentBackend; the durable policy provider mirrors the F6 settings source without
    // coupling the manager to the settings layer (defaults to Empty for constructors that do
    // not supply one).
    private readonly IProductControlSession _productControlSession;
    private readonly Func<DurableProductPolicy> _durablePolicyProvider;
    // Reinforcement Preset Console (R3). Owned like _productControlSession; MainViewModel
    // publishes preset snapshots through IReinforcementProjectionPublisher and the
    // coordinator replaces the Agent-held read-only projection over command 62.
    private readonly ReinforcementPresetProjectionCoordinator _reinforcementProjection;
    // Secret Protocol Preset Console (P3). Independent second coordinator instance with
    // its own session id; MainViewModel publishes snapshots through
    // ISecretProtocolProjectionPublisher and the coordinator replaces the Agent-held
    // read-only projection over command 64.
    private readonly SecretProtocolPresetProjectionCoordinator _secretProtocolProjection;

    public TrainerSessionManager()
        : this(() => new InjectedAgentBackend(), ResolveDefaultAgentDllPath)
    {
    }

    internal TrainerSessionManager(Func<DurableProductPolicy> durablePolicyProvider)
        : this(
            () => new InjectedAgentBackend(),
            ResolveDefaultAgentDllPath,
            durablePolicyProvider: durablePolicyProvider)
    {
    }

    public TrainerSessionManager(
        Func<InjectedAgentBackend> agentBackendFactory,
        Func<string> agentDllPathProvider,
        Func<IProductControlClient>? productControlClientFactory = null,
        Func<DurableProductPolicy>? durablePolicyProvider = null,
        IReinforcementPresetConsoleClient? reinforcementConsoleClient = null,
        ISecretProtocolPresetConsoleClient? secretProtocolConsoleClient = null)
    {
        _agentBackendFactory = agentBackendFactory;
        _agentDllPathProvider = agentDllPathProvider;
        _capabilityPolicy = new TrainerFeatureCapabilityPolicy();
        _productControlSession = new ProductControlSession(
            productControlClientFactory ?? (() => new AgentNamedPipeClient()));
        _durablePolicyProvider = durablePolicyProvider ?? (() => DurableProductPolicy.Empty);
        _reinforcementProjection = new ReinforcementPresetProjectionCoordinator(
            reinforcementConsoleClient,
            reportSyncStatus: message =>
            {
                if (message is null)
                {
                    _diagnosticState.RecordEvent(
                        DiagnosticEventSeverity.Info,
                        DiagnosticEventCodes.ReinforcementProjectionSynced,
                        "增援预设已同步到游戏内控制台。");
                }
                else
                {
                    _diagnosticState.RecordEvent(
                        DiagnosticEventSeverity.Warning,
                        DiagnosticEventCodes.ReinforcementProjectionSyncFailed,
                        message);
                }
            });
        _secretProtocolProjection = new SecretProtocolPresetProjectionCoordinator(
            secretProtocolConsoleClient,
            reportSyncStatus: message =>
            {
                if (message is null)
                {
                    _diagnosticState.RecordEvent(
                        DiagnosticEventSeverity.Info,
                        DiagnosticEventCodes.SecretProtocolProjectionSynced,
                        "秘密协议预设已同步到游戏内控制台。");
                }
                else
                {
                    _diagnosticState.RecordEvent(
                        DiagnosticEventSeverity.Warning,
                        DiagnosticEventCodes.SecretProtocolProjectionSyncFailed,
                        message);
                }
            });
        _diagnosticState.OnChanged = () => DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Product Control Plane exposure seam (U2/U4). Explicit implementation keeps the internal
    // session type off the public ITrainerSessionService surface (avoids CS0059). The session is
    // handed out whenever a target is attached; its Negotiation reports the live
    // capability/identity/schema state (including still-negotiating) so consumers never need a
    // separate readiness signal. Reset() clears it back to Offline on detach.
    IProductControlSession? IProductControlSessionHost.ProductControl =>
        _agentBackend is not null ? _productControlSession : null;

    // Reinforcement Preset Console (R3): WPF-saved preset snapshots flow through here to
    // the coordinator; the coordinator syncs whenever the Agent is (or becomes) ready.
    void IReinforcementProjectionPublisher.PublishReinforcementPresets(
        IReadOnlyList<ReinforcementPreset> presets) =>
        _reinforcementProjection.UpdatePresets(presets);

    // Secret Protocol Preset Console (P3): same flow through the second coordinator.
    void ISecretProtocolProjectionPublisher.PublishSecretProtocolPresets(
        IReadOnlyList<SecretProtocolQueuePreset> presets) =>
        _secretProtocolProjection.UpdatePresets(presets);

    public event EventHandler? DiagnosticsChanged;

    public IReadOnlyList<TrainerDiagnosticEvent> DiagnosticEvents => _diagnosticState.Events;

    public ITrainerFeatureController? FeatureController => _featureController;

    public bool ArePatchesInstalled => _arePatchesInstalled;

    public int? TargetProcessId => _targetProcessId;

    public bool CanUseFeatures => _agentBackend?.IsConnected == true;

    public int InstalledHookCount => _diagnosticState.InstalledHookCount;

    public string RemoteSymbolSummary =>
        _diagnosticState.AgentStatus is null
            ? "Native runtime 未连接。"
            : $"DLL Agent v{_diagnosticState.AgentStatus.Value.AgentVersion}: native capabilities=0x{_diagnosticState.AgentStatus.Value.NativeRuntimeCapabilities:X8}";

    public AttachResult AttachTarget(TrainerManifest manifest, TrainerTarget target)
    {
        _manifest = manifest;
        _currentTarget = target;
        _diagnosticState.ResetForAttach();
        _productControlSession.Reset();
        _unavailableFeatureReasons.Clear();
        RecordDiagnosticEvent(
            DiagnosticEventSeverity.Info,
            DiagnosticEventCodes.AttachStarted,
            $"开始连接 {target.VersionProfileId ?? target.FileVersion}（DLL Agent）。");

        if (target.ProcessId is null)
        {
            _diagnosticState.RecordFailure(
                DiagnosticEventCodes.AttachFailed,
                "无法确定目标进程 PID。",
                domain: ErrorDomain.Env,
                retryHint: RetryHint.UserAction,
                stage: TrainerErrorVocabulary.StageTarget);
            throw new InvalidOperationException("无法确定目标进程 PID。");
        }

        var profile = Ra3VersionProfileRegistry.ResolveTargetProfile(target);
        if (profile?.SupportsAgentBackend != true)
        {
            ClearAttachState();
            var result = new AttachResult(
                false,
                profile is null
                    ? "无法确认目标版本配置，当前不会注入 DLL Agent。"
                    : $"已识别 {profile.DisplayName}，但该版本尚未完成 DLL Agent 地址验证，当前不会注入 Agent。");
            _diagnosticState.RecordFailure(
                DiagnosticEventCodes.AttachProfileUnsupported,
                result.Message,
                domain: ErrorDomain.Contract,
                retryHint: RetryHint.NotRetryable,
                stage: "profile");
            return result;
        }

        if (!target.VersionSupported)
        {
            ClearAttachState();
            var result = new AttachResult(false, $"版本不支持；DLL Agent 可安装版本：{FormatInstallableProfiles()}。");
            _diagnosticState.RecordFailure(
                DiagnosticEventCodes.AttachVersionUnsupported,
                result.Message,
                domain: ErrorDomain.Contract,
                retryHint: RetryHint.NotRetryable,
                stage: "profile");
            return result;
        }

        _targetProcessId = target.ProcessId;
        _agentTarget = target;
        _agentBackend = _agentBackendFactory();
        try
        {
            _diagnosticState.SetAgentStatus(_agentBackend
                .AttachAsync(target, manifest, _agentDllPathProvider(), TimeSpan.FromSeconds(30))
                .GetAwaiter()
                .GetResult());

            // Native catalog identity and addresses are resolved once inside the Agent. Reconnect
            // only observes that runtime and never replaces its snapshot with App profile RVAs.
        }
        catch (Exception ex)
        {
            _diagnosticState.CaptureAgent(_agentBackend);
            _agentBackend = null;
            _agentTarget = null;
            _diagnosticState.SetAgentStatus(null);
            _targetProcessId = null;
            _featureController = null;
            _arePatchesInstalled = false;

            _diagnosticState.RecordFailure(
                DiagnosticEventCodes.AgentAttachFailed,
                ex.Message,
                domain: ex is TrainerException trainerFailure ? trainerFailure.Domain : ErrorDomain.Env,
                retryHint: ex is TrainerException trainerHint ? trainerHint.RetryHint : RetryHint.UserAction,
                stage: TrainerErrorVocabulary.StageAgent);
            if (ex is AgentCompatibilityException)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }

            throw new InvalidOperationException($"DLL Agent 注入失败：{ex.Message}", ex);
        }

        _diagnosticState.CaptureAgent(_agentBackend);
        ApplyProfileFeatureAvailability(manifest, profile);
        var displayName = profile.DisplayName;
        // Agent-owned runtime (plan §6): the Agent self-installs its core hooks at init, so any
        // successful attach — fresh injection or takeover — observes an already-installed runtime.
        // The App adopts that install and never sends its own InstallPatches.
        var agentInstalledHooks = _diagnosticState.AgentStatus is { InstalledHookCount: > 0 };
        if (agentInstalledHooks)
        {
            _featureController = _agentBackend.CreateFeatureController(_diagnosticState.AgentStatus!.Value);
            _arePatchesInstalled = true;
            var effectiveHookCount = checked((int)_diagnosticState.AgentStatus!.Value.InstalledHookCount);
            _diagnosticState.SetPatchInstallResult(new PatchInstallResult(
                effectiveHookCount,
                checked((int)_diagnosticState.AgentStatus!.Value.InstalledHookCount),
                []));
            _diagnosticState.CaptureRuntimeState(_featureController, _arePatchesInstalled);
            TryEnableOverlay();
            // Plan Phase 1: when the Agent advertises the additive RuntimeAssetInjection capability
            // (0x800), release the versioned AttributeModifier bundle and request the load once. The
            // actual load + self-check runs on the next game-thread tick; the bundle stays registered
            // for the process lifetime. Best-effort: a failure is recorded as a diagnostic event and
            // does not abort the session (the Agent fail-closes dependent products to Unavailable).
            TryRequestRuntimeAssetLoad();
        }
        else if (!ShouldEnableOverlay(target))
        {
            _diagnosticState.SetOverlayNotApplicable();
        }

        var attachResult = agentInstalledHooks
            ? new AttachResult(
                true,
                $"已连接 {displayName}，DLL Agent 已自安装 {_diagnosticState.AgentStatus!.Value.InstalledHookCount} 个 Hook。")
            : new AttachResult(true, $"已连接 {displayName}（DLL Agent 尚未报告已安装 Hook）。");
        _diagnosticState.RecordEvent(
            DiagnosticEventSeverity.Info,
            DiagnosticEventCodes.AgentAttached,
            attachResult.Message);
        StartProductControlNegotiation(target.ProcessId.Value);
        // Reinforcement Preset Console (R3): a (re)attached Agent always receives a fresh
        // projection under this App run's session id, atomically superseding any state an
        // in-process older session left behind.
        _reinforcementProjection.OnAgentReady(target.ProcessId.Value);
        _secretProtocolProjection.OnAgentReady(target.ProcessId.Value);
        return attachResult;
    }

    private void ApplyProfileFeatureAvailability(TrainerManifest manifest, Ra3VersionProfile? profile)
    {
        var profileId = profile?.Id ?? string.Empty;
        foreach (var feature in TrainerFeatureCatalog.CreateGridFeatures(manifest.Features)
                     .Where(feature => !feature.SupportsProfile(profileId)))
        {
            _unavailableFeatureReasons[feature.RawName] = profile is null
                ? "不可用：该功能仅支持已验证的特定游戏版本。"
                : $"不可用：该功能不支持 {profile.DisplayName}。";
        }
    }

    private void ClearAttachState()
    {
        _agentBackend = null;
        _agentTarget = null;
        _diagnosticState.SetAgentStatus(null);
        _targetProcessId = null;
        _featureController = null;
        _arePatchesInstalled = false;
        _runtimeAssetLoadRequested = false;
        _runtimeAssetDiagnosticsCaptured = false;
    }

    // Kicks off the Product Control Plane negotiation + durable policy import as a
    // fire-and-forget task, mirroring the overlay-monitor pattern: the pipe round-trips must
    // never block the attach path (UI thread). The task guards every continuation with
    // ReferenceEquals(_agentBackend, backend) so a rapid detach/re-attach cannot publish stale
    // diagnostics onto a different target.
    private void StartProductControlNegotiation(int processId)
    {
        var backend = _agentBackend;
        if (backend is null || _diagnosticState.AgentStatus is not AgentStatusPayload status)
        {
            return;
        }

        _ = NegotiateProductControlAsync(backend, processId, status);
    }

    // Recovery path for the one-shot negotiation: when the session is attached but the plane
    // never settled (transient pipe failure right after attach, game thread not yet pumping),
    // a user-triggered detect re-kicks the negotiation instead of requiring a full re-attach.
    // Ok needs no retry; CapabilityUnavailable is a deterministic capability-gate refusal that a
    // retry cannot flip. Every other state (Offline handshake gap, pipe Faulted, stale import)
    // is worth one manual re-kick while the session is attached.
    public void EnsureProductControlNegotiationStarted()
    {
        var status = _productControlSession.Negotiation.Status;
        if (status is ProductControlStatus.Ok or ProductControlStatus.CapabilityUnavailable)
        {
            return;
        }

        if (_agentBackend?.IsConnected != true || _targetProcessId is not int processId)
        {
            return;
        }

        _diagnosticState.RecordEvent(
            DiagnosticEventSeverity.Info,
            DiagnosticEventCodes.ProductControlRenegotiate,
            "产品控制协商未完成，正在重新协商。");
        StartProductControlNegotiation(processId);
    }

    private async Task NegotiateProductControlAsync(
        InjectedAgentBackend backend,
        int processId,
        AgentStatusPayload status)
    {
        var timeout = TimeSpan.FromSeconds(3);
        try
        {
            await _productControlSession
                .NegotiateAndImportPolicyAsync(processId, status, _durablePolicyProvider(), timeout)
                .ConfigureAwait(false);
            if (!ReferenceEquals(_agentBackend, backend))
            {
                return;
            }

            var diagnostics = await ProductControlDiagnosticsCollector
                .CollectAsync(_productControlSession, timeout)
                .ConfigureAwait(false);
            if (!ReferenceEquals(_agentBackend, backend))
            {
                return;
            }

            _diagnosticState.CaptureProductControlDiagnostics(diagnostics);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_agentBackend, backend))
            {
                _diagnosticState.RecordEvent(
                    DiagnosticEventSeverity.Warning,
                    DiagnosticEventCodes.ProductControlNegotiateFailed,
                    "产品控制协商失败，相关功能暂不可用。",
                    ex.Message);
            }
        }
    }

    private InjectedAgentBackend RequireAgentBackend() =>
        _agentBackend?.IsConnected == true
            ? _agentBackend
            : throw new InvalidOperationException("请先连接游戏中的 Agent。");

    public SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir)
    {
        _diagnosticState.RecordEvent(DiagnosticEventSeverity.Info, DiagnosticEventCodes.PatchInstallStarted, "确认 DLL Agent 已安装 Patch。", "DLL Agent");
        if (_agentBackend is null || _agentTarget is null)
        {
            throw new InvalidOperationException("请先检测进程。");
        }

        // Agent-owned runtime (plan §6): the Agent self-installs its core hooks at init, so the host
        // no longer builds an address payload or sends InstallPatches. It observes the Agent's reported
        // install count (captured at attach) and adopts control of that runtime.
        var installedHookCount = _diagnosticState.AgentStatus?.InstalledHookCount ?? 0u;
        if (_diagnosticState.AgentStatus is not null)
        {
            _featureController = _agentBackend.CreateFeatureController(_diagnosticState.AgentStatus.Value);
        }
        _arePatchesInstalled = installedHookCount > 0;
        var agentInstallResult = new PatchMismatchReportResult(
            new PatchInstallResult(
                checked((int)installedHookCount),
                checked((int)installedHookCount),
                []),
            ReportPath: null);
        _diagnosticState.CapturePatchResult(agentInstallResult);
        if (_featureController is null)
        {
            MarkFeaturesUnavailable([], null, TrainerFeatureCatalog.CreateGridFeatures(manifest.Features));
        }
        _diagnosticState.CaptureRuntimeState(_featureController, _arePatchesInstalled);
        TryEnableOverlay();
        return new SessionInstallOutcome(agentInstallResult, CreatePatchInstalledStatus(agentInstallResult));
    }

    public void ResetPatchesState()
    {
        ClearSessionState(restoreAgentPatches: true);
    }

    public void MarkTargetOffline()
    {
        ClearSessionState(restoreAgentPatches: false);
    }

    private void ClearSessionState(bool restoreAgentPatches)
    {
        var hadTarget = _currentTarget is not null;
        CancelOverlayMonitor();
        if (restoreAgentPatches && _agentBackend is not null && _arePatchesInstalled)
        {
            TryStopOverlay();
            try
            {
                _agentBackend.RestorePatchesAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Cleanup is best-effort; target process may have already exited.
                _diagnosticState.RecordEvent(
                    DiagnosticEventSeverity.Warning,
                    DiagnosticEventCodes.PatchRestoreWarning,
                    "Patch 恢复未能确认完成。",
                    ex.Message);
            }
        }

        _agentBackend = null;
        _agentTarget = null;
        _diagnosticState.SetAgentStatus(null);
        _targetProcessId = null;
        _currentTarget = null;
        _featureController = null;
        _arePatchesInstalled = false;
        _productControlSession.Reset();
        _reinforcementProjection.OnAgentDetached();
        _secretProtocolProjection.OnAgentDetached();
        _diagnosticState.ClearDiagnosticState();
        _unavailableFeatureReasons.Clear();
        if (hadTarget)
        {
            _diagnosticState.RecordEvent(DiagnosticEventSeverity.Info, DiagnosticEventCodes.SessionReset, "会话已结束，运行时状态已清理。");
        }
        else
        {
            DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        // Plan §497: the App's normal exit never restores the Agent-owned runtime; the Agent keeps
        // its self-installed core hooks until it is ejected. Only the explicit "恢复 Patch" action
        // (ResetPatchesState) asks the Agent to restore.
        ClearSessionState(restoreAgentPatches: false);
    }

    public bool IsTargetGameForeground()
    {
        return _targetProcessId is int targetProcessId &&
            _foregroundWindowProcess.GetForegroundProcessId() == targetProcessId;
    }

    private readonly TrainerFeatureCapabilityPolicy _capabilityPolicy;

    public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        // Phase 1.2 Optimization: Single-call static Compute() replaces 4-layer indirect flow.
        // Removes BuildCapabilityContext(), ApplyEvaluation(), SessionManagerCapabilityContext wrapper.
        var profile = _currentTarget is null ? null : Ra3VersionProfileRegistry.ResolveTargetProfile(_currentTarget);
        return FeatureCapabilitySnapshot.Compute(
            feature,
            hasTarget: _currentTarget is not null || (_arePatchesInstalled && _featureController is not null),
            sessionReady: CanUseFeatures || _featureController is not null,
            patchesInstalled: _arePatchesInstalled,
            backendSupportsDirectGameApi: _currentTarget is null || profile?.SupportsDirectGameApi == true,
            directGameApiReady: _featureController is IAgentFeatureController { SupportsDirectGameApi: true },
            unavailableReason: _unavailableFeatureReasons.GetValueOrDefault(feature.RawName),
            currentProfile: profile,
            installedHooks: _diagnosticState.AgentStatus is { InstalledHookCount: > 0 }
                ? TrainerFeatureBehaviorCatalog.DeclaredHookIds
                : Array.Empty<uint>(),
            patchSets: _diagnosticState.AgentStatus is { InstalledHookCount: > 0 } && HasProvenPatchSetLayout(profile)
                ? TrainerFeatureBehaviorCatalog.DeclaredPatchSetIds
                : Array.Empty<uint>());
    }

    // Profiles whose Agent build registers the FrameRateUnlock patch set. Mirrors the Hook 41 RVA
    // anchor table in the Agent's FrameRateUnlockPatchSet: ra3_1.12 (TW + Steam layouts) and
    // ra3_1.13. Any other build leaves the patch set unregistered agent-side (fail-closed).
    private static bool HasProvenPatchSetLayout(Ra3VersionProfile? profile) =>
        profile is not null &&
        (string.Equals(profile.Id, "ra3_1.12", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(profile.Id, "ra3_1.13", StringComparison.OrdinalIgnoreCase));

    private string CreatePatchInstalledStatus(PatchMismatchReportResult result)
    {
        if (result.SkippedHooks.Count == 0)
        {
            return $"DLL Agent Patch 已安装，Hook={result.InstallResult.InstalledHookCount}；{RemoteSymbolSummary}";
        }

        var disabledCount = _unavailableFeatureReasons.Count;
        var message = $"Patch 已部分安装；{result.SkippedHooks.Count} 个 hook 因版本未验证或字节不匹配已跳过，{disabledCount} 个相关功能已禁用。";
        return string.IsNullOrWhiteSpace(result.ReportPath)
            ? message
            : $"{message} 诊断日志：{result.ReportPath}";
    }

    private void MarkFeaturesUnavailable(
        IReadOnlyCollection<string> enableFlags,
        string? reportPath,
        IEnumerable<TrainerFeature> features)
    {
        var disabledFlags = new HashSet<string>(enableFlags, StringComparer.OrdinalIgnoreCase);
        var disablesAllFeatures = disabledFlags.Count == 0;
        var reason = disablesAllFeatures
            ? "不可用：基础 Patch 点未通过版本或字节验证且无法映射到单个功能，已禁用全部功能。可能原因：当前 profile 未验证、该位置已经被 patch 过、游戏版本不一致，或者 MOD 加载时修改了代码段。"
            : "不可用：相关 Patch 点未通过版本或字节验证，hook 已安全跳过。可能原因：当前 profile 未验证、该位置已经被 patch 过、游戏版本不一致，或者 MOD 加载时修改了代码段。";
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            reason += $" 诊断日志：{reportPath}";
        }

        foreach (var feature in features)
        {
            if (disablesAllFeatures || feature.EnableFlags.Any(disabledFlags.Contains))
            {
                _unavailableFeatureReasons[feature.RawName] = reason;
            }
        }

        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    public TrainerDiagnosticSnapshot GetDiagnosticSnapshot(
        IReadOnlyList<TrainerFeature> features,
        int maxEvents = TrainerDiagnosticEventBuffer.Capacity)
    {
        ArgumentNullException.ThrowIfNull(features);
        var capabilities = features
            .DistinctBy(feature => feature.RawName, StringComparer.Ordinal)
            .Select(GetFeatureCapability)
            .OrderBy(capability => capability.GroupName, StringComparer.Ordinal)
            .ThenBy(capability => capability.DisplayName, StringComparer.Ordinal)
            .ToArray();
        return _diagnosticState.GetSnapshot(
            features,
            _currentTarget,
            _arePatchesInstalled,
            _manifest,
            capabilities,
            _agentBackend?.IsConnected == true,
            maxEvents);
    }

    public async Task<TrainerDiagnosticSnapshot> RefreshDiagnosticsAsync(
        IReadOnlyList<TrainerFeature> features,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_agentBackend?.IsConnected == true)
            {
                _diagnosticState.SetAgentStatus(await _agentBackend
                    .GetStatusAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false));

                // Phase 1 visibility: once the asset load has been requested, pull the Agent's
                // runtime diagnostics once and surface the asset.* lines as events so the load result
                // (Ready/Failed + resolved template count) is observable in the event stream. One-shot
                // per attach (gated alongside the load request) to avoid spamming on every refresh.
                if (_runtimeAssetLoadRequested && !_runtimeAssetDiagnosticsCaptured)
                {
                    _runtimeAssetDiagnosticsCaptured = true;
                    await TryCaptureAssetRuntimeDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
                }

                if (_productControlSession.Negotiation.Status != ProductControlStatus.TargetOffline)
                {
                    _diagnosticState.CaptureProductControlDiagnostics(
                        await ProductControlDiagnosticsCollector
                            .CollectAsync(
                                _productControlSession,
                                TimeSpan.FromSeconds(2),
                                cancellationToken)
                            .ConfigureAwait(false));
                }
            }

            if (_arePatchesInstalled && _featureController is not null)
            {
                await Task.Run(() => _diagnosticState.CaptureRuntimeState(_featureController, _arePatchesInstalled), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _diagnosticState.ClearRuntimeReadState();
            }

            if (_arePatchesInstalled && _agentBackend is not null && ShouldEnableOverlay(_agentTarget))
            {
                try
                {
                    _diagnosticState.CaptureOverlayStatus(await _agentBackend
                        .GetOverlayStatusAsync(TimeSpan.FromSeconds(2), cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    _diagnosticState.CaptureOverlayFailure($"游戏内面板状态读取失败：{ex.Message}");
                }
            }

            // Low-frequency session-cache refresh of the Agent-reported reinforcement
            // selection name (R3): rides the existing diagnostics cadence, never polls.
            await _reinforcementProjection
                .RefreshCachedSelectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await _secretProtocolProjection
                .RefreshCachedSelectionAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnosticState.RecordEvent(
                DiagnosticEventSeverity.Warning,
                DiagnosticEventCodes.RuntimeRefreshFailed,
                "运行时诊断刷新失败。",
                ex.Message);
        }

        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        return GetDiagnosticSnapshot(features);
    }

    // Plan Phase 1: release the versioned AttributeModifier bundle to a stable directory and ask the
    // Agent to load it on the game thread. Idempotent per attach (gated by _runtimeAssetLoadRequested,
    // reset on detach). Only fires when the Agent advertises RuntimeAssetInjection (0x800). Best-effort:
    // any failure becomes a diagnostic event; the session continues and the Agent fail-closes
    // dependent products to Unavailable.
    private void TryRequestRuntimeAssetLoad()
    {
        if (_runtimeAssetLoadRequested)
        {
            _diagnosticState.RecordEvent(DiagnosticEventSeverity.Info, "runtime.asset_load_skip",
                "资产包加载已请求过，跳过。");
            return;
        }
        if (_agentBackend is null || _targetProcessId is null)
        {
            _diagnosticState.RecordEvent(DiagnosticEventSeverity.Warning, "runtime.asset_load_skip",
                "Agent 后端或目标进程为空，无法请求加载。");
            return;
        }

        _runtimeAssetLoadRequested = true;
        try
        {
            // Select the bundle format variant by engine family: Uprising uses Game 7 stream headers
            // (SDK-U HeaderFixer conversion); RA3 1.12/1.13 use the original Game 6 SDK-X output.
            var profile = _currentTarget is null ? null : Ra3VersionProfileRegistry.ResolveTargetProfile(_currentTarget);
            var isUprising = profile?.Id.StartsWith("ra3_uprising", StringComparison.OrdinalIgnoreCase) == true;

            var manifestPath = RayaTrainer.Core.RuntimeAssets.RuntimeAssetPathProvider.EnsureBundleReleased(uprising: isUprising);

            _diagnosticState.RecordEvent(
                DiagnosticEventSeverity.Info,
                DiagnosticEventCodes.RuntimeAssetLoadReleasing,
                $"正在释放{(isUprising ? "Uprising (Game 7)" : "RA3 (Game 6)")}资产包到磁盘...",
                $"manifest={manifestPath}; profile={profile?.Id ?? "unknown"}");

            var result = _agentBackend
                .LoadBundledRuntimeAssetsAsync(_targetProcessId.Value, manifestPath, TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();

            _diagnosticState.RecordEvent(
                DiagnosticEventSeverity.Info,
                DiagnosticEventCodes.RuntimeAssetLoadRequested,
                $"已请求加载 AttributeModifier 资产包：status={result.StatusCode} state={result.State} resolved={result.ResolvedTemplateCount}/{result.ExpectedTemplateCount}",
                $"manifest={manifestPath}");
        }
        catch (Exception ex)
        {
            _diagnosticState.RecordEvent(
                DiagnosticEventSeverity.Warning,
                DiagnosticEventCodes.RuntimeAssetLoadFailed,
                $"AttributeModifier 资产包加载请求失败：{ex.GetType().Name}: {ex.Message}",
                ex.StackTrace);
        }
    }

    // Phase 1 visibility: pull the Agent's runtime diagnostics and surface asset.* lines as events.
    // The load runs on the game thread one tick after the request, so this is polled on the first
    // diagnostics refresh after the request. Surfaces Ready/Failed + resolved count.
    private async Task TryCaptureAssetRuntimeDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_agentBackend is null)
        {
            return;
        }

        var payload = await _agentBackend
            .TryGetRuntimeDiagnosticsAsync(TimeSpan.FromSeconds(2), cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return;
        }

        foreach (var line in payload.Value.Lines)
        {
            // Surface asset.*, capability.*, scriptruntime.* and matchcontext.* lines so both the
            // load result, any capability unavailability reason (e.g. a symbol that did not resolve
            // on the live binary), the ScriptRuntime discovery/observation stage, and the live
            // MatchContext lifecycle are all visible for diagnosis.
            if (!line.StartsWith("asset.", StringComparison.Ordinal) &&
                !line.StartsWith("capability.", StringComparison.Ordinal) &&
                !line.StartsWith("scriptruntime.", StringComparison.Ordinal) &&
                !line.StartsWith("script.", StringComparison.Ordinal) &&
                !line.StartsWith("matchcontext.", StringComparison.Ordinal))
            {
                continue;
            }
            // asset.runtime.state=Ready, asset.templates.resolved=12/12, asset.failure.reason=...
            var severity = line.Contains("state=Ready", StringComparison.Ordinal) ||
                           line.StartsWith("capability.", StringComparison.Ordinal) == false && line.Contains("Ready", StringComparison.Ordinal)
                ? DiagnosticEventSeverity.Info
                : DiagnosticEventSeverity.Warning;
            // Treat capability.* unavailable lines as info too (they are normal on unproven profiles).
            if (line.StartsWith("capability.", StringComparison.Ordinal))
            {
                severity = DiagnosticEventSeverity.Info;
            }
            // ScriptRuntime / MatchContext diagnostics are informational probes, not warnings.
            string category = DiagnosticEventCodes.RuntimeAssetDiagnostics;
            string label = "AttributeModifier 资产运行时自检";
            if (line.StartsWith("scriptruntime.", StringComparison.Ordinal) ||
                line.StartsWith("script.", StringComparison.Ordinal))
            {
                severity = DiagnosticEventSeverity.Info;
                category = DiagnosticEventCodes.RuntimeScriptDiagnostics;
                label = "脚本运行时自检";
            }
            else if (line.StartsWith("matchcontext.", StringComparison.Ordinal))
            {
                severity = DiagnosticEventSeverity.Info;
                category = DiagnosticEventCodes.RuntimeMatchContextDiagnostics;
                label = "对局上下文自检";
            }
            // Put the raw line in the message itself (not just the tooltip) so the result is visible
            // at a glance: "AttributeModifier 资产运行时自检: asset.runtime.state=Ready".
            _diagnosticState.RecordEvent(
                severity,
                category,
                $"{label}：{line}",
                line);
        }
    }

    private void TryEnableOverlay()
    {
        if (_agentBackend is null || !ShouldEnableOverlay(_agentTarget))
        {
            _diagnosticState.SetOverlayNotApplicable();
            return;
        }

        try
        {
            var result = _agentBackend
                .SetOverlayStateAsync(true, true, TimeSpan.FromSeconds(2))
                .GetAwaiter()
                .GetResult();
            if (result.StatusCode != AgentStatusCode.Ok)
            {
                _diagnosticState.CaptureOverlayFailure($"游戏内面板启用失败：{result.StatusCode}。");
                return;
            }

            _overlayVisible = true;

            var status = _agentBackend
                .GetOverlayStatusAsync(TimeSpan.FromSeconds(1))
                .GetAwaiter()
                .GetResult();
            _diagnosticState.CaptureOverlayStatus(status);
            if (status.IsReady)
            {
                RecordOverlayReady();
                return;
            }
            if (status.Lifecycle == AgentOverlayLifecycle.Failed)
            {
                _diagnosticState.CaptureOverlayFailure(
                    $"游戏内面板初始化失败：{status.LastError}。其他修改器功能仍可使用。",
                    status);
                return;
            }

            StartOverlayMonitor(_agentBackend);
        }
        catch (Exception ex)
        {
            _diagnosticState.CaptureOverlayFailure(
                $"游戏内面板启用失败：{ex.Message}。其他修改器功能仍可使用。");
        }
    }

    private void TryStopOverlay()
    {
        if (_agentBackend is null || !ShouldEnableOverlay(_agentTarget))
        {
            return;
        }

        CancelOverlayMonitor();
        _overlayVisible = false;
        try
        {
            var result = _agentBackend
                .SetOverlayStateAsync(false, false, TimeSpan.FromSeconds(3))
                .GetAwaiter()
                .GetResult();
            var status = _agentBackend
                .GetOverlayStatusAsync(TimeSpan.FromSeconds(1))
                .GetAwaiter()
                .GetResult();
            _diagnosticState.CaptureOverlayStatus(status);
            if (result.StatusCode != AgentStatusCode.Ok)
            {
                _diagnosticState.CaptureOverlayFailure(
                    $"游戏内面板停止未能确认完成：{result.StatusCode}。将继续恢复游戏 Patch。",
                    status);
            }
        }
        catch (Exception ex)
        {
            _diagnosticState.CaptureOverlayFailure(
                $"游戏内面板停止未能确认完成：{ex.Message}。将继续恢复游戏 Patch。");
        }
    }

    // True when a connected RA3 1.12 session with patches installed can accept an overlay
    // visibility toggle. Gates the App F10 hotkey so the key is only consumed while the
    // overlay is actually running.
    public bool CanToggleOverlay =>
        _agentBackend is not null && _arePatchesInstalled && ShouldEnableOverlay(_agentTarget);

    // Flips the in-game overlay visibility via the pipe. Called from the App hotkey path
    // (low-level keyboard hook), which works under RA3's in-match DirectInput. Fire-and-
    // forget so the UI thread never blocks on the pipe round-trip.
    public void ToggleOverlayVisibility()
    {
        var backend = _agentBackend;
        if (backend is null || !_arePatchesInstalled || !ShouldEnableOverlay(_agentTarget))
        {
            return;
        }

        var next = !_overlayVisible;
        _overlayVisible = next;
        _ = ApplyOverlayVisibilityAsync(backend, next);
    }

    private async Task ApplyOverlayVisibilityAsync(InjectedAgentBackend backend, bool visible)
    {
        try
        {
            var result = await backend
                .SetOverlayStateAsync(true, visible, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
            if (!ReferenceEquals(_agentBackend, backend))
            {
                return;
            }

            var status = await backend
                .GetOverlayStatusAsync(TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);
            if (!ReferenceEquals(_agentBackend, backend))
            {
                return;
            }

            _diagnosticState.CaptureOverlayStatus(status);
            if (result.StatusCode != AgentStatusCode.Ok)
            {
                _diagnosticState.CaptureOverlayFailure(
                    $"切换游戏内面板未确认完成：{result.StatusCode}。",
                    status);
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_agentBackend, backend))
            {
                _diagnosticState.CaptureOverlayFailure($"切换游戏内面板失败：{ex.Message}。");
            }
        }
    }

    private void StartOverlayMonitor(InjectedAgentBackend backend)
    {
        CancelOverlayMonitor();
        var cancellation = new CancellationTokenSource();
        _overlayMonitorCancellation = cancellation;
        _ = MonitorOverlayReadyAsync(backend, cancellation.Token);
    }

    private async Task MonitorOverlayReadyAsync(
        InjectedAgentBackend backend,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        AgentOverlayStatusPayload status = default;
        try
        {
            do
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                status = await backend
                    .GetOverlayStatusAsync(TimeSpan.FromSeconds(1), cancellationToken)
                    .ConfigureAwait(false);
                if (!ReferenceEquals(_agentBackend, backend))
                {
                    return;
                }

                _diagnosticState.CaptureOverlayStatus(status);
                if (status.IsReady)
                {
                    RecordOverlayReady();
                    return;
                }

                if (status.Lifecycle == AgentOverlayLifecycle.Failed)
                {
                    _diagnosticState.CaptureOverlayFailure(
                        $"游戏内面板初始化失败：{status.LastError}。其他修改器功能仍可使用。",
                        status);
                    return;
                }
            }
            while (DateTime.UtcNow < deadline);

            _diagnosticState.CaptureOverlayFailure(
                "游戏内面板在 3 秒内未进入就绪状态。其他修改器功能仍可使用。",
                status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_agentBackend, backend))
            {
                _diagnosticState.CaptureOverlayFailure(
                    $"游戏内面板状态读取失败：{ex.Message}。其他修改器功能仍可使用。");
            }
        }
    }

    private void RecordOverlayReady()
    {
        _diagnosticState.RecordEvent(
            DiagnosticEventSeverity.Info,
            DiagnosticEventCodes.OverlayReady,
            "游戏内面板已就绪，按 F10 可隐藏或显示。");
    }

    private void CancelOverlayMonitor()
    {
        var cancellation = Interlocked.Exchange(ref _overlayMonitorCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private static bool ShouldEnableOverlay(TrainerTarget? target)
    {
        if (target is null)
        {
            return false;
        }

        return string.Equals(
            target.VersionProfileId,
            Ra3VersionProfileRegistry.Ra3112.Id,
            StringComparison.OrdinalIgnoreCase);
    }

    public void RecordDiagnosticEvent(
        DiagnosticEventSeverity severity,
        string code,
        string message,
        string? detail = null)
    {
        _diagnosticState.RecordEvent(severity, code, message, detail);
    }

    private static string ResolveDefaultAgentDllPath() => ResolveDefaultAgentDllPath(AppContext.BaseDirectory);

    internal static string ResolveDefaultAgentDllPath(string baseDirectory)
    {
        var appLocalPath = Path.Combine(baseDirectory, "RayaTrainer.Agent.dll");

        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "RayaTrainer.sln");
            if (File.Exists(solutionPath))
            {
                // Development runs may have both configurations on disk. Never select a DLL
                // from the other configuration merely because it was built more recently.
                var configuration = Path.GetRelativePath(directory.FullName, baseDirectory)
                    .Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(segment =>
                        segment.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                        segment.Equals("Release", StringComparison.OrdinalIgnoreCase));
                if (configuration is null)
                {
                    break;
                }

                var configurationArtifact = Path.Combine(
                    directory.FullName,
                    "artifacts",
                    "native",
                    configuration,
                    "Win32",
                    "RayaTrainer.Agent.dll");
                return new[] { appLocalPath, configurationArtifact }
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault() ?? appLocalPath;
            }

            directory = directory.Parent;
        }

        return appLocalPath;
    }

    private static string FormatInstallableProfiles()
    {
        return string.Join("、", Ra3VersionProfileRegistry.InstallableProfiles.Select(profile => profile.DisplayName));
    }

    // Phase 1.2 Backward compatibility method - tests rely on this.
    // The actual gate logic is now inline in FeatureCapabilitySnapshot.Compute().
    internal static bool IsUnitUpgradeNativeLayoutReady(Ra3VersionProfile profile)
    {
        if (profile is null) return false;
        return string.Equals(profile.Id, "ra3_1.12", StringComparison.OrdinalIgnoreCase);
    }
}
