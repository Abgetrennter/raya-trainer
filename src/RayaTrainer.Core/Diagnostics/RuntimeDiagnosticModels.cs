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
    public static TrainerDiagnosticSnapshot Offline { get; } = new(
        DateTimeOffset.MinValue,
        TrainerDiagnosticHealth.Offline,
        "尚未连接游戏进程。",
        null,
        new AgentDiagnosticSnapshot(false, false, null, null, AgentProtocol.Version, "", 0, "当前没有 Agent 会话。"),
        new SignatureDiagnosticSnapshot(false, 0, 0, 0, 0, [], [], [], "当前没有签名扫描结果。"),
        new PatchDiagnosticSnapshot(0, 0, 0, [], null, "Patch 尚未安装。"),
        new GameRuntimeDiagnosticSnapshot(null, "未知", null, false, false, "尚未读取游戏循环。"),
        new LaaDiagnosticSnapshot(null, null, false, "尚未检查 LAA 标记。"),
        [],
        [],
        [],
        null);
}
