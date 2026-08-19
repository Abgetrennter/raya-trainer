using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Errors;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Diagnostics;

public enum TrainerRuntimeKind
{
    Agent
}

public enum TrainerDiagnosticHealth
{
    Offline,
    Healthy,
    Attention,
    Error
}

public enum DiagnosticStageState
{
    Pending,
    Healthy,
    Warning,
    Error,
    NotApplicable
}

public enum FeatureCapabilityState
{
    Ready,
    Waiting,
    Unavailable
}

public enum DiagnosticEventSeverity
{
    Info,
    Warning,
    Error
}

public sealed record DiagnosticTargetSnapshot(
    string ProcessName,
    int? ProcessId,
    string FileVersion,
    string ProfileId,
    string ProfileName,
    TrainerRuntimeKind Runtime,
    string ModulePath,
    string ModuleBase);

public sealed record AgentDiagnosticSnapshot(
    bool Applicable,
    bool Connected,
    AgentStatusCode? StatusCode,
    ushort? AgentVersion,
    ushort ExpectedAgentVersion,
    string ModuleBase,
    uint NativeRuntimeCapabilities,
    string Summary);

public sealed record SignatureDiagnosticSnapshot(
    bool Applicable,
    uint EntryCount,
    uint MatchedCount,
    int RequiredCount,
    int RequiredMatchedCount,
    IReadOnlyList<string> RequiredUnresolved,
    IReadOnlyList<string> OptionalUnresolved,
    IReadOnlyList<string> MatchedSymbols,
    IReadOnlyList<string> SupersededSymbols,
    string Summary);

public sealed record SkippedHookDiagnosticSnapshot(
    string Name,
    string Address,
    IReadOnlyList<string> EnableFlags,
    string Reason);

public sealed record PatchDiagnosticSnapshot(
    int ManifestHookCount,
    int EffectiveHookCount,
    int InstalledHookCount,
    IReadOnlyList<SkippedHookDiagnosticSnapshot> SkippedHooks,
    string? ReportPath,
    string Summary);

public sealed record OverlayDiagnosticSnapshot(
    bool Applicable,
    AgentStatusCode? StatusCode,
    AgentOverlayLifecycle Lifecycle,
    AgentOverlayFlags Flags,
    uint RenderFrameCount,
    uint ButtonClickCount,
    uint DeviceResetCount,
    AgentOverlayError LastError,
    string Summary)
{
    public static OverlayDiagnosticSnapshot NotApplicable { get; } = new(
        false,
        null,
        AgentOverlayLifecycle.Disabled,
        AgentOverlayFlags.None,
        0,
        0,
        0,
        AgentOverlayError.None,
        "当前目标不启用游戏内面板。");
}

/// <summary>
/// Product Control Plane v1 diagnostics surfaced to the novice-friendly diagnostics page
/// (U3). Built from the negotiated Agent capability/schema plus the on-demand
/// QueryMatchContext / GetDesired responses. Deliberately carries NO internal pointers,
/// no <c>Route</c> strings and no raw wire bytes: only the layered public states and a
/// single Chinese <see cref="StatusSummary"/> line explaining what the user should do
/// next (why a button is disabled / why nothing happened / whether it waits for the next
/// match).
/// </summary>
public sealed record ProductControlDiagnostics
{
    public bool CapabilityNegotiated { get; init; }
    public uint GrantedCapabilities { get; init; }
    public bool MatchContextCapable { get; init; }
    public bool ProductControlPlaneCapable { get; init; }
    public int ContextSchemaVersion { get; init; }
    public int ProductControlSchemaVersion { get; init; }

    public string MatchLifecycle { get; init; } = "Unknown";
    public ulong SnapshotGeneration { get; init; }
    public uint ActivePlayerCount { get; init; }

    public int DesiredTotalCount { get; init; }
    public int DesiredPendingCount { get; init; }
    public int DesiredActiveCount { get; init; }
    public int DesiredDisabledCount { get; init; }
    public int DesiredSupersededCount { get; init; }
    public ulong PolicyRevision { get; init; }

    public ulong LastSubmittedIntentId { get; init; }
    public string LastResultProductId { get; init; } = "";
    public string LastAdmissionState { get; init; } = "";
    public string LastExecutionState { get; init; } = "";
    public string LastEffectState { get; init; } = "";
    public string LastCompensationState { get; init; } = "";
    public string LastErrorCode { get; init; } = "";

    // Human "next step" line for a novice user (why a button is disabled / why nothing
    // happened / whether it waits for the next match). Chinese, novice-friendly.
    public string StatusSummary { get; init; } = "";
}

public sealed record GameRuntimeDiagnosticSnapshot(
    int? GameMode,
    string GameModeName,
    uint? GameThreadTick,
    bool ReadAttempted,
    bool ReadSucceeded,
    string Summary);

public sealed record LaaDiagnosticSnapshot(
    bool? IsLargeAddressAware,  // null = 未检查, true = 已标记, false = 未标记
    string? ModulePath,         // 被检查的 .game 文件路径
    bool HasBackup,             // .Backup 备份文件是否存在
    string Summary);

public sealed record DiagnosticStageSnapshot(
    string Id,
    string Label,
    DiagnosticStageState State,
    string Summary,
    string? RecommendedAction = null);

public sealed record FeatureCapabilitySnapshot
(
    string FeatureId,
    string DisplayName,
    string GroupName,
    FeatureCapabilityState State,
    string ReasonCode,
    string Reason)
{
    /// <summary>
    /// Phase 1.2 Optimization: Single static Compute() replaces 4-layer indirect flow.
    /// All gates evaluated inline (hasTarget, sessionReady, profile support, native hooks,
    /// patch sets, unavailable reasons) — no BuildCapabilityContext/ApplyEvaluation wrappers.
    /// </summary>
    public static FeatureCapabilitySnapshot Compute(
        TrainerFeature feature,
        bool hasTarget,
        bool sessionReady,
        bool patchesInstalled,
        bool backendSupportsDirectGameApi,
        bool directGameApiReady,
        string? unavailableReason,
        Ra3VersionProfile? currentProfile,
        IReadOnlyCollection<uint> installedHooks,
        IReadOnlyCollection<uint> patchSets)
    {
        // Step 1: No target connected → Waiting
        if (!hasTarget)
        {
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",  // Default groupName from TrainerFeatureGroupCatalog lookup in real usage
                FeatureCapabilityState.Waiting,
                "NO_TARGET",
                "未连接到游戏进程。");
        }

        // Step 2: Session not ready → Waiting
        if (!sessionReady)
        {
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",
                FeatureCapabilityState.Waiting,
                "SESSION_NOT_READY",
                "会话初始化未完成。");
        }

        // Step 3: Profile version unsupported
        if (currentProfile is not null && !feature.SupportsProfile(currentProfile.Id))
        {
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",
                FeatureCapabilityState.Unavailable,
                "PROFILE_NOT_SUPPORTED",
                $"不可用：该功能不支持 {currentProfile.DisplayName}。");
        }

        // Step 3b: Feature requires DirectGameApi but the current backend/profile does not provide it.
        // This gate was present in the pre-1.2 TrainerFeatureCapabilityEvaluator (line 45) and must
        // survive the Compute() flattening: Uprising profiles do not support DirectGameApi, so any
        // feature that RequiresDirectGameApi must be Unavailable there with DIRECT_GAME_API_REQUIRED.
        // The Step 6 gate below (DirectGameApi not ready) only fires when the backend *does* support
        // it; without this earlier gate, Uprising would incorrectly pass through to Ready.
        if (feature.RequiresDirectGameApi && !backendSupportsDirectGameApi)
        {
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",
                FeatureCapabilityState.Unavailable,
                "DIRECT_GAME_API_REQUIRED",
                "不可用：该功能需要已启用 Direct GameApi 的 DLL Agent 后端。");
        }

        // Step 4: Native hook dependency (if feature requires it)
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior(feature.RawName);
        if (behavior?.AsNativeToggle() is { } toggle && 
            toggle.HookId.HasValue && 
            !installedHooks.Contains(toggle.HookId.Value))
        {
            // Special handling for FrameRateUnlock60fps - check both Hook and PatchSet together
            if (string.Equals(feature.RawName, TrainerFeatureIds.FrameRateUnlock60fps, StringComparison.OrdinalIgnoreCase) &&
                toggle.PatchSetId.HasValue && !patchSets.Contains(toggle.PatchSetId.Value))
            {
                // Return composite error when both Hook AND PatchSet are missing
                return new(
                    feature.RawName,
                    feature.DisplayName,
                    "功能",
                    FeatureCapabilityState.Unavailable,
                    "FRAMERATE_COMPOSITE_INCOMPLETE",
                    "60fps 解锁需要完整的三重绑定依赖：Hook 41（帧率解锁游戏更新）、PatchSet 1（运行时字节补丁）和 State 20（帧率状态同步）。");
            }
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",
                FeatureCapabilityState.Unavailable,
                "NATIVE_HOOK_MISSING",
                $"{feature.DisplayName}需要的 Hook {toggle.HookId.Value} 尚未安装，请重新连接游戏。");
        }

        // Step 5: Patch set dependency (60fps composite gate)
        if (behavior?.AsNativeToggle() is { PatchSetId: var patchSetId } && 
            patchSetId.HasValue && 
            !patchSets.Contains(patchSetId.Value))
        {
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",
                FeatureCapabilityState.Unavailable,
                "FRAMERATE_COMPOSITE_INCOMPLETE",
                "60fps 解锁需要完整的三重绑定依赖：Hook 41（帧率解锁游戏更新）、PatchSet 1（运行时字节补丁）和 State 20（帧率状态同步）。PatchSet 1 尚未注册。");
        }

        // Step 6: DirectGameApi not ready
        if (!directGameApiReady && backendSupportsDirectGameApi)
        {
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",
                FeatureCapabilityState.Unavailable,
                "DIRECT_GAME_API_NOT_READY",
                "Direct GameApi 尚未就绪。");
        }

        // Step 7: Unavailable reason from MarkFeaturesUnavailable
        if (unavailableReason is not null)
        {
            return new(
                feature.RawName,
                feature.DisplayName,
                "功能",
                FeatureCapabilityState.Unavailable,
                "MANUALLY_DISABLED",
                unavailableReason);
        }

        // Final: All gates passed → Ready
        return new(
            feature.RawName,
            feature.DisplayName,
            "功能",
            FeatureCapabilityState.Ready,
            "READY",
            "就绪");
    }
}

public sealed record TrainerDiagnosticEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    DiagnosticEventSeverity Severity,
    string Code,
    string Message,
    string? Detail = null,
    long? OperationId = null);

/// <summary>
/// Structured last-failure record captured by the diagnostics single entry
/// (<c>TrainerDiagnosticState.RecordFailure</c>, design doc §6.1). Carries the unified
/// vocabulary fields so the diagnostics page, export bundle and developers can locate the
/// failure: stable code, origin domain, stage attribution, correlation id and evidence.
/// <see cref="Domain"/>/<see cref="RetryHint"/> stay null for legacy failures that have not
/// been classified yet — never defaulted to Fault.
/// </summary>
public sealed record TrainerDiagnosticFailure(
    string Code,
    string Message,
    ErrorDomain? Domain = null,
    RetryHint? RetryHint = null,
    string? Stage = null,
    long? OperationId = null,
    IReadOnlyDictionary<string, string>? Evidence = null,
    string? Detail = null);

public sealed record TrainerDiagnosticSnapshot(
    DateTimeOffset CapturedAt,
    TrainerDiagnosticHealth Health,
    string Summary,
    DiagnosticTargetSnapshot? Target,
    AgentDiagnosticSnapshot Agent,
    SignatureDiagnosticSnapshot Signatures,
    PatchDiagnosticSnapshot Patch,
    GameRuntimeDiagnosticSnapshot Game,
    LaaDiagnosticSnapshot Laa,
    IReadOnlyList<DiagnosticStageSnapshot> Stages,
    IReadOnlyList<FeatureCapabilitySnapshot> Capabilities,
    IReadOnlyList<TrainerDiagnosticEvent> RecentEvents,
    string? LastReportPath)
{
    public OverlayDiagnosticSnapshot Overlay { get; init; } = OverlayDiagnosticSnapshot.NotApplicable;

    /// <summary>
    /// Product Control Plane diagnostics (U3). Null when the current target never advertised
    /// the plane / no negotiation has run yet.
    /// </summary>
    public ProductControlDiagnostics? ProductControl { get; init; }

    /// <summary>
    /// Structured last failure captured this attach (design doc §6.3): rides the snapshot so
    /// the export bundle carries the unified code, stage, correlation id and evidence.
    /// </summary>
    public TrainerDiagnosticFailure? LastFailure { get; init; }

    public static TrainerDiagnosticSnapshot Offline { get; } = new(
        DateTimeOffset.MinValue,
        TrainerDiagnosticHealth.Offline,
        "尚未连接游戏进程。",
        null,
        new AgentDiagnosticSnapshot(false, false, null, null, AgentProtocol.Version, "", 0, "当前没有 Agent 会话。"),
        new SignatureDiagnosticSnapshot(false, 0, 0, 0, 0, [], [], [], [], "当前没有签名扫描结果。"),
        new PatchDiagnosticSnapshot(0, 0, 0, [], null, "Patch 尚未安装。"),
        new GameRuntimeDiagnosticSnapshot(null, "未知", null, false, false, "尚未读取游戏循环。"),
        new LaaDiagnosticSnapshot(null, null, false, "尚未检查 LAA 标记。"),
        [],
        [],
        [],
        null);
}
