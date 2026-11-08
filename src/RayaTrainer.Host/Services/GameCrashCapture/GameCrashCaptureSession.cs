using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Host.Services.GameCrashCapture;

/// <summary>
/// 游戏闪退采集会话（docs/game-crash-diagnostics-design.md §5）：拥有 CrashCapture 子进程、
/// 崩前冻结快照、报告存储与生命周期状态机；不依赖 WPF。目标身份在启动捕获前现查
/// （精确 FILETIME），并由 CrashCapture 二次校验，防 PID 重用抓错进程。
/// 状态回调可能来自线程池——消费方（App）须自行调度回 UI 线程。
/// </summary>
public sealed class GameCrashCaptureSession :
    IGameCrashCaptureService, IGameCrashCaptureAttachCoordinator, IDisposable
{
    public const int DefaultPendingReportLimit = 3;

    private const string ConsentTextVersionCurrent = "2026-10-08.1";

    private static readonly TimeSpan AttachHandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CancelExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LateResultTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ShutdownDumpGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SnapshotFreezeInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions OutcomeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ITrainerDiagnosticsSource _source;
    private readonly Func<IReadOnlyList<TrainerFeature>> _featuresProvider;
    private readonly ICrashCaptureProcessLauncher _launcher;
    private readonly CrashReportStore _store;
    private readonly Func<string> _exePathProvider;
    private readonly Func<int, (ulong CreateTimeUtc, string ImagePath)?> _identityProvider;
    private readonly Func<int, ulong?> _commitChargeProvider;

    private readonly object _stateLock = new();
    private GameCrashCaptureSessionState _state = GameCrashCaptureSessionState.Disabled;
    private string _statusMessage = "游戏闪退诊断未开启。";
    private readonly List<GameCrashReportBundle> _pendingReports = [];

    private ICrashCaptureProcess? _process;
    private TaskCompletionSource _monitoringReached = CreateCanceledTcs();
    private CancellationTokenSource? _snapshotLoopCancellation;

    // 当前采集会话身份与崩前冻结诊断（最后有效快照；崩溃后不再刷新）。
    private string _reportId = "";
    private string? _consentedAtUtc;
    private string? _consentTextVersion;
    private string? _captureVersion;
    private int _targetPid;
    private string _targetImage = "";
    private TrainerTarget? _sessionTarget;
    private TrainerDiagnosticSnapshot? _frozenSnapshot;
    private IReadOnlyList<TrainerDiagnosticEvent> _frozenEvents = [];

    /// <summary>CrashCapture 结果文件结构（rtcap/1，由辅助程序原子写出）。</summary>
    private sealed record CrashCaptureOutcomeFile(
        string Protocol,
        string Session,
        string Status,
        int Target_Pid,
        string? Exception_Code,
        uint? Exception_Thread,
        string? Dump_Path,
        long? Dump_Size,
        string? Failure_Reason,
        string? Started_At_Utc,
        string? Exception_At_Utc,
        string? Completed_At_Utc);

    public GameCrashCaptureSession(
        ITrainerDiagnosticsSource source,
        Func<IReadOnlyList<TrainerFeature>> featuresProvider,
        ICrashCaptureProcessLauncher? launcher = null,
        CrashReportStore? store = null,
        Func<string>? exePathProvider = null,
        Func<int, (ulong CreateTimeUtc, string ImagePath)?>? identityProvider = null,
        Func<int, ulong?>? commitChargeProvider = null)
    {
        _source = source;
        _featuresProvider = featuresProvider;
        _launcher = launcher ?? new CrashCaptureProcessLauncher();
        _store = store ?? new CrashReportStore();
        _exePathProvider = exePathProvider ?? ResolveDefaultCrashCaptureExePath;
        _identityProvider = identityProvider ?? DefaultIdentity;
        _commitChargeProvider = commitChargeProvider ?? DefaultCommitCharge;
        RefreshReports();
    }

    public event EventHandler? StateChanged;

    public GameCrashCaptureSessionState State
    {
        get { lock (_stateLock) return _state; }
    }

    public string StatusMessage
    {
        get { lock (_stateLock) return _statusMessage; }
    }

    public int PendingReportLimit => DefaultPendingReportLimit;

    public IReadOnlyList<GameCrashReportBundle> PendingReports
    {
        get { lock (_stateLock) return _pendingReports.ToArray(); }
    }

    public bool TryBeginAfterConsent(string consentTextVersion, TrainerTarget? currentTarget)
    {
        lock (_stateLock)
        {
            if (_state is GameCrashCaptureSessionState.Starting or GameCrashCaptureSessionState.Monitoring
                or GameCrashCaptureSessionState.SavingReport)
            {
                _statusMessage = "诊断已在进行中。";
                return false;
            }

            if (_state == GameCrashCaptureSessionState.ReportPending)
            {
                _statusMessage = "已有一份待处理报告。请先导出或删除当前报告，再开启新的诊断。";
                return false;
            }

            if (_pendingReports.Count >= DefaultPendingReportLimit)
            {
                _statusMessage = $"本地待处理报告已达上限（{DefaultPendingReportLimit} 份）。请先处理旧报告，不会自动覆盖。";
                return false;
            }
        }

        _consentedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        _consentTextVersion = consentTextVersion;

        if (currentTarget?.ProcessId is { } pid)
        {
            _ = StartCaptureAsync(currentTarget, pid);
        }
        else
        {
            SetState(GameCrashCaptureSessionState.WaitingForTarget, "已同意开启。等待选择游戏后自动开始（仅覆盖开启后的故障）。");
        }

        return true;
    }

    public async Task AttachCaptureBeforeInjectionAsync(TrainerTarget target)
    {
        if (target.ProcessId is not { } targetPid)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_state is not (GameCrashCaptureSessionState.WaitingForTarget
                or GameCrashCaptureSessionState.Starting
                or GameCrashCaptureSessionState.Monitoring))
            {
                return; // 未开启或已结束：协调点零动作。
            }

            if (_state is GameCrashCaptureSessionState.Starting or GameCrashCaptureSessionState.Monitoring
                && _targetPid == targetPid)
            {
                return; // 已在监测同一目标（连接后开启→再附加的场景）。
            }
        }

        // 切换到新目标：先安全脱离旧采集（授权结束），再对新目标开启。
        if (_process is not null)
        {
            await CancelCurrentProcessAsync().ConfigureAwait(false);
        }

        _ = StartCaptureAsync(target, targetPid);
        // 给附加握手一个有限等待：注入必须发生在捕获附加完成之后（设计 §3）。
        using var timeout = new CancellationTokenSource(AttachHandshakeTimeout);
        try
        {
            await _monitoringReached.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 开启失败已由会话状态承载；不阻断注入。
        }
    }

    public async Task CancelAsync()
    {
        lock (_stateLock)
        {
            if (_state == GameCrashCaptureSessionState.WaitingForTarget)
            {
                SetStateLocked(GameCrashCaptureSessionState.Disabled, "已取消，本次开启授权结束。");
                return;
            }
        }

        await CancelCurrentProcessAsync().ConfigureAwait(false);
    }

    public void OnTargetSessionEnded()
    {
        Task.Run(async () =>
        {
            lock (_stateLock)
            {
                if (_state is not (GameCrashCaptureSessionState.Monitoring
                    or GameCrashCaptureSessionState.SavingReport))
                {
                    if (_state == GameCrashCaptureSessionState.WaitingForTarget)
                    {
                        SetStateLocked(GameCrashCaptureSessionState.Disabled, "游戏会话已结束，本次开启授权结束。");
                    }
                    return;
                }
            }

            // 目标离线：CrashCapture 的 EXIT_PROCESS→RESULT 通常先于心跳到达；给迟到结果一个窗口。
            ICrashCaptureProcess? process;
            lock (_stateLock)
            {
                process = _process;
            }

            if (process is not null)
            {
                await process.WaitForExitAsync(LateResultTimeout).ConfigureAwait(false);
            }

            lock (_stateLock)
            {
                if (_state is GameCrashCaptureSessionState.Monitoring or GameCrashCaptureSessionState.SavingReport)
                {
                    // 无异常现场：保留日志型报告（设计 §3"未捕获到完整现场，允许导出已有日志"）。
                    FinalizeAsNoCaptureLocked("游戏已退出，未捕获到完整现场。");
                    SetStateLocked(GameCrashCaptureSessionState.ReportPending, _statusMessage);
                }
            }
        });
    }

    public async Task ShutdownAsync()
    {
        lock (_stateLock)
        {
            if (_state is GameCrashCaptureSessionState.Disabled or GameCrashCaptureSessionState.WaitingForTarget
                or GameCrashCaptureSessionState.Ended or GameCrashCaptureSessionState.Stopped
                or GameCrashCaptureSessionState.StartFailed)
            {
                if (_state == GameCrashCaptureSessionState.WaitingForTarget)
                {
                    SetStateLocked(GameCrashCaptureSessionState.Disabled, "RT 已关闭，本次开启授权结束。");
                }
                return;
            }
        }

        if (_state == GameCrashCaptureSessionState.SavingReport)
        {
            // 写盘中：完成当前一次采集再退出（设计 §5）。
            if (_process is not null)
            {
                await _process.WaitForExitAsync(ShutdownDumpGrace).ConfigureAwait(false);
            }
        }
        else
        {
            await CancelCurrentProcessAsync().ConfigureAwait(false);
        }
    }

    public void RefreshReports()
    {
        var bundles = _store.ListReports();
        lock (_stateLock)
        {
            _pendingReports.Clear();
            _pendingReports.AddRange(RecoverInterruptedReports(bundles));
        }

        RaiseStateChanged();
    }

    public Task ExportReportAsync(GameCrashReportBundle report, string zipPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        return CrashReportZipExporter.ExportAsync(
            zipPath, report, _store.GetReportDirectory(report.Report.ReportId), cancellationToken);
    }

    public bool DeleteReport(string reportId)
    {
        var deleted = _store.DeleteReport(reportId);
        if (deleted)
        {
            RefreshReports();
        }

        return deleted;
    }

    public void Dispose()
    {
        _snapshotLoopCancellation?.Cancel();
        _process?.Dispose();
    }

    // ---------------------------------------------------------------- 内部

    private async Task StartCaptureAsync(TrainerTarget target, int targetPid)
    {
        SetState(GameCrashCaptureSessionState.Starting, "正在开启游戏闪退诊断…");

        var identity = _identityProvider(targetPid);
        if (identity is null)
        {
            SetState(GameCrashCaptureSessionState.StartFailed, "诊断未开启：无法确认目标进程身份（进程可能已退出）。");
            return;
        }

        var (createTimeUtc, imagePath) = identity.Value;

        if (!CheckDiskHeadroom(targetPid, out var headroomMessage))
        {
            SetState(GameCrashCaptureSessionState.StartFailed, $"诊断未开启：{headroomMessage}");
            return;
        }

        _reportId = NewReportId();
        _targetPid = targetPid;
        _targetImage = imagePath;
        _sessionTarget = target;
        _consentedAtUtc ??= DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        TakeFrozenSnapshot();

        var reportDir = _store.GetReportDirectory(_reportId);
        string resultFilePath;
        ICrashCaptureProcess process;
        try
        {
            Directory.CreateDirectory(reportDir);
            resultFilePath = Path.Combine(reportDir, "capture-result.json");

            // 初始 bundle 立即落盘：RT 意外退出后仍可恢复（恢复路径可结合 result.json 升级终态）。
            _store.CreateReport(BuildBundle(
                new GameCrashReport
                {
                    ReportId = _reportId,
                    SessionId = _reportId,
                    Status = GameCrashReportStatus.Failed,
                    FailureReason = "capture-session-interrupted",
                    TargetPid = targetPid,
                    TargetImage = imagePath,
                    TargetProfileId = target.VersionProfileId,
                    TargetFileVersion = target.FileVersion,
                    ConsentedAtUtc = _consentedAtUtc,
                    ConsentTextVersion = _consentTextVersion,
                    MonitoringStartedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    TrainerVersion = ResolveTrainerVersion(),
                    AgentExpectedBuildId = AgentBuildIdentity.BuildId,
                }));

            process = _launcher.Start(new CrashCaptureStartRequest(
                _exePathProvider(), targetPid, createTimeUtc, imagePath, reportDir, _reportId, resultFilePath));
        }
        catch (Exception ex)
        {
            SetState(GameCrashCaptureSessionState.StartFailed, $"诊断未开启：{ex.Message}。可重试或继续正常使用。");
            return;
        }

        lock (_stateLock)
        {
            _process?.Dispose();
            _process = process;
            _monitoringReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        process.OutputLine += OnProcessLine;
        process.Exited += OnProcessExited;

        try
        {
            using var timeout = new CancellationTokenSource(AttachHandshakeTimeout);
            await _monitoringReached.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_stateLock)
            {
                if (_state == GameCrashCaptureSessionState.Starting && ReferenceEquals(_process, process))
                {
                    SetStateLocked(GameCrashCaptureSessionState.StartFailed,
                        "诊断未开启：捕获组件附加握手超时。可重试或继续正常使用。");
                    DetachProcessLocked(process);
                }
            }

            return;
        }

        lock (_stateLock)
        {
            if (ReferenceEquals(_process, process))
            {
                SetStateLocked(GameCrashCaptureSessionState.Monitoring,
                    "正在监测游戏闪退。正常玩游戏即可；游戏崩溃时将自动保存现场。");
            }
        }

        StartSnapshotFreezeLoop();
    }

    private void OnProcessLine(string line)
    {
        if (!line.StartsWith("RTCAP/1 ", StringComparison.Ordinal))
        {
            return;
        }

        var payload = line["RTCAP/1 ".Length..];
        if (payload.StartsWith("HELLO ", StringComparison.Ordinal))
        {
            var version = ExtractKeyValue(payload, "version=");
            if (version is not null)
            {
                _captureVersion = version;
            }
        }
        else if (payload.StartsWith("MONITORING", StringComparison.Ordinal))
        {
            _monitoringReached.TrySetResult();
        }
        else if (payload.StartsWith("DUMPING", StringComparison.Ordinal))
        {
            lock (_stateLock)
            {
                if (_state == GameCrashCaptureSessionState.Monitoring)
                {
                    SetStateLocked(GameCrashCaptureSessionState.SavingReport, "捕获到游戏崩溃，正在保存现场…");
                }
            }
        }
        else if (payload.StartsWith("RESULT status=", StringComparison.Ordinal))
        {
            HandleResultLine(payload);
        }
        else if (payload.StartsWith("FAILED reason=", StringComparison.Ordinal))
        {
            var reason = payload["FAILED reason=".Length..];
            lock (_stateLock)
            {
                if (_state is GameCrashCaptureSessionState.Starting or GameCrashCaptureSessionState.Monitoring)
                {
                    SetStateLocked(GameCrashCaptureSessionState.StartFailed, $"诊断未开启：捕获组件报告失败（{reason}）。");
                }
            }
        }
    }

    private void HandleResultLine(string payload)
    {
        var status = ExtractKeyValue(payload, "status=");
        switch (status)
        {
            case "cancelled":
                lock (_stateLock)
                {
                    SetStateLocked(GameCrashCaptureSessionState.Stopped, "已停止监测，本次授权结束。");
                }
                break;

            case "captured":
            case "exited-no-capture":
            case "failed":
                FinalizeFromOutcomeFile(status);
                break;
        }
    }

    private void FinalizeFromOutcomeFile(string captureStatus)
    {
        var reportDir = _store.GetReportDirectory(_reportId);
        var resultPath = Path.Combine(reportDir, "capture-result.json");
        CrashCaptureOutcomeFile? outcome = null;
        for (var attempt = 0; attempt < 50 && outcome is null; attempt++)
        {
            try
            {
                if (File.Exists(resultPath))
                {
                    outcome = JsonSerializer.Deserialize<CrashCaptureOutcomeFile>(
                        File.ReadAllText(resultPath), OutcomeJsonOptions);
                }
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
                outcome = new CrashCaptureOutcomeFile("rtcap/1", _reportId, "failed", _targetPid,
                    null, null, null, null, "capture-result-json-corrupt", null, null, null);
            }

            if (outcome is null)
            {
                Thread.Sleep(100);
            }
        }

        lock (_stateLock)
        {
            var report = BuildReportFromOutcome(captureStatus, outcome);
            _store.SaveBundle(BuildBundle(report));
            _pendingReports.Insert(0, BuildBundle(report));
            var message = report.Status switch
            {
                GameCrashReportStatus.Captured => "已保存游戏闪退报告，尚未发送。可导出、删除或稍后处理。",
                GameCrashReportStatus.NoCapture => "游戏已退出，未捕获到完整现场。可导出已有日志。",
                _ => $"采集失败：{report.FailureReason ?? "未知原因"}。可导出已有日志。",
            };
            SetStateLocked(GameCrashCaptureSessionState.ReportPending, message);
        }
    }

    private void FinalizeAsNoCaptureLocked(string message)
    {
        if (_reportId.Length == 0)
        {
            return;
        }

        var report = BuildReportFromOutcome("exited-no-capture", null);
        _store.SaveBundle(BuildBundle(report));
        _pendingReports.Insert(0, BuildBundle(report));
        _statusMessage = message;
    }

    private GameCrashReport BuildReportFromOutcome(string captureStatus, CrashCaptureOutcomeFile? outcome)
    {
        var reportDir = _store.GetReportDirectory(_reportId);
        var dumpFile = CrashReportStore.FindDumpFile(reportDir);
        var status = captureStatus switch
        {
            "captured" => GameCrashReportStatus.Captured,
            "exited-no-capture" => GameCrashReportStatus.NoCapture,
            _ => GameCrashReportStatus.Failed
        };

        var target = _sessionTarget;
        var dumpName = dumpFile is null ? null : Path.GetFileName(dumpFile);
        if (captureStatus == "captured" && dumpFile is null)
        {
            // 捕获声明成功但转正 Dump 缺失：如实降级为失败，不产出"无现场的 Captured 报告"。
            captureStatus = "failed";
            status = GameCrashReportStatus.Failed;
        }

        return new GameCrashReport
        {
            ReportId = _reportId,
            SessionId = _reportId,
            Status = status,
            ConsentedAtUtc = _consentedAtUtc,
            ConsentTextVersion = _consentTextVersion,
            MonitoringStartedAtUtc = outcome?.Started_At_Utc,
            ExceptionAtUtc = outcome?.Exception_At_Utc,
            CompletedAtUtc = outcome?.Completed_At_Utc ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ExceptionCode = status == GameCrashReportStatus.Captured ? outcome?.Exception_Code : null,
            ExceptionThreadId = status == GameCrashReportStatus.Captured ? outcome?.Exception_Thread : null,
            DumpFileName = status == GameCrashReportStatus.Captured ? dumpName : null,
            DumpSizeBytes = status == GameCrashReportStatus.Captured
                ? (outcome?.Dump_Size ?? (dumpFile is null ? null : new FileInfo(dumpFile).Length))
                : null,
            FailureReason = status == GameCrashReportStatus.Failed ? outcome?.Failure_Reason : null,
            TargetPid = _targetPid,
            TargetImage = _targetImage,
            TargetProfileId = target?.VersionProfileId,
            TargetFileVersion = target?.FileVersion,
            TrainerVersion = ResolveTrainerVersion(),
            CrashCaptureVersion = _captureVersion,
            AgentExpectedBuildId = AgentBuildIdentity.BuildId,
            AgentLoadedBuildId = null // 已加载 Agent 身份若可得由诊断快照承载；此处不假定一致。
        };
    }

    private GameCrashReportBundle BuildBundle(GameCrashReport report)
    {
        var mismatchText = ReadMismatchText();
        return new GameCrashReportBundle
        {
            Report = report,
            FrozenSnapshot = _frozenSnapshot,
            FrozenEvents = _frozenEvents,
            MismatchText = mismatchText,
        };
    }

    private string? ReadMismatchText()
    {
        var path = _frozenSnapshot?.Patch.ReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void OnProcessExited()
    {
        lock (_stateLock)
        {
            switch (_state)
            {
                case GameCrashCaptureSessionState.Starting:
                    SetStateLocked(GameCrashCaptureSessionState.StartFailed, "诊断未开启：捕获组件意外退出。可重试或继续正常使用。");
                    _monitoringReached.TrySetCanceled();
                    break;
                case GameCrashCaptureSessionState.Monitoring:
                    // 子进程消失（被强杀等）：目标仍受保护语义终止；不伪造报告。
                    SetStateLocked(GameCrashCaptureSessionState.Stopped, "捕获组件已退出，诊断未继续。可重新开启；已有报告不受影响。");
                    break;
            }
        }
    }

    private async Task CancelCurrentProcessAsync()
    {
        ICrashCaptureProcess? process;
        lock (_stateLock)
        {
            process = _process;
        }

        if (process is null)
        {
            return;
        }

        process.TrySendLine("CANCEL");
        if (!await process.WaitForExitAsync(CancelExitTimeout).ConfigureAwait(false))
        {
            // 不能用杀进程当作取消（设计 §5）；进程通常会在下一轮事件轮询内自行退出。
        }

        lock (_stateLock)
        {
            if (ReferenceEquals(_process, process))
            {
                if (_state is GameCrashCaptureSessionState.Starting
                    or GameCrashCaptureSessionState.Monitoring
                    or GameCrashCaptureSessionState.SavingReport)
                {
                    // RESULT 行若已处理则状态已是终态；否则按安全脱离收口。
                    SetStateLocked(GameCrashCaptureSessionState.Stopped, "已停止监测，本次授权结束。");
                }

                DetachProcessLocked(process);
            }
        }
    }

    private void StartSnapshotFreezeLoop()
    {
        _snapshotLoopCancellation?.Cancel();
        _snapshotLoopCancellation = new CancellationTokenSource();
        var token = _snapshotLoopCancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested
                       && _state is GameCrashCaptureSessionState.Monitoring
                           or GameCrashCaptureSessionState.SavingReport)
                {
                    await Task.Delay(SnapshotFreezeInterval, token).ConfigureAwait(false);
                    if (_state is GameCrashCaptureSessionState.Monitoring
                        or GameCrashCaptureSessionState.SavingReport)
                    {
                        TakeFrozenSnapshot();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void TakeFrozenSnapshot()
    {
        try
        {
            var features = _featuresProvider();
            var snapshot = _source.GetDiagnosticSnapshot(features);
            var events = _source.DiagnosticEvents;
            lock (_stateLock)
            {
                _frozenSnapshot = snapshot;
                _frozenEvents = events.ToArray();
            }
        }
        catch
        {
            // 快照失败不阻断采集；报告内按缺失处理。
        }
    }

    private bool CheckDiskHeadroom(int targetPid, out string message)
    {
        message = "";
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_store.RootDirectory)
                ?? Path.GetPathRoot(AppContext.BaseDirectory)!);
            var commit = _commitChargeProvider(targetPid);
            var required = unchecked((long)(commit ?? 0UL)) + (1L << 30); // 提交内存估计 + 1 GiB 余量（设计 §6）
            if (drive.AvailableFreeSpace < required)
            {
                message = $"磁盘剩余空间不足（需要约 {required / (1L << 30)} GiB，剩余 {drive.AvailableFreeSpace / (1L << 30)} GiB）。";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // 预检失败不阻断开启：实际写入仍会走明确失败路径。
        }

        return true;
    }

    private IReadOnlyList<GameCrashReportBundle> RecoverInterruptedReports(IReadOnlyList<GameCrashReportBundle> bundles)
    {
        var recovered = new List<GameCrashReportBundle>();
        foreach (var bundle in bundles)
        {
            if (bundle.Report.FailureReason is not ("capture-session-interrupted" or "report-incomplete"))
            {
                recovered.Add(bundle);
                continue;
            }

            // RT 崩溃恢复：CrashCapture 结果文件在而报告未终态 → 按结果升级为终态报告。
            var dir = _store.GetReportDirectory(bundle.Report.ReportId);
            var resultPath = Path.Combine(dir, "capture-result.json");
            try
            {
                if (!File.Exists(resultPath))
                {
                    recovered.Add(bundle);
                    continue;
                }

                var outcome = JsonSerializer.Deserialize<CrashCaptureOutcomeFile>(
                    File.ReadAllText(resultPath), OutcomeJsonOptions);
                if (outcome is null)
                {
                    recovered.Add(bundle);
                    continue;
                }

                var dumpFile = CrashReportStore.FindDumpFile(dir);
                var status = outcome.Status switch
                {
                    "captured" => GameCrashReportStatus.Captured,
                    "exited-no-capture" => GameCrashReportStatus.NoCapture,
                    _ => GameCrashReportStatus.Failed
                };
                var upgraded = bundle.Report with
                {
                    Status = status,
                    ExceptionCode = status == GameCrashReportStatus.Captured ? outcome.Exception_Code : null,
                    ExceptionThreadId = status == GameCrashReportStatus.Captured ? outcome.Exception_Thread : null,
                    DumpFileName = status == GameCrashReportStatus.Captured && dumpFile is not null
                        ? Path.GetFileName(dumpFile)
                        : null,
                    DumpSizeBytes = status == GameCrashReportStatus.Captured && dumpFile is not null
                        ? new FileInfo(dumpFile).Length
                        : null,
                    FailureReason = status == GameCrashReportStatus.Failed ? outcome.Failure_Reason : null,
                    CompletedAtUtc = outcome.Completed_At_Utc,
                    ExceptionAtUtc = outcome.Exception_At_Utc,
                };
                _store.SaveBundle(bundle with { Report = upgraded });
                recovered.Add(bundle with { Report = upgraded });
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                recovered.Add(bundle);
            }
        }

        return recovered;
    }

    private void DetachProcessLocked(ICrashCaptureProcess process)
    {
        if (!ReferenceEquals(_process, process))
        {
            return;
        }

        process.OutputLine -= OnProcessLine;
        process.Exited -= OnProcessExited;
        process.Dispose();
        _process = null;
        _snapshotLoopCancellation?.Cancel();
    }

    private void SetState(GameCrashCaptureSessionState state, string message)
    {
        lock (_stateLock)
        {
            SetStateLocked(state, message);
        }
    }

    private void SetStateLocked(GameCrashCaptureSessionState state, string message)
    {
        _state = state;
        _statusMessage = message;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static TaskCompletionSource CreateCanceledTcs()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.TrySetCanceled();
        return tcs;
    }

    private static string? ExtractKeyValue(string payload, string key)
    {
        var index = payload.IndexOf(key, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + key.Length;
        var end = payload.IndexOf(' ', start);
        return end < 0 ? payload[start..] : payload[start..end];
    }

    private static (ulong CreateTimeUtc, string ImagePath)? DefaultIdentity(int pid)
    {
        return NativeProcessQueries.TryGetProcessIdentity(pid, out var createTime, out var image)
            ? (createTime, image)
            : null;
    }

    private static ulong? DefaultCommitCharge(int pid)
    {
        return NativeProcessQueries.TryGetCommitChargeBytes(pid, out var commit) ? commit : null;
    }

    private static string ResolveTrainerVersion()
    {
        return typeof(GameCrashCaptureSession).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static string NewReportId()
    {
        return DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + Random.Shared.Next(0x1000000, 0xFFFFFFF).ToString("x", CultureInfo.InvariantCulture);
    }

    private static string ResolveDefaultCrashCaptureExePath()
    {
        return ResolveDefaultCrashCaptureExePath(AppContext.BaseDirectory);
    }

    internal static string ResolveDefaultCrashCaptureExePath(string baseDirectory)
    {
        var appLocalPath = Path.Combine(baseDirectory, "RayaTrainer.CrashCapture.exe");

        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "RayaTrainer.sln");
            if (File.Exists(solutionPath))
            {
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
                    "RayaTrainer.CrashCapture.exe");
                return new[] { appLocalPath, configurationArtifact }
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault() ?? appLocalPath;
            }

            directory = directory.Parent;
        }

        return appLocalPath;
    }
}
