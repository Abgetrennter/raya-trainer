using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Runtime;

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
    public ulong MapEpoch { get; init; }
    public ulong SnapshotGeneration { get; init; }
    public uint ActivePlayerCount { get; init; }
    public bool SinglePlayerProven { get; init; }

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

public sealed record FeatureCapabilitySnapshot(
    string FeatureId,
    string DisplayName,
    string GroupName,
    FeatureCapabilityState State,
    string ReasonCode,
    string Reason);

public sealed record TrainerDiagnosticEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    DiagnosticEventSeverity Severity,
    string Code,
    string Message,
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
