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
    private AgentStatusPayload? _agentStatus;
    private int? _targetProcessId;
    private bool _arePatchesInstalled;
    private ITrainerFeatureController? _featureController;
    private readonly Dictionary<string, string> _unavailableFeatureReasons = new(StringComparer.Ordinal);
    private readonly ForegroundWindowProcess _foregroundWindowProcess = new();
    private readonly TrainerDiagnosticEventBuffer _diagnosticEvents = new();
    private TrainerManifest? _manifest;
    private TrainerTarget? _currentTarget;
    private AgentSignatureScanPayload? _lastSignatureScan;
    private IReadOnlyList<string> _lastRequiredUnresolvedSignatures = [];
    private IReadOnlyList<string> _lastOptionalUnresolvedSignatures = [];
    private IReadOnlyList<string> _lastOptionalSignatureSymbols = [];
    private PatchInstallResult _lastPatchInstallResult = PatchInstallResult.Empty;
    private string? _lastReportPath;
    private string? _lastDiagnosticErrorCode;
    private string? _lastDiagnosticError;
    private int? _lastGameMode;
    private uint? _lastGameThreadTick;
    private bool _lastRuntimeReadAttempted;
    private bool _lastRuntimeReadSucceeded;
    private bool _lastRuntimeReadFailed;

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
    }

    public event EventHandler? DiagnosticsChanged;

    public IReadOnlyList<TrainerDiagnosticEvent> DiagnosticEvents => _diagnosticEvents.Snapshot();

    public ITrainerFeatureController? FeatureController => _featureController;

    public bool ArePatchesInstalled => _arePatchesInstalled;

    public int? TargetProcessId => _targetProcessId;

    public bool CanUseFeatures => _agentBackend?.IsConnected == true;

    public int InstalledHookCount => (int)(_agentStatus?.InstalledHookCount ?? 0);

    public string RemoteSymbolSummary =>
        _agentStatus is null
            ? "Native runtime 未连接。"
            : $"DLL Agent v{_agentStatus.Value.AgentVersion}: native capabilities=0x{_agentStatus.Value.NativeRuntimeCapabilities:X8}";

    public AttachResult AttachTarget(TrainerManifest manifest, TrainerTarget target)
    {
        _manifest = manifest;
        _currentTarget = target;
        _lastDiagnosticError = null;
        _lastDiagnosticErrorCode = null;
        _lastSignatureScan = null;
        _lastRequiredUnresolvedSignatures = [];
        _lastOptionalUnresolvedSignatures = [];
        _lastOptionalSignatureSymbols = [];
        _lastPatchInstallResult = PatchInstallResult.Empty;
        _lastReportPath = null;
        _lastGameMode = null;
        _lastGameThreadTick = null;
        _lastRuntimeReadAttempted = false;
        _lastRuntimeReadSucceeded = false;
        _lastRuntimeReadFailed = false;
        _unavailableFeatureReasons.Clear();
        RecordDiagnosticEvent(
            DiagnosticEventSeverity.Info,
            "attach.started",
            $"开始连接 {target.VersionProfileId ?? target.FileVersion}（DLL Agent）。");

        if (target.ProcessId is null)
        {
            RecordDiagnosticFailure("attach.failed", "无法确定目标进程 PID。");
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
            RecordDiagnosticFailure("attach.profile_unsupported", result.Message);
            return result;
        }

        if (!target.VersionSupported)
        {
            ClearAttachState();
            var result = new AttachResult(false, $"版本不支持；DLL Agent 可安装版本：{FormatInstallableProfiles()}。");
            RecordDiagnosticFailure("attach.version_unsupported", result.Message);
            return result;
        }

        _targetProcessId = target.ProcessId;
        _agentTarget = target;
        _agentBackend = _agentBackendFactory();
        try
        {
            _agentStatus = _agentBackend
                .AttachAsync(target, _agentDllPathProvider(), TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            CaptureAgentDiagnostics(_agentBackend);
            _agentBackend = null;
            _agentTarget = null;
            _agentStatus = null;
            _targetProcessId = null;
            _featureController = null;
            _arePatchesInstalled = false;
            RecordDiagnosticFailure("agent.attach_failed", ex.Message);
            if (ex is AgentCompatibilityException)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }

            throw new InvalidOperationException($"DLL Agent 注入失败：{ex.Message}", ex);
        }

        CaptureAgentDiagnostics(_agentBackend);
        ApplyProfileFeatureAvailability(manifest, profile);
        var displayName = profile.DisplayName;
        var resumedInstalledAgent = _agentBackend.ReusedExistingAgent && _agentStatus is { InstalledHookCount: > 0 };
        if (resumedInstalledAgent)
        {
            _featureController = _agentBackend.CreateFeatureController(_agentStatus!.Value);
            _arePatchesInstalled = true;
            var effectiveHookCount = CountEffectiveHooks(manifest, profile);
            _lastPatchInstallResult = new PatchInstallResult(
                effectiveHookCount,
                checked((int)_agentStatus.Value.InstalledHookCount),
                []);
            CaptureRuntimeStateSafely();
        }

        var attachResult = resumedInstalledAgent
            ? new AttachResult(
                true,
                $"已重新连接 {displayName} 中现有的 DLL Agent，已恢复 {_agentStatus!.Value.InstalledHookCount} 个 Hook 的控制。")
            : new AttachResult(true, $"已连接 {displayName}（DLL Agent）。");
        RecordDiagnosticEvent(
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
        _agentStatus = null;
        _targetProcessId = null;
        _featureController = null;
        _arePatchesInstalled = false;
    }

    public SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir)
    {
        RecordDiagnosticEvent(DiagnosticEventSeverity.Info, "patch.install_started", "开始安装 Patch。", "DLL Agent");
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
            var reportPath = WriteAgentMismatchReport(diagnosticsDir);
            _arePatchesInstalled = false;
            _lastReportPath = reportPath;
            RecordDiagnosticFailure(
                "patch.mismatch",
                "Agent Patch 安装失败：Hook 原始字节不匹配。",
                reportPath);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(reportPath)
                    ? "Agent Patch 安装失败：hook 字节不匹配，且未能从 Agent 拉取诊断。"
                    : $"Agent Patch 安装失败：hook 字节不匹配。诊断日志：{reportPath}",
                ex);
        }
        catch (Exception ex)
        {
            RecordDiagnosticFailure("patch.install_failed", ex.Message);
            throw;
        }

        _agentStatus = _agentStatus is AgentStatusPayload status
            ? status with { InstalledHookCount = result.InstalledHookCount }
            : null;
        if (_agentStatus is not null)
        {
            _featureController = _agentBackend.CreateFeatureController(_agentStatus.Value);
        }
        _arePatchesInstalled = true;
        var agentInstallResult = new PatchMismatchReportResult(
            new PatchInstallResult(
                manifest.PatchManifest.Hooks.Count,
                checked((int)result.InstalledHookCount),
                _agentBackend.LastSkippedHookPlans.Select(ToSkippedPatchHook).ToArray()),
            ReportPath: null);
        CapturePatchInstallResult(agentInstallResult);
        ApplySkippedHooks(agentInstallResult, TrainerFeatureCatalog.CreateGridFeatures(manifest.Features));
        if (_featureController is null)
        {
            MarkFeaturesUnavailable([], null, TrainerFeatureCatalog.CreateGridFeatures(manifest.Features));
        }
        CaptureRuntimeStateSafely();
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
                RecordDiagnosticEvent(
                    DiagnosticEventSeverity.Warning,
                    "patch.restore_warning",
                    "Patch 恢复未能确认完成。",
                    ex.Message);
            }
        }

        _agentBackend = null;
        _agentTarget = null;
        _agentStatus = null;
        _targetProcessId = null;
        _currentTarget = null;
        _featureController = null;
        _arePatchesInstalled = false;
        _lastPatchInstallResult = PatchInstallResult.Empty;
        _lastSignatureScan = null;
        _lastRequiredUnresolvedSignatures = [];
        _lastOptionalUnresolvedSignatures = [];
        _lastOptionalSignatureSymbols = [];
        _lastGameMode = null;
        _lastGameThreadTick = null;
        _lastRuntimeReadAttempted = false;
        _lastRuntimeReadSucceeded = false;
        _lastRuntimeReadFailed = false;
        _unavailableFeatureReasons.Clear();
        if (hadTarget)
        {
            RecordDiagnosticEvent(DiagnosticEventSeverity.Info, "session.reset", "会话已结束，运行时状态已清理。");
        }
        else
        {
            NotifyDiagnosticsChanged();
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

    public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var profile = _currentTarget is null ? null : Ra3VersionProfileRegistry.ResolveTargetProfile(_currentTarget);
        var directGameApiReady = _featureController is IAgentFeatureController { SupportsDirectGameApi: true };
        return TrainerFeatureCapabilityEvaluator.Evaluate(
            feature,
            new TrainerFeatureCapabilityContext(
                HasTarget: _currentTarget is not null || (_arePatchesInstalled && _featureController is not null),
                SessionReady: CanUseFeatures || _featureController is not null,
                PatchesInstalled: _arePatchesInstalled,
                BackendSupportsDirectGameApi: _currentTarget is null || profile?.SupportsDirectGameApi == true,
                DirectGameApiReady: directGameApiReady,
                UnavailableReason: _unavailableFeatureReasons.GetValueOrDefault(feature.RawName)));
    }

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

        NotifyDiagnosticsChanged();
    }

    public TrainerDiagnosticSnapshot GetDiagnosticSnapshot(
        IReadOnlyList<TrainerFeature> features,
        int maxEvents = TrainerDiagnosticEventBuffer.Capacity)
    {
        ArgumentNullException.ThrowIfNull(features);
        var events = _diagnosticEvents.Snapshot(maxEvents);
        var profile = _currentTarget is null
            ? null
            : Ra3VersionProfileRegistry.ResolveTargetProfile(_currentTarget);
        var target = _currentTarget is null
            ? null
            : new DiagnosticTargetSnapshot(
                _currentTarget.ProcessName,
                _currentTarget.ProcessId,
                _currentTarget.FileVersion,
                profile?.Id ?? _currentTarget.VersionProfileId ?? "unknown",
                profile?.DisplayName ?? "未知版本",
                TrainerRuntimeKind.Agent,
                _currentTarget.ModulePath,
                FormatAddress(_currentTarget.ModuleBase));

        var agentApplicable = _currentTarget is not null;
        var agentConnected = _agentBackend?.IsConnected == true;
        var agentStatus = _agentStatus ?? _agentBackend?.LastStatus;
        var agent = new AgentDiagnosticSnapshot(
            agentApplicable,
            agentConnected,
            agentStatus?.StatusCode,
            agentStatus?.AgentVersion,
            AgentProtocol.Version,
            FormatAddress(agentStatus?.ModuleBase),
            agentStatus?.NativeRuntimeCapabilities ?? 0,
            agentConnected
                    ? $"Agent 协议 v{agentStatus?.AgentVersion ?? 0} 已连接。"
                    : "Agent 尚未连接或握手失败。");

        var signatureApplicable = agentApplicable && profile?.SupportsSignatureScanning == true;
        var requiredCount = _lastSignatureScan is null
            ? 0
            : Math.Max(0, checked((int)_lastSignatureScan.EntryCount) - _lastOptionalSignatureSymbols.Count);
        var requiredMatched = Math.Max(0, requiredCount - _lastRequiredUnresolvedSignatures.Count);
        var signatures = new SignatureDiagnosticSnapshot(
            signatureApplicable,
            _lastSignatureScan?.EntryCount ?? 0,
            _lastSignatureScan?.MatchedCount ?? 0,
            requiredCount,
            requiredMatched,
            _lastRequiredUnresolvedSignatures,
            _lastOptionalUnresolvedSignatures,
            profile?.SupersededHooks.Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
            !signatureApplicable
                ? "当前 profile 不执行 Agent 签名扫描。"
                : _lastSignatureScan is null
                    ? "尚未取得签名扫描结果。"
                    : _lastRequiredUnresolvedSignatures.Count > 0
                        ? $"required 签名缺失 {_lastRequiredUnresolvedSignatures.Count} 项。"
                        : $"签名 {_lastSignatureScan.MatchedCount}/{_lastSignatureScan.EntryCount}；required 全部满足。");

        var manifestHookCount = _manifest?.PatchManifest.Hooks.Count ?? 0;
        var effectiveHookCount = CountEffectiveHooks(_manifest, profile);
        var skippedHooks = _lastPatchInstallResult.SkippedHooks
            .Select(skipped => new SkippedHookDiagnosticSnapshot(
                skipped.Hook.ReturnLabel ?? skipped.Hook.SectionTitle,
                skipped.Hook.Address,
                skipped.Hook.EnableFlags,
                skipped.Reason))
            .ToArray();
        var patch = new PatchDiagnosticSnapshot(
            manifestHookCount,
            effectiveHookCount,
            InstalledHookCount,
            skippedHooks,
            _lastReportPath,
            !_arePatchesInstalled
                ? "Patch 尚未安装。"
                : skippedHooks.Length > 0
                    ? $"已安装 {InstalledHookCount}/{effectiveHookCount}，跳过 {skippedHooks.Length} 个 Hook。"
                    : $"已安装 {InstalledHookCount}/{effectiveHookCount} 个 Hook。");

        var game = new GameRuntimeDiagnosticSnapshot(
            _lastGameMode,
            FormatGameMode(_lastGameMode),
            _lastGameThreadTick,
            _lastRuntimeReadAttempted,
            _lastRuntimeReadSucceeded,
            !_lastRuntimeReadAttempted
                ? "尚未读取游戏模式与 game tick。"
                : _lastRuntimeReadSucceeded
                ? $"{FormatGameMode(_lastGameMode)}；tick={_lastGameThreadTick?.ToString() ?? "?"}。"
                : "读取游戏模式或 game tick 失败。");

        var modulePath = target?.ModulePath ?? "";
        var laa = string.IsNullOrWhiteSpace(modulePath) || !File.Exists(modulePath)
            ? new LaaDiagnosticSnapshot(null, null, false, "未连接目标，无法检查 LAA。")
            : new LaaDiagnosticSnapshot(
                null,
                modulePath,
                LargeAddressAwarePatcher.HasBackup(modulePath),
                "点击「检查 LAA」查看。");

        var capabilities = features
            .DistinctBy(feature => feature.RawName, StringComparer.Ordinal)
            .Select(GetFeatureCapability)
            .OrderBy(capability => capability.GroupName, StringComparer.Ordinal)
            .ThenBy(capability => capability.DisplayName, StringComparer.Ordinal)
            .ToArray();
        var stages = BuildStages(target, profile, agent, signatures, patch, game);
        var health = CalculateHealth(target, stages, patch);
        var summary = health switch
        {
            TrainerDiagnosticHealth.Offline => "尚未连接游戏进程。",
            TrainerDiagnosticHealth.Error => _lastDiagnosticError ?? "检测到阻止当前会话继续工作的错误。",
            TrainerDiagnosticHealth.Attention => !_arePatchesInstalled
                ? "连接已建立，等待安装 Patch。"
                : "会话可用，但存在需要关注的降级或读取失败。",
            _ => "目标、运行后端、Patch 与当前运行时状态均正常。"
        };

        return new TrainerDiagnosticSnapshot(
            DateTimeOffset.Now,
            health,
            summary,
            target,
            agent,
            signatures,
            patch,
            game,
            laa,
            stages,
            capabilities,
            events,
            _lastReportPath);
    }

    public async Task<TrainerDiagnosticSnapshot> RefreshDiagnosticsAsync(
        IReadOnlyList<TrainerFeature> features,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_agentBackend?.IsConnected == true)
            {
                _agentStatus = await _agentBackend
                    .GetStatusAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_arePatchesInstalled && _featureController is not null)
            {
                await Task.Run(CaptureRuntimeStateSafely, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _lastGameMode = null;
                _lastGameThreadTick = null;
                _lastRuntimeReadAttempted = false;
                _lastRuntimeReadSucceeded = false;
                _lastRuntimeReadFailed = false;
            }
        }
        catch (Exception ex)
        {
            RecordRuntimeReadFailure(ex.Message);
        }

        NotifyDiagnosticsChanged();
        return GetDiagnosticSnapshot(features);
    }

    public void RecordDiagnosticEvent(
        DiagnosticEventSeverity severity,
        string code,
        string message,
        string? detail = null)
    {
        if (_diagnosticEvents.Add(severity, code, message, detail))
        {
            NotifyDiagnosticsChanged();
        }
    }

    private void CaptureAgentDiagnostics(InjectedAgentBackend? backend)
    {
        if (backend is null)
        {
            return;
        }

        _agentStatus = backend.LastStatus;
        _lastSignatureScan = backend.LastSignatureScan;
        _lastRequiredUnresolvedSignatures = backend.LastRequiredUnresolvedSignatures;
        _lastOptionalUnresolvedSignatures = backend.LastOptionalUnresolvedSignatures;
        _lastOptionalSignatureSymbols = backend.LastOptionalSignatureSymbols;
        if (_agentStatus is AgentStatusPayload status)
        {
            RecordDiagnosticEvent(
                DiagnosticEventSeverity.Info,
                "agent.handshake",
                $"Agent 协议握手完成：v{status.AgentVersion}。",
                $"expected=v{AgentProtocol.Version}; status={status.StatusCode}; fingerprint=0x{status.BuildFingerprint:X16}");
        }
        if (_lastSignatureScan is not null)
        {
            RecordDiagnosticEvent(
                _lastRequiredUnresolvedSignatures.Count == 0
                    ? DiagnosticEventSeverity.Info
                    : DiagnosticEventSeverity.Error,
                "agent.signature_scan",
                $"签名扫描 {_lastSignatureScan.MatchedCount}/{_lastSignatureScan.EntryCount}。",
                _lastRequiredUnresolvedSignatures.Count == 0
                    ? $"optional 未命中 {_lastOptionalUnresolvedSignatures.Count} 项。"
                    : $"required 未命中：{string.Join(", ", _lastRequiredUnresolvedSignatures)}");
        }
    }

    private void CapturePatchInstallResult(PatchMismatchReportResult result)
    {
        _lastPatchInstallResult = result.InstallResult;
        _lastReportPath = result.ReportPath;
        _lastDiagnosticErrorCode = null;
        _lastDiagnosticError = null;
        RecordDiagnosticEvent(
            result.SkippedHooks.Count == 0 ? DiagnosticEventSeverity.Info : DiagnosticEventSeverity.Warning,
            result.SkippedHooks.Count == 0 ? "patch.installed" : "patch.installed_partial",
            result.SkippedHooks.Count == 0
                ? $"Patch 已安装，Hook={result.InstallResult.InstalledHookCount}。"
                : $"Patch 已部分安装，跳过 {result.SkippedHooks.Count} 个 Hook。",
            result.ReportPath);
    }

    private void CaptureRuntimeState()
    {
        var controller = _featureController;
        if (!_arePatchesInstalled || controller is null)
        {
            _lastRuntimeReadAttempted = false;
            _lastRuntimeReadSucceeded = false;
            return;
        }

        _lastRuntimeReadAttempted = true;
        try
        {
            _lastGameMode = controller.ReadGameMode();
            _lastGameThreadTick = controller.ReadGameThreadTick();
            _lastRuntimeReadSucceeded = true;
            if (_lastRuntimeReadFailed)
            {
                _lastRuntimeReadFailed = false;
                RecordDiagnosticEvent(
                    DiagnosticEventSeverity.Info,
                    "runtime.refresh_recovered",
                    "运行时诊断读取已恢复。");
            }
        }
        catch
        {
            _lastRuntimeReadSucceeded = false;
            throw;
        }
    }

    private void CaptureRuntimeStateSafely()
    {
        try
        {
            CaptureRuntimeState();
        }
        catch (Exception ex)
        {
            RecordRuntimeReadFailure(ex.Message);
        }
    }

    private void RecordRuntimeReadFailure(string detail)
    {
        _lastRuntimeReadAttempted = true;
        _lastRuntimeReadSucceeded = false;
        if (_lastRuntimeReadFailed)
        {
            return;
        }

        _lastRuntimeReadFailed = true;
        RecordDiagnosticEvent(
            DiagnosticEventSeverity.Warning,
            "runtime.refresh_failed",
            "运行时诊断刷新失败。",
            detail);
    }

    private void RecordDiagnosticFailure(string code, string message, string? detail = null)
    {
        _lastDiagnosticErrorCode = code;
        _lastDiagnosticError = message;
        RecordDiagnosticEvent(DiagnosticEventSeverity.Error, code, message, detail);
    }

    private void NotifyDiagnosticsChanged() => DiagnosticsChanged?.Invoke(this, EventArgs.Empty);

    private static IReadOnlyList<DiagnosticStageSnapshot> BuildStages(
        DiagnosticTargetSnapshot? target,
        Ra3VersionProfile? profile,
        AgentDiagnosticSnapshot agent,
        SignatureDiagnosticSnapshot signatures,
        PatchDiagnosticSnapshot patch,
        GameRuntimeDiagnosticSnapshot game)
    {
        return
        [
            new("target", "TARGET", target is null ? DiagnosticStageState.Pending : DiagnosticStageState.Healthy,
                target is null ? "未连接目标" : $"PID {target.ProcessId}", target is null ? "检测或启动游戏进程" : null),
            new("profile", "PROFILE", target is null ? DiagnosticStageState.Pending : profile?.IsPatchInstallable == true ? DiagnosticStageState.Healthy : DiagnosticStageState.Error,
                profile?.DisplayName ?? "未识别", profile?.IsPatchInstallable == true ? null : "使用已验证的游戏版本"),
            new("agent", "AGENT", !agent.Applicable ? DiagnosticStageState.NotApplicable : agent.Connected && agent.AgentVersion == agent.ExpectedAgentVersion ? DiagnosticStageState.Healthy : DiagnosticStageState.Error,
                agent.Summary, agent.Applicable && !agent.Connected ? "检查 Agent DLL、权限与协议版本" : null),
            new("signature", "SIGNATURE", !signatures.Applicable ? DiagnosticStageState.NotApplicable : signatures.EntryCount == 0 ? DiagnosticStageState.Pending : signatures.RequiredUnresolved.Count == 0 ? DiagnosticStageState.Healthy : DiagnosticStageState.Error,
                signatures.Summary, signatures.RequiredUnresolved.Count > 0 ? "停止安装并重新验证 required 签名" : null),
            new("patch", "PATCH", patch.InstalledHookCount == 0 ? DiagnosticStageState.Pending : patch.SkippedHooks.Count > 0 ? DiagnosticStageState.Warning : DiagnosticStageState.Healthy,
                patch.Summary, patch.InstalledHookCount == 0 ? "安装 Patch" : patch.SkippedHooks.Count > 0 ? "检查跳过 Hook 与诊断报告" : null),
            new("game", "GAME LOOP", patch.InstalledHookCount == 0
                    ? DiagnosticStageState.Pending
                    : !game.ReadAttempted
                        ? DiagnosticStageState.Pending
                        : game.ReadSucceeded
                            ? DiagnosticStageState.Healthy
                            : DiagnosticStageState.Warning,
                game.Summary, game.ReadAttempted && !game.ReadSucceeded ? "进入对局或刷新运行时状态" : null)
        ];
    }

    private TrainerDiagnosticHealth CalculateHealth(
        DiagnosticTargetSnapshot? target,
        IReadOnlyList<DiagnosticStageSnapshot> stages,
        PatchDiagnosticSnapshot patch)
    {
        if (target is null)
        {
            return TrainerDiagnosticHealth.Offline;
        }

        if (!string.IsNullOrWhiteSpace(_lastDiagnosticError) || stages.Any(stage => stage.State == DiagnosticStageState.Error))
        {
            return TrainerDiagnosticHealth.Error;
        }

        if (!_arePatchesInstalled || patch.SkippedHooks.Count > 0 || stages.Any(stage => stage.State == DiagnosticStageState.Warning))
        {
            return TrainerDiagnosticHealth.Attention;
        }

        return TrainerDiagnosticHealth.Healthy;
    }

    private static int CountEffectiveHooks(TrainerManifest? manifest, Ra3VersionProfile? profile)
    {
        if (manifest is null)
        {
            return 0;
        }

        var profileId = profile?.Id ?? string.Empty;
        return manifest.PatchManifest.Hooks.Count(hook =>
            hook.SupportsProfile(profileId) &&
            (hook.ReturnLabel is null || profile?.SupersededHooks.Contains(hook.ReturnLabel) != true));
    }

    private static string FormatGameMode(int? gameMode) => gameMode switch
    {
        null => "未知",
        GameRuntimeConstants.GameModeShell => "主菜单",
        2 => "遭遇战",
        8 => "战役",
        _ => $"对局模式 {gameMode}"
    };

    private static string FormatAddress(nint? address) => address is null or 0 ? "" : $"0x{address.Value:X}";

    private static string FormatAddress(uint? address) => address is null or 0 ? "" : $"0x{address.Value:X8}";

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
    /// Pulls the last hook mismatch from the DLL and writes a PatchMismatchReport, mirroring
    /// the external-memory backend's report. Returns the report path, or null if the DLL had
    /// no diagnostic to report.
    /// </summary>
    private string? WriteAgentMismatchReport(string diagnosticsDir)
    {
        if (_agentBackend is null || _agentTarget is null)
        {
            return null;
        }

        AgentMismatchDiagnosticsPayload diagnostics;
        try
        {
            diagnostics = _agentBackend
                .GetMismatchDiagnosticsAsync(TimeSpan.FromSeconds(2))
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // The DLL may be in a state where the diagnostics query itself fails; fall back to
            // the bare status-code message in that case.
            return null;
        }

        if (!diagnostics.HasMismatch)
        {
            return null;
        }

        // The DLL reports the offending hook address and bytes but not the manifest metadata
        // (section title / enable flags), since address resolution now lives on the host.
        // Build a synthetic PatchHookPlan so the existing PatchMismatchReportWriter can render
        // the same expected/actual/dump layout it uses for the external backend.
        var absoluteAddress = unchecked((nint)diagnostics.HookAddress);
        var syntheticHook = new PatchHookPlan(
            Address: $"0x{diagnostics.HookAddress:X}",
            PatchLength: Math.Max(5, diagnostics.ExpectedBytes.Length),
            OriginalBytes: diagnostics.ExpectedBytes)
        {
            SectionTitle = "Agent hook (version mismatch)"
        };

        var skipped = new SkippedPatchHook(
            syntheticHook,
            HookIndex: 0,
            HookCount: 0,
            absoluteAddress,
            diagnostics.ExpectedBytes,
            diagnostics.ActualBytes,
            unchecked((nint)diagnostics.DumpStartAddress),
            diagnostics.DumpBytes,
            "Patch 点原始字节不匹配；可能原因：该位置已经被 patch 过、游戏版本不一致，或者 MOD 加载时修改了代码段。");

        var installResult = new PatchInstallResult(
            HookCount: 0,
            InstalledHookCount: 0,
            new[] { skipped });

        return PatchMismatchReportWriter.Write(diagnosticsDir, _agentTarget, installResult, new PatchMismatchReportOptions());
    }

    private static string FormatInstallableProfiles()
    {
        return string.Join("、", Ra3VersionProfileRegistry.InstallableProfiles.Select(profile => profile.DisplayName));
    }
}
