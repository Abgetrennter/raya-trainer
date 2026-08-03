using System.IO;
using System.Linq;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.Services;

internal sealed class TrainerDiagnosticState
{
    private readonly TrainerDiagnosticEventBuffer _events = new();
    private AgentStatusPayload? _agentStatus;
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
    private TrainerMismatchDiagnostic? _latestMismatchDiagnostic;
    private OverlayDiagnosticSnapshot _overlay = OverlayDiagnosticSnapshot.NotApplicable;
    private ProductControlDiagnostics? _productControl;

    public Action? OnChanged;

    public IReadOnlyList<TrainerDiagnosticEvent> Events => _events.Snapshot();

    public AgentStatusPayload? AgentStatus => _agentStatus;

    public int InstalledHookCount => (int)(_agentStatus?.InstalledHookCount ?? 0);

    public string? LastReportPath => _lastReportPath;

    public string? LastDiagnosticError => _lastDiagnosticError;

    /// <summary>
    /// The most recent mismatch diagnostic (Hook, RuntimePatchSet, or PatchSetIpConflict)
    /// captured during patch installation. Null when no mismatch has been recorded.
    /// </summary>
    public TrainerMismatchDiagnostic? LatestMismatchDiagnostic => _latestMismatchDiagnostic;

    public void ResetForAttach()
    {
        _lastDiagnosticError = null;
        _lastDiagnosticErrorCode = null;
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
        _latestMismatchDiagnostic = null;
        _overlay = OverlayDiagnosticSnapshot.NotApplicable;
        _productControl = null;
    }

    public void ClearDiagnosticState()
    {
        _lastPatchInstallResult = PatchInstallResult.Empty;
        _lastRequiredUnresolvedSignatures = [];
        _lastOptionalUnresolvedSignatures = [];
        _lastOptionalSignatureSymbols = [];
        _lastGameMode = null;
        _lastGameThreadTick = null;
        _lastRuntimeReadAttempted = false;
        _lastRuntimeReadSucceeded = false;
        _lastRuntimeReadFailed = false;
        _latestMismatchDiagnostic = null;
        _overlay = OverlayDiagnosticSnapshot.NotApplicable;
        _productControl = null;
    }

    public void ClearRuntimeReadState()
    {
        _lastGameMode = null;
        _lastGameThreadTick = null;
        _lastRuntimeReadAttempted = false;
        _lastRuntimeReadSucceeded = false;
        _lastRuntimeReadFailed = false;
    }

    public void SetAgentStatus(AgentStatusPayload? status)
    {
        _agentStatus = status;
    }

    public void SetPatchInstallResult(PatchInstallResult result)
    {
        _lastPatchInstallResult = result;
    }

    public void SetReportPath(string? path)
    {
        _lastReportPath = path;
    }

    public void CaptureAgent(InjectedAgentBackend? backend)
    {
        if (backend is null)
        {
            return;
        }

        _agentStatus = backend.LastStatus;
        if (_agentStatus is AgentStatusPayload status)
        {
            RecordEvent(
                DiagnosticEventSeverity.Info,
                "agent.handshake",
                $"Agent 协议握手完成：v{status.AgentVersion}。",
                $"expected=v{AgentProtocol.Version}; status={status.StatusCode}; fingerprint=0x{status.BuildFingerprint:X16}");
        }
    }

    public void CapturePatchResult(PatchMismatchReportResult result)
    {
        _lastPatchInstallResult = result.InstallResult;
        _lastReportPath = result.ReportPath;
        _lastDiagnosticErrorCode = null;
        _lastDiagnosticError = null;

        // Track the latest mismatch diagnostic from the agent's extended cmd 34 payload.
        _latestMismatchDiagnostic = result.AllDiagnostics?.FirstOrDefault();

        // Emit kind-specific events when the agent reported a discriminated diagnostic.
        if (_latestMismatchDiagnostic is not null && result.SkippedHooks.Count > 0)
        {
            var (code, message) = _latestMismatchDiagnostic.Kind switch
            {
                MismatchKind.RuntimePatchSet =>
                    ("agent.patchset_install_mismatch",
                     $"PatchSet {_latestMismatchDiagnostic.SubjectId} 入口字节不匹配。"),
                MismatchKind.PatchSetIpConflict =>
                    ("agent.patchset_codeflow_ip_conflict",
                     $"PatchSet {_latestMismatchDiagnostic.SubjectId} 线程 IP 冲突。"),
                _ => ("agent.hook_mismatch",
                      $"Hook #{_latestMismatchDiagnostic.SubjectId} @ 0x{_latestMismatchDiagnostic.HookAddress:X8} 字节不匹配。")
            };
            RecordEvent(DiagnosticEventSeverity.Warning, code, message, result.ReportPath);
        }
        else
        {
            RecordEvent(
                result.SkippedHooks.Count == 0 ? DiagnosticEventSeverity.Info : DiagnosticEventSeverity.Warning,
                result.SkippedHooks.Count == 0 ? "patch.installed" : "patch.installed_partial",
                result.SkippedHooks.Count == 0
                    ? $"Patch 已安装，Hook={result.InstallResult.InstalledHookCount}。"
                    : $"Patch 已部分安装，跳过 {result.SkippedHooks.Count} 个 Hook。",
                result.ReportPath);
        }
    }

    public void CaptureRuntimeState(ITrainerFeatureController? controller, bool arePatchesInstalled)
    {
        if (!arePatchesInstalled || controller is null)
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
                RecordEvent(
                    DiagnosticEventSeverity.Info,
                    "runtime.refresh_recovered",
                    "运行时诊断读取已恢复。");
            }
        }
        catch (Exception ex)
        {
            _lastRuntimeReadSucceeded = false;
            if (!_lastRuntimeReadFailed)
            {
                _lastRuntimeReadFailed = true;
                RecordEvent(
                    DiagnosticEventSeverity.Warning,
                    "runtime.refresh_failed",
                    "运行时诊断刷新失败。",
                    ex.Message);
            }
        }
    }

    public void CaptureOverlayStatus(AgentOverlayStatusPayload status)
    {
        _overlay = new OverlayDiagnosticSnapshot(
            true,
            status.StatusCode,
            status.Lifecycle,
            status.Flags,
            status.RenderFrameCount,
            status.ButtonClickCount,
            status.DeviceResetCount,
            status.LastError,
            FormatOverlaySummary(status));
        OnChanged?.Invoke();
    }

    public void CaptureOverlayFailure(string message, AgentOverlayStatusPayload? status = null)
    {
        _overlay = status is AgentOverlayStatusPayload payload
            ? new OverlayDiagnosticSnapshot(
                true,
                payload.StatusCode,
                payload.Lifecycle,
                payload.Flags,
                payload.RenderFrameCount,
                payload.ButtonClickCount,
                payload.DeviceResetCount,
                payload.LastError,
                message)
            : new OverlayDiagnosticSnapshot(
                true,
                AgentStatusCode.InternalError,
                AgentOverlayLifecycle.Failed,
                AgentOverlayFlags.None,
                0,
                0,
                0,
                AgentOverlayError.None,
                message);
        RecordEvent(DiagnosticEventSeverity.Warning, "overlay.degraded", message);
    }

    public void SetOverlayNotApplicable()
    {
        _overlay = OverlayDiagnosticSnapshot.NotApplicable;
    }

    /// <summary>
    /// Stores the latest Product Control Plane diagnostics (I4 negotiation + context /
    /// desired snapshot) and raises the shared change notification, mirroring
    /// <see cref="CaptureOverlayStatus"/>. The product-control state is display-only: it never
    /// participates in <see cref="CalculateHealth"/> so a target that lacks the plane stays
    /// Healthy.
    /// </summary>
    public void CaptureProductControlDiagnostics(ProductControlDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _productControl = diagnostics;
        OnChanged?.Invoke();
    }

    public void RecordEvent(
        DiagnosticEventSeverity severity,
        string code,
        string message,
        string? detail = null)
    {
        if (_events.Add(severity, code, message, detail))
        {
            OnChanged?.Invoke();
        }
    }

    public void RecordFailure(string code, string message, string? detail = null)
    {
        _lastDiagnosticErrorCode = code;
        _lastDiagnosticError = message;
        RecordEvent(DiagnosticEventSeverity.Error, code, message, detail);
    }

    public TrainerDiagnosticSnapshot GetSnapshot(
        IReadOnlyList<TrainerFeature> features,
        TrainerTarget? currentTarget,
        bool arePatchesInstalled,
        TrainerManifest? manifest,
        IReadOnlyList<FeatureCapabilitySnapshot> capabilities,
        bool agentConnected,
        int maxEvents = TrainerDiagnosticEventBuffer.Capacity)
    {
        ArgumentNullException.ThrowIfNull(features);
        var events = _events.Snapshot(maxEvents);
        var profile = currentTarget is null
            ? null
            : Ra3VersionProfileRegistry.ResolveTargetProfile(currentTarget);
        var target = currentTarget is null
            ? null
            : new DiagnosticTargetSnapshot(
                currentTarget.ProcessName,
                currentTarget.ProcessId,
                currentTarget.FileVersion,
                profile?.Id ?? currentTarget.VersionProfileId ?? "unknown",
                profile?.DisplayName ?? "未知版本",
                TrainerRuntimeKind.Agent,
                currentTarget.ModulePath,
                FormatAddress(currentTarget.ModuleBase));

        var agentApplicable = currentTarget is not null;
        var agentStatus = _agentStatus;
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

        // Agent-owned runtime: the App does not scan or expose implementation signatures.
        var signatures = new SignatureDiagnosticSnapshot(
            Applicable: false,
            EntryCount: 0,
            MatchedCount: 0,
            RequiredCount: 0,
            RequiredMatchedCount: 0,
            _lastRequiredUnresolvedSignatures,
            _lastOptionalUnresolvedSignatures,
            MatchedSymbols: [],
            profile?.SupersededHooks.Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
            "运行时解析由 Agent 内部完成，App 不提供实现签名诊断。");

        var manifestHookCount = Math.Max(manifest?.PatchManifest.Hooks.Count ?? 0, InstalledHookCount);
        var effectiveHookCount = Math.Max(CountEffectiveHooks(manifest, profile), InstalledHookCount);
        var skippedHooks = _lastPatchInstallResult.SkippedHooks
            .Select(skipped => new SkippedHookDiagnosticSnapshot(
                skipped.Name,
                Address: string.Empty,
                EnableFlags: [],
                skipped.Reason))
            .ToArray();
        var patch = new PatchDiagnosticSnapshot(
            manifestHookCount,
            effectiveHookCount,
            InstalledHookCount,
            skippedHooks,
            _lastReportPath,
            !arePatchesInstalled
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

        var stages = BuildStages(target, profile, agent, patch, game);
        var health = CalculateHealth(target, stages, patch, arePatchesInstalled, _overlay);
        var summary = health switch
        {
            TrainerDiagnosticHealth.Offline => "尚未连接游戏进程。",
            TrainerDiagnosticHealth.Error => _lastDiagnosticError ?? "检测到阻止当前会话继续工作的错误。",
            TrainerDiagnosticHealth.Attention => !arePatchesInstalled
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
            _lastReportPath)
        {
            Overlay = _overlay,
            ProductControl = _productControl
        };
    }

    private TrainerDiagnosticHealth CalculateHealth(
        DiagnosticTargetSnapshot? target,
        IReadOnlyList<DiagnosticStageSnapshot> stages,
        PatchDiagnosticSnapshot patch,
        bool arePatchesInstalled,
        OverlayDiagnosticSnapshot overlay)
    {
        if (target is null)
        {
            return TrainerDiagnosticHealth.Offline;
        }

        if (!string.IsNullOrWhiteSpace(_lastDiagnosticError) || stages.Any(stage => stage.State == DiagnosticStageState.Error))
        {
            return TrainerDiagnosticHealth.Error;
        }

        if (!arePatchesInstalled || patch.SkippedHooks.Count > 0 ||
            stages.Any(stage => stage.State == DiagnosticStageState.Warning) ||
            overlay.Applicable &&
            (overlay.Lifecycle != AgentOverlayLifecycle.Ready ||
             overlay.LastError != AgentOverlayError.None ||
             overlay.StatusCode is not AgentStatusCode.Ok))
        {
            return TrainerDiagnosticHealth.Attention;
        }

        return TrainerDiagnosticHealth.Healthy;
    }

    private static string FormatOverlaySummary(AgentOverlayStatusPayload status)
    {
        return status.Lifecycle switch
        {
            AgentOverlayLifecycle.Ready =>
                $"游戏内面板已就绪；帧={status.RenderFrameCount}，点击={status.ButtonClickCount}，Reset={status.DeviceResetCount}。",
            AgentOverlayLifecycle.Disabled => "游戏内面板已停止。",
            AgentOverlayLifecycle.WaitingForFrame => "游戏内面板已安装 Hook，正在等待首个 D3D9 帧。",
            AgentOverlayLifecycle.Installing => "正在安装游戏内面板 Hook。",
            AgentOverlayLifecycle.Stopping => "正在停止游戏内面板。",
            _ => $"游戏内面板不可用：{status.LastError}。"
        };
    }

    private static IReadOnlyList<DiagnosticStageSnapshot> BuildStages(
        DiagnosticTargetSnapshot? target,
        Ra3VersionProfile? profile,
        AgentDiagnosticSnapshot agent,
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

    internal static int CountEffectiveHooks(TrainerManifest? manifest, Ra3VersionProfile? profile)
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
}
