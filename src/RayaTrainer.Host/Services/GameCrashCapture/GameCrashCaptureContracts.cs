using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Host.Services.GameCrashCapture;

/// <summary>采集会话对外状态（docs/game-crash-diagnostics-design.md §5 生命周期）。</summary>
public enum GameCrashCaptureSessionState
{
    /// <summary>未开启（初始态）。</summary>
    Disabled,

    /// <summary>已同意，等待选定游戏（尚未选定目标时不得显示"正在监测"）。</summary>
    WaitingForTarget,

    /// <summary>正在开启（子进程启动 + 附加握手）。</summary>
    Starting,

    /// <summary>正在监测。</summary>
    Monitoring,

    /// <summary>开启失败（明确原因，可重试；不静默伪装为已受保护）。</summary>
    StartFailed,

    /// <summary>捕获到未处理异常，正在保存现场。</summary>
    SavingReport,

    /// <summary>报告待处理（含 Captured / NoCapture / Failed 三种报告状态）。</summary>
    ReportPending,

    /// <summary>游戏退出且无异常现场，本次授权结束。</summary>
    Ended,

    /// <summary>用户取消或 RT 关闭，安全脱离，本次授权结束。</summary>
    Stopped
}

/// <summary>App 消费面：状态、待处理报告与生命周期操作。实现不依赖 WPF。</summary>
public interface IGameCrashCaptureService
{
    GameCrashCaptureSessionState State { get; }

    /// <summary>当前状态的中文说明（零基础用户文案，随状态更新）。</summary>
    string StatusMessage { get; }

    /// <summary>本地待处理报告上限（设计 §6：最多三份，到顶提示处理旧报告，不自动覆盖）。</summary>
    int PendingReportLimit { get; }

    IReadOnlyList<GameCrashReportBundle> PendingReports { get; }

    event EventHandler? StateChanged;

    /// <summary>
    /// 用户在知情同意弹窗点击"我理解并愿意开启"。同意只对本次采集会话有效，不持久化。
    /// 已连接（有当前目标）则直接进入开启；否则等待目标。返回 false 表示拒绝（如待处理
    /// 报告已达上限、磁盘余量不足），拒绝理由写入 <see cref="StatusMessage"/>。
    /// </summary>
    bool TryBeginAfterConsent(string consentTextVersion, TrainerTarget? currentTarget);

    /// <summary>用户取消监测（安全脱离，目标不受影响）。</summary>
    Task CancelAsync();

    /// <summary>
    /// 游戏会话结束（心跳离线/用户断开/切换目标）：本次授权结束；待处理报告不受影响。
    /// </summary>
    void OnTargetSessionEnded();

    /// <summary>RT 正常关闭：未采集安全脱离；写盘中完成本次采集后退出。</summary>
    Task ShutdownAsync();

    /// <summary>重扫报告目录（RT 重启后的恢复入口）。</summary>
    void RefreshReports();

    /// <summary>导出指定报告为外发 ZIP（异步可取消；取消只删半成品 ZIP，不动原始报告）。</summary>
    Task ExportReportAsync(GameCrashReportBundle report, string zipPath, CancellationToken cancellationToken);

    /// <summary>删除指定报告（仅限报告存储内的该报告目录）。</summary>
    bool DeleteReport(string reportId);
}

/// <summary>TrainerSessionManager 在 Agent 注入前调用的协调点（"连接前开启"时序合同）。</summary>
public interface IGameCrashCaptureAttachCoordinator
{
    /// <summary>在创建注入线程之前完成捕获附加握手；失败不阻断注入，由会话自行降级为开启失败。</summary>
    Task AttachCaptureBeforeInjectionAsync(TrainerTarget target);
}

/// <summary>CrashCapture 子进程启动参数（结构化参数列表，不拼 shell）。</summary>
public sealed record CrashCaptureStartRequest(
    string ExePath,
    int TargetPid,
    ulong TargetCreateTime,
    string TargetImage,
    string OutDir,
    string SessionId,
    string ResultFilePath);

/// <summary>对 CrashCapture 子进程的最小抽象；测试用 fake 替代真实进程。</summary>
public interface ICrashCaptureProcess : IDisposable
{
    /// <summary>stdout 协议行（RTCAP/1 ...）逐行上报。</summary>
    event Action<string>? OutputLine;

    /// <summary>子进程退出（无论是否有 RESULT）。</summary>
    event Action? Exited;

    bool HasExited { get; }

    int ExitCode { get; }

    /// <summary>向 stdin 写一行命令（CANCEL）。管道已关闭时返回 false。</summary>
    bool TrySendLine(string line);

    /// <summary>等待退出；超时返回 false（不杀进程）。</summary>
    Task<bool> WaitForExitAsync(TimeSpan timeout);

    /// <summary>直接终止（仅用于失联兜底与测试；产品路径用 CANCEL 安全脱离）。</summary>
    void Kill();
}

public interface ICrashCaptureProcessLauncher
{
    /// <summary>启动失败（exe 缺失/OS 拒绝）抛异常，消息面向用户可读。</summary>
    ICrashCaptureProcess Start(CrashCaptureStartRequest request);
}
