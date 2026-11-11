using System.Text.Json.Serialization;

namespace RayaTrainer.Core.Diagnostics;

public enum GameCrashReportStatus
{
    /// <summary>second-chance 异常已写出完整 Dump，报告待用户处理。</summary>
    Captured,

    /// <summary>游戏退出但未捕获到异常现场（正常退出/自行处理/被强杀），报告只有日志部分。</summary>
    NoCapture,

    /// <summary>采集开启或执行明确失败（身份不符/附加失败/磁盘不足等）。</summary>
    Failed
}

/// <summary>
/// 游戏闪退报告的数据合同（docs/game-crash-diagnostics-design.md §6）。
/// 由 Host 的 GameCrashCaptureSession 在崩溃定案后写入
/// %LocalAppData%\RayaTrainer\CrashReports\&lt;report-id&gt;\report.json；
/// 缺失的身份字段保持 null——报告如实标注未知，不假定一致。
/// </summary>
public sealed record GameCrashReport
{
    public string ReportId { get; init; } = "";

    /// <summary>本次采集会话 ID（等于传给 CrashCapture 的 --session）。</summary>
    public string SessionId { get; init; } = "";

    public GameCrashReportStatus Status { get; init; }

    // —— 时间组（全部 UTC ISO-8601）——
    public string? ConsentedAtUtc { get; init; }
    public string? MonitoringStartedAtUtc { get; init; }
    public string? ExceptionAtUtc { get; init; }
    public string? CompletedAtUtc { get; init; }

    /// <summary>知情同意文案版本（本地追溯；不持久化"今后自动同意"）。</summary>
    public string? ConsentTextVersion { get; init; }

    // —— 异常现场 ——
    public string? ExceptionCode { get; init; }        // "0xC0000005"；null = 无异常
    public uint? ExceptionThreadId { get; init; }
    public string? DumpFileName { get; init; }          // Captured 时有效
    public long? DumpSizeBytes { get; init; }
    public string? FailureReason { get; init; }         // Failed 时的明确原因

    // —— 目标身份 ——
    public int? TargetPid { get; init; }
    public string? TargetImage { get; init; }
    public string? TargetProfileId { get; init; }
    public string? TargetFileVersion { get; init; }

    // —— 版本组：磁盘与实际加载不一致时两者都保留 ——
    public string? TrainerVersion { get; init; }
    public string? CrashCaptureVersion { get; init; }
    public ulong? AgentExpectedBuildId { get; init; }   // 编译期（磁盘 DLL 期望身份）
    public ulong? AgentLoadedBuildId { get; init; }     // 实际注入 Agent 上报；null = 未知

    // —— 崩前冻结诊断（最后有效快照 + 近期事件）——
    public string? FrozenSnapshotCapturedAtUtc { get; init; }

    /// <summary>用户补充的"闪退前做了什么"，可选。</summary>
    public string? UserNote { get; set; }
}

/// <summary>报告目录中随报告保存的冻结诊断（崩前最后有效快照），供恢复与导出。</summary>
public sealed record GameCrashReportBundle
{
    public GameCrashReport Report { get; init; } = new();

    [JsonPropertyName("snapshot")]
    public TrainerDiagnosticSnapshot? FrozenSnapshot { get; init; }

    [JsonPropertyName("events")]
    public IReadOnlyList<TrainerDiagnosticEvent> FrozenEvents { get; init; } = [];

    /// <summary>Patch mismatch 报告原文（已有文本，导出时同样做用户目录替换）。</summary>
    [JsonPropertyName("mismatchText")]
    public string? MismatchText { get; init; }
}
