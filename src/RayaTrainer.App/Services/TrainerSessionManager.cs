using System.Linq;
using System.IO;
using RayaTrainer.Core.Agent;
using RayaTrainer.App.Hotkeys;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.Services;

public sealed class TrainerSessionManager : ITrainerSessionService, ITrainerDiagnosticsSource, IDisposable
{
    private readonly Func<InjectedAgentBackend> _agentBackendFactory;
    private readonly Func<string> _agentDllPathProvider;
    private InjectedAgentBackend? _agentBackend;
    private TrainerTarget? _agentTarget;
    private int? _targetProcessId;
    private bool _arePatchesInstalled;
    private ITrainerFeatureController? _featureController;
    private readonly Dictionary<string, string> _unavailableFeatureReasons = new(StringComparer.Ordinal);
    private readonly ForegroundWindowProcess _foregroundWindowProcess = new();
    private readonly TrainerDiagnosticState _diagnosticState = new();
    private TrainerManifest? _manifest;
    private TrainerTarget? _currentTarget;

    public TrainerSessionManager()
        : this(() => new InjectedAgentBackend(), ResolveDefaultAgentDllPath)
    {
    }

    public TrainerSessionManager(
        Func<InjectedAgentBackend> agentBackendFactory,
        Func<string> agentDllPathProvider)
    {
        _agentBackendFactory = agentBackendFactory;
        _agentDllPathProvider = agentDllPathProvider;
        _capabilityPolicy = new TrainerFeatureCapabilityPolicy();
        _diagnosticState.OnChanged = () => DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

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
        _unavailableFeatureReasons.Clear();
        RecordDiagnosticEvent(
            DiagnosticEventSeverity.Info,
            "attach.started",
            $"开始连接 {target.VersionProfileId ?? target.FileVersion}（DLL Agent）。");

        if (target.ProcessId is null)
        {
            _diagnosticState.RecordFailure("attach.failed", "无法确定目标进程 PID。");
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
            _diagnosticState.RecordFailure("attach.profile_unsupported", result.Message);
            return result;
        }

        if (!target.VersionSupported)
        {
            ClearAttachState();
            var result = new AttachResult(false, $"版本不支持；DLL Agent 可安装版本：{FormatInstallableProfiles()}。");
            _diagnosticState.RecordFailure("attach.version_unsupported", result.Message);
            return result;
        }

        _targetProcessId = target.ProcessId;
        _agentTarget = target;
        _agentBackend = _agentBackendFactory();
        try
        {
            _diagnosticState.SetAgentStatus(_agentBackend
                .AttachAsync(target, manifest, _agentDllPathProvider(), TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult());

            // Deliver the per-profile native catalog BETWEEN attach and install, so
            // InstallPatchesAsync sees a ready catalog and every native hook handler
            // immediately has the correct profile-specific RVAs.
            _agentBackend
                .DeliverNativeCatalogAsync(target, TimeSpan.FromSeconds(30))
                .GetAwaiter()
                .GetResult();
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

            if (ex is NativeCatalogDeliveryException)
            {
                _diagnosticState.CaptureNativeCatalogDeliveryFailure(ex);
                throw new InvalidOperationException(ex.Message, ex);
            }

            _diagnosticState.RecordFailure("agent.attach_failed", ex.Message);
            if (ex is AgentCompatibilityException)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }

            throw new InvalidOperationException($"DLL Agent 注入失败：{ex.Message}", ex);
        }

        _diagnosticState.CaptureAgent(_agentBackend);
        ApplyProfileFeatureAvailability(manifest, profile);
        var displayName = profile.DisplayName;
        var resumedInstalledAgent = _agentBackend.ReusedExistingAgent && _diagnosticState.AgentStatus is { InstalledHookCount: > 0 };
        if (resumedInstalledAgent)
        {
            _featureController = _agentBackend.CreateFeatureController(_diagnosticState.AgentStatus!.Value);
            _arePatchesInstalled = true;
            var effectiveHookCount = TrainerDiagnosticState.CountEffectiveHooks(manifest, profile);
            _diagnosticState.SetPatchInstallResult(new PatchInstallResult(
                effectiveHookCount,
                checked((int)_diagnosticState.AgentStatus!.Value.InstalledHookCount),
                []));
            _diagnosticState.CaptureRuntimeState(_featureController, _arePatchesInstalled);
        }

        var attachResult = resumedInstalledAgent
            ? new AttachResult(
                true,
                $"已重新连接 {displayName} 中现有的 DLL Agent，已恢复 {_diagnosticState.AgentStatus!.Value.InstalledHookCount} 个 Hook 的控制。")
            : target.SignatureCompatibilityMode
                ? new AttachResult(true, $"已连接 {displayName}（签名兼容校验通过，尚未安装 Patch）。")
                : new AttachResult(true, $"已连接 {displayName}（DLL Agent）。");
        _diagnosticState.RecordEvent(
            DiagnosticEventSeverity.Info,
            resumedInstalledAgent ? "agent.reconnected" : "agent.attached",
            attachResult.Message);
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
    }

    public SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir)
    {
        _diagnosticState.RecordEvent(DiagnosticEventSeverity.Info, "patch.install_started", "开始安装 Patch。", "DLL Agent");
        if (_agentBackend is null || _agentTarget is null)
        {
            throw new InvalidOperationException("请先检测进程。");
        }

        AgentCommandResultPayload result;
        try
        {
            result = _agentBackend
                .InstallPatchesAsync(
                    manifest,
                    _agentTarget,
                    TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
        }
        catch (InvalidOperationException ex) when (IsAgentPatchMismatch(ex))
        {
            var (reportPath, mismatchKind) = WriteAgentMismatchReport(diagnosticsDir);
            _arePatchesInstalled = false;
            _diagnosticState.SetReportPath(reportPath);
            var eventCode = mismatchKind switch
            {
                MismatchKind.RuntimePatchSet => "agent.patchset_install_mismatch",
                MismatchKind.PatchSetIpConflict => "agent.patchset_codeflow_ip_conflict",
                _ => "patch.mismatch"
            };
            _diagnosticState.RecordFailure(
                eventCode,
                mismatchKind switch
                {
                    MismatchKind.RuntimePatchSet => "Agent Patch 安装失败：运行时 PatchSet 入口字节不匹配。",
                    MismatchKind.PatchSetIpConflict => "Agent Patch 安装失败：线程 IP 冲突导致 CodeFlow 入口无法安全 patch。",
                    _ => "Agent Patch 安装失败：Hook 原始字节不匹配。"
                },
                reportPath);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(reportPath)
                    ? "Agent Patch 安装失败：hook 字节不匹配，且未能从 Agent 拉取诊断。"
                    : $"Agent Patch 安装失败：hook 字节不匹配。诊断日志：{reportPath}",
                ex);
        }
        catch (Exception ex)
        {
            _diagnosticState.RecordFailure("patch.install_failed", ex.Message);
            throw;
        }

        _diagnosticState.SetAgentStatus(_diagnosticState.AgentStatus is AgentStatusPayload status
            ? status with { InstalledHookCount = result.InstalledHookCount }
            : null);
        if (_diagnosticState.AgentStatus is not null)
        {
            _featureController = _agentBackend.CreateFeatureController(_diagnosticState.AgentStatus.Value);
        }
        _arePatchesInstalled = true;
        var agentInstallResult = new PatchMismatchReportResult(
            new PatchInstallResult(
                manifest.PatchManifest.Hooks.Count,
                checked((int)result.InstalledHookCount),
                _agentBackend.LastSkippedHookPlans.Select(ToSkippedPatchHook).ToArray()),
            ReportPath: null);
        _diagnosticState.CapturePatchResult(agentInstallResult);
        ApplySkippedHooks(agentInstallResult, TrainerFeatureCatalog.CreateGridFeatures(manifest.Features));
        if (_featureController is null)
        {
            MarkFeaturesUnavailable([], null, TrainerFeatureCatalog.CreateGridFeatures(manifest.Features));
        }
        _diagnosticState.CaptureRuntimeState(_featureController, _arePatchesInstalled);
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
        if (restoreAgentPatches && _agentBackend is not null && _arePatchesInstalled)
        {
            try
            {
                _agentBackend.RestorePatchesAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Cleanup is best-effort; target process may have already exited.
                _diagnosticState.RecordEvent(
                    DiagnosticEventSeverity.Warning,
                    "patch.restore_warning",
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
        _diagnosticState.ClearDiagnosticState();
        _unavailableFeatureReasons.Clear();
        if (hadTarget)
        {
            _diagnosticState.RecordEvent(DiagnosticEventSeverity.Info, "session.reset", "会话已结束，运行时状态已清理。");
        }
        else
        {
            DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        ResetPatchesState();
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

        // 1. Compute base snapshot using existing evaluator (behavior-preserving).
        var profile = _currentTarget is null ? null : Ra3VersionProfileRegistry.ResolveTargetProfile(_currentTarget);
        var directGameApiReady = _featureController is IAgentFeatureController { SupportsDirectGameApi: true };
        var baseSnapshot = TrainerFeatureCapabilityEvaluator.Evaluate(
            feature,
            new TrainerFeatureCapabilityContext(
                HasTarget: _currentTarget is not null || (_arePatchesInstalled && _featureController is not null),
                SessionReady: CanUseFeatures || _featureController is not null,
                PatchesInstalled: _arePatchesInstalled,
                BackendSupportsDirectGameApi: _currentTarget is null || profile?.SupportsDirectGameApi == true,
                DirectGameApiReady: directGameApiReady,
                UnavailableReason: _unavailableFeatureReasons.GetValueOrDefault(feature.RawName)));

        // 2. Build capability context for policy evaluation.
        var capContext = BuildCapabilityContext(baseSnapshot);

        // 3. Evaluate policy gates (composite NativeToggle, CapabilityOnly profile,
        //    transitional P1 special cases).
        var evaluation = _capabilityPolicy.Evaluate(feature, capContext);

        // 4. Apply evaluation result on top of base snapshot.
        return ApplyEvaluation(baseSnapshot, evaluation);
    }

    /// <summary>
    /// Assembles the <see cref="ITrainerFeatureCapabilityContext"/> from current session state.
    /// </summary>
    private ITrainerFeatureCapabilityContext BuildCapabilityContext(FeatureCapabilitySnapshot baseSnapshot)
    {
        var profile = _currentTarget is null ? null : Ra3VersionProfileRegistry.ResolveTargetProfile(_currentTarget);
        return new SessionManagerCapabilityContext
        {
            IsAgentConnected = _agentBackend?.IsConnected == true,
            CurrentProfile = profile,
            InstalledNativeHookIds = _agentBackend?.InstalledNativeHookIds ?? (IReadOnlyCollection<uint>)Array.Empty<uint>(),
            RegisteredPatchSetIds = _agentBackend?.PatchSetsRegistered ?? (IReadOnlyCollection<uint>)Array.Empty<uint>(),
            IsNativeCatalogDelivered = _agentBackend?.IsNativeCatalogDelivered == true,
            BaseSnapshot = baseSnapshot
        };
    }

    /// <summary>
    /// Merges a <see cref="FeatureCapabilityEvaluation"/> back into the base snapshot,
    /// preserving the feature identity metadata (FeatureId, DisplayName, GroupName).
    /// </summary>
    private static FeatureCapabilitySnapshot ApplyEvaluation(
        FeatureCapabilitySnapshot baseSnapshot,
        FeatureCapabilityEvaluation evaluation)
    {
        return baseSnapshot with
        {
            State = evaluation.State,
            ReasonCode = evaluation.ReasonCode,
            Reason = evaluation.Reason ?? baseSnapshot.Reason
        };
    }

    /// <summary>
    /// Checks that the three native-agent catalog entries required for object-level upgrade
    /// grant are all Verified with a non-zero RVA. Delegates to the policy class.
    /// </summary>
    internal static bool IsUnitUpgradeNativeLayoutReady(Ra3VersionProfile profile) =>
        TrainerFeatureCapabilityPolicy.IsUnitUpgradeNativeLayoutReady(profile);

    /// <summary>
    /// Default <see cref="ITrainerFeatureCapabilityContext"/> implementation that reads from
    /// the session manager's current state.
    /// </summary>
    private sealed class SessionManagerCapabilityContext : ITrainerFeatureCapabilityContext
    {
        public bool IsAgentConnected { get; init; }
        public Ra3VersionProfile? CurrentProfile { get; init; }
        public IReadOnlyCollection<uint> InstalledNativeHookIds { get; init; } = Array.Empty<uint>();
        public IReadOnlyCollection<uint> RegisteredPatchSetIds { get; init; } = Array.Empty<uint>();
        public bool IsNativeCatalogDelivered { get; init; }
        public FeatureCapabilitySnapshot BaseSnapshot { get; init; } = null!;
    }

    private string CreatePatchInstalledStatus(PatchMismatchReportResult result)
    {
        var skippedPatchSetCount = _agentBackend?.SkippedPatchSetIds.Count ?? 0;
        var patchSetNotice = skippedPatchSetCount == 0
            ? string.Empty
            : "；当前游戏构建的 60fps 运行时补丁位置不同，已安全禁用“60fps 帧率解锁”，其他功能不受影响";
        if (result.SkippedHooks.Count == 0)
        {
            return $"DLL Agent Patch 已安装，Hook={result.InstallResult.InstalledHookCount}{patchSetNotice}；{RemoteSymbolSummary}";
        }

        var disabledCount = _unavailableFeatureReasons.Count;
        var message = $"Patch 已部分安装；{result.SkippedHooks.Count} 个 hook 因版本未验证或字节不匹配已跳过，{disabledCount} 个相关功能已禁用{patchSetNotice}。";
        return string.IsNullOrWhiteSpace(result.ReportPath)
            ? message
            : $"{message} 诊断日志：{result.ReportPath}";
    }

    private void ApplySkippedHooks(PatchMismatchReportResult result, IEnumerable<TrainerFeature> features)
    {
        var hasUnmappedHook = result.SkippedHooks
            .Any(skipped => skipped.Hook.EnableFlags.Count == 0);
        var skippedFlags = result.SkippedHooks
            .SelectMany(skipped => skipped.Hook.EnableFlags)
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!hasUnmappedHook && skippedFlags.Length == 0)
        {
            return;
        }

        MarkFeaturesUnavailable(hasUnmappedHook ? [] : skippedFlags, result.ReportPath, features);
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
            }

            if (_arePatchesInstalled && _featureController is not null)
            {
                await Task.Run(() => _diagnosticState.CaptureRuntimeState(_featureController, _arePatchesInstalled), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _diagnosticState.ClearRuntimeReadState();
            }
        }
        catch (Exception ex)
        {
            _diagnosticState.RecordEvent(
                DiagnosticEventSeverity.Warning,
                "runtime.refresh_failed",
                "运行时诊断刷新失败。",
                ex.Message);
        }

        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        return GetDiagnosticSnapshot(features);
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
                // Development run: prefer the newest Agent DLL under artifacts/native. The
                // build script may target either Debug or Release; hard-coding one config
                // silently loads a stale DLL from the other config and surfaces as a
                // protocol-mismatch error at injection time (the App is fresh, the DLL is not).
                var candidates = new[]
                {
                    Path.Combine(directory.FullName, "artifacts", "native", "Debug", "Win32", "RayaTrainer.Agent.dll"),
                    Path.Combine(directory.FullName, "artifacts", "native", "Release", "Win32", "RayaTrainer.Agent.dll")
                };
                var newestArtifact = candidates
                    .Where(File.Exists)
                    .Select(File.GetLastWriteTimeUtc)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                if (newestArtifact != DateTime.MinValue)
                {
                    var appLocalTime = File.Exists(appLocalPath)
                        ? File.GetLastWriteTimeUtc(appLocalPath)
                        : DateTime.MinValue;
                    // Prefer app-local when it is newer than every artifact: build-and-run.ps1
                    // copies the just-built DLL next to the App, so app-local is the freshest
                    // source in the common case. Fall back to the newest artifact only when the
                    // developer built the DLL without copying it (e.g. ran MSBuild directly).
                    if (appLocalTime >= newestArtifact)
                    {
                        return appLocalPath;
                    }

                    return candidates.First(path => File.Exists(path) && File.GetLastWriteTimeUtc(path) == newestArtifact);
                }

                break;
            }

            directory = directory.Parent;
        }

        return appLocalPath;
    }

    private static SkippedPatchHook ToSkippedPatchHook(SkippedPatchHookPlan skipped)
    {
        return new SkippedPatchHook(
            skipped.Hook,
            skipped.HookIndex,
            skipped.HookCount,
            AbsoluteAddress: 0,
            ExpectedBytes: skipped.Hook.OriginalBytes.ToArray(),
            ActualBytes: [],
            DumpStartAddress: 0,
            DumpBytes: [],
            skipped.Reason);
    }

    /// <summary>
    /// Recognizes the bare PatchMismatch error thrown by
    /// <see cref="InjectedAgentBackend.InstallPatchesAsync"/> so we can intercept it and pull
    /// byte-level diagnostics from the DLL instead of letting it surface as an opaque failure.
    /// </summary>
    private static bool IsAgentPatchMismatch(InvalidOperationException ex)
    {
        return ex.Message.Contains("PatchMismatch", StringComparison.Ordinal);
    }

    /// <summary>
    /// Pulls the last mismatch diagnostic from the DLL (either Hook, RuntimePatchSet, or
    /// PatchSetIpConflict) and writes a PatchMismatchReport, mirroring the external-memory
    /// backend's report. Returns (report path, mismatch kind) or (null, Hook) when no
    /// diagnostic is available.
    /// </summary>
    private (string? Path, MismatchKind Kind) WriteAgentMismatchReport(string diagnosticsDir)
    {
        if (_agentBackend is null || _agentTarget is null)
        {
            return (null, MismatchKind.Hook);
        }

        // Prefer the diagnostic already fetched by InjectedAgentBackend.InstallPatchesAsync,
        // which will have the extended kind/subject fields. Fall back to a direct query when
        // the backend hasn't cached one (backward compat).
        var diagnostic = _agentBackend.LastMismatchDiagnostic;
        if (diagnostic is null)
        {
            // Query the agent directly. This populates LastMismatchDiagnostic on success
            // and returns null if the agent has no pending diagnostic.
            try
            {
                diagnostic = _agentBackend
                    .GetMismatchDiagnosticsAsync(TimeSpan.FromSeconds(2))
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // The DLL may be in a state where the diagnostics query itself fails; fall back to
                // the bare status-code message in that case.
                return (null, MismatchKind.Hook);
            }

            if (diagnostic is null)
            {
                return (null, MismatchKind.Hook);
            }
        }

        var kind = diagnostic.Kind;
        var absoluteAddress = unchecked((nint)diagnostic.HookAddress);

        // Build a synthetic PatchHookPlan so the existing PatchMismatchReportWriter can render
        // the same expected/actual/dump layout it uses for the external backend.
        // For IP conflicts, the payload carries no expected/actual/dump bytes, so use empty arrays.
        var hasBytes = diagnostic.ExpectedBytes.Length > 0;
        var syntheticHook = new PatchHookPlan(
            Address: $"0x{diagnostic.HookAddress:X}",
            PatchLength: Math.Max(5, hasBytes ? diagnostic.ExpectedBytes.Length : 5),
            OriginalBytes: hasBytes ? diagnostic.ExpectedBytes : [0x90])
        {
            SectionTitle = kind switch
            {
                MismatchKind.RuntimePatchSet => "Agent PatchSet pre-install mismatch",
                MismatchKind.PatchSetIpConflict => "Agent PatchSet CodeFlow IP conflict",
                _ => "Agent hook (version mismatch)"
            }
        };

        var skipped = new SkippedPatchHook(
            syntheticHook,
            HookIndex: 0,
            HookCount: 0,
            absoluteAddress,
            hasBytes ? diagnostic.ExpectedBytes : [],
            hasBytes ? diagnostic.ActualBytes : [],
            unchecked((nint)diagnostic.HookAddress),
            hasBytes ? diagnostic.DumpBytes : [],
            kind switch
            {
                MismatchKind.RuntimePatchSet =>
                    "PatchSet 入口原始字节不匹配；可能原因：该位置已经被 patch 过、游戏版本不一致。",
                MismatchKind.PatchSetIpConflict =>
                    "CodeFlow 入口线程 IP 冲突；目标线程在 ±16 字节安全区内执行，无法安全 patch。",
                _ => "Patch 点原始字节不匹配；可能原因：该位置已经被 patch 过、游戏版本不一致，或者 MOD 加载时修改了代码段。"
            });

        var installResult = new PatchInstallResult(
            HookCount: 0,
            InstalledHookCount: 0,
            new[] { skipped });

        var path = PatchMismatchReportWriter.Write(
            diagnosticsDir,
            _agentTarget,
            installResult,
            new PatchMismatchReportOptions(),
            diagnostics: [diagnostic]);

        return (path, kind);
    }

    private static string FormatInstallableProfiles()
    {
        return string.Join("、", Ra3VersionProfileRegistry.InstallableProfiles.Select(profile => profile.DisplayName));
    }
}
