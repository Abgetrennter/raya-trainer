using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RayaTrainer.App.Views;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Host.Services.GameCrashCapture;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 诊断页"游戏闪退诊断"卡片的 ViewModel（docs/game-crash-diagnostics-design.md §3 用户流程）。
/// 只做状态投影与用户操作转发；状态机真相源是 Host 的 GameCrashCaptureSession。
/// </summary>
public sealed class CrashDiagnosticsViewModel : ViewModelBase, IDisposable
{
    /// <summary>同意文案版本：随文案实质变化递增，仅用于本地报告追溯。</summary>
    public const string ConsentTextVersion = "2026-10-08.1";

    private readonly IGameCrashCaptureService _service;
    private readonly Action<string> _setStatus;
    private readonly Func<TrainerTarget?> _currentTargetProvider;

    private bool _isBusy;
    private string _stateLabel = "";
    private string _statusMessage = "";
    private bool _canEnable;
    private bool _canCancel;
    private bool _hasPendingReport;
    private string _reportSummary = "";
    private string _userNote = "";
    private readonly HashSet<string> _exportRemindedReportIds = [];

    public CrashDiagnosticsViewModel(
        IGameCrashCaptureService service,
        Action<string> setStatus,
        Func<TrainerTarget?> currentTargetProvider)
    {
        _service = service;
        _setStatus = setStatus;
        _currentTargetProvider = currentTargetProvider;
        EnableCommand = new RelayCommand(Enable, () => !_isBusy && CanEnable);
        CancelCommand = new RelayCommand(() => _ = CancelAsync(), () => !_isBusy && CanCancel);
        ExportReportCommand = new RelayCommand(() => _ = ExportReportAsync(), () => !_isBusy && HasPendingReport);
        DeleteReportCommand = new RelayCommand(() => _ = DeleteReportAsync(), () => !_isBusy && HasPendingReport);
        DismissReportCommand = new RelayCommand(DismissReport, () => HasPendingReport);
        _service.StateChanged += OnStateChanged;
        ApplyState();
    }

    public RelayCommand EnableCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ExportReportCommand { get; }

    public RelayCommand DeleteReportCommand { get; }

    public RelayCommand DismissReportCommand { get; }

    public string StateLabel
    {
        get => _stateLabel;
        private set
        {
            _stateLabel = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool CanEnable
    {
        get => _canEnable;
        private set
        {
            _canEnable = value;
            OnPropertyChanged();
        }
    }

    public bool CanCancel
    {
        get => _canCancel;
        private set
        {
            _canCancel = value;
            OnPropertyChanged();
        }
    }

    public bool HasPendingReport
    {
        get => _hasPendingReport;
        private set
        {
            _hasPendingReport = value;
            OnPropertyChanged();
        }
    }

    public string ReportSummary
    {
        get => _reportSummary;
        private set
        {
            _reportSummary = value;
            OnPropertyChanged();
        }
    }

    /// <summary>用户补充的"闪退前做了什么"（可选；导出时随报告写入）。</summary>
    public string UserNote
    {
        get => _userNote;
        set
        {
            _userNote = value;
            OnPropertyChanged();
        }
    }

    public void Dispose()
    {
        _service.StateChanged -= OnStateChanged;
    }

    private void Enable()
    {
        var consent = new CrashConsentText
        {
            Title = "开启游戏闪退诊断",
            Heading = "开启游戏闪退诊断？",
            Body = "开启后，RT 会在游戏崩溃时尝试保存游戏进程的内存现场（Dump），帮助定位闪退原因。\n\n"
                   + "文件可能包含用户名、文件路径，以及当时游戏进程内存中的其他个人信息。我们无法完整识别或去除这些内容，不能保证文件已匿名化。\n\n"
                   + "文件仅保存在你的电脑上，不会自动发送。生成后，你可以决定是否私下发送给开发者，或直接删除。文件可能较大，保存期间游戏可能短暂停顿。",
            Detail = "本次开启只针对一个游戏进程，游戏结束后自动停止。不参与不影响正常使用。",
            ConfirmText = "我理解并愿意开启",
            CancelText = "暂不开启"
        };

        if (ShowConsent(consent) != true)
        {
            return;
        }

        var accepted = _service.TryBeginAfterConsent(ConsentTextVersion, _currentTargetProvider());
        if (!accepted)
        {
            _setStatus(_service.StatusMessage);
        }
    }

    private async Task CancelAsync()
    {
        SetBusy(true);
        try
        {
            await _service.CancelAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExportReportAsync()
    {
        var report = _service.PendingReports.FirstOrDefault();
        if (report is null)
        {
            return;
        }

        // 用户补充说明（可选）随导出写入报告。
        report.Report.UserNote = string.IsNullOrWhiteSpace(UserNote) ? null : UserNote.Trim();

        if (!_exportRemindedReportIds.Contains(report.Report.ReportId))
        {
            var reminder = new CrashConsentText
            {
                Title = "导出闪退报告",
                Heading = "导出前请确认",
                Body = "报告包含游戏内存现场，可能保留无法完全去除的个人信息。只有在你愿意分享这些内容时，才将报告私发给开发者。",
                Detail = "请勿上传到公开评论区或公开 Issue。",
                ConfirmText = "继续导出",
                CancelText = "取消"
            };
            if (ShowConsent(reminder) != true)
            {
                return; // 用户取消：不生成外发 ZIP。
            }

            _exportRemindedReportIds.Add(report.Report.ReportId);
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"ra3-crash-{report.Report.ReportId}.zip",
            Filter = "闪退报告 (*.zip)|*.zip",
            Title = "导出游戏闪退报告"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SetBusy(true);
        _setStatus("正在导出闪退报告…");
        try
        {
            await _service.ExportReportAsync(report, dialog.FileName, CancellationToken.None);
            _setStatus($"闪退报告已导出：{dialog.FileName}");
            OpenInExplorerAndSelect(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _setStatus($"导出失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteReportAsync()
    {
        var report = _service.PendingReports.FirstOrDefault();
        if (report is null)
        {
            return;
        }

        var confirm = new CrashConsentText
        {
            Title = "删除闪退报告",
            Heading = "删除这份闪退报告？",
            Body = $"报告 {report.Report.ReportId} 将从本机永久删除，且无法重新生成（内存现场只有一份）。",
            Detail = "删除后不影响正常使用，也不影响之后再次开启诊断。",
            ConfirmText = "删除",
            CancelText = "保留"
        };
        if (ShowConsent(confirm) != true)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await Task.Run(() => _service.DeleteReport(report.Report.ReportId));
            _setStatus("闪退报告已删除。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DismissReport()
    {
        _setStatus("报告保留在本机，可稍后在诊断页处理。");
    }

    private bool? ShowConsent(CrashConsentText text)
    {
        var window = new CrashDiagnosticsConsentWindow(text)
        {
            Owner = Application.Current?.MainWindow
        };
        window.ShowDialog();
        return window.Confirmed ? true : null;
    }

    private static void OpenInExplorerAndSelect(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            // 打开文件夹失败不覆盖导出成功状态；用户可手动定位。
        }
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        // 状态事件可能来自线程池（协议行回调）；切回 UI 线程投影。
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyState();
        }
        else
        {
            dispatcher.BeginInvoke(ApplyState);
        }
    }

    private void ApplyState()
    {
        StateLabel = _service.State switch
        {
            GameCrashCaptureSessionState.Disabled => "未开启",
            GameCrashCaptureSessionState.WaitingForTarget => "已同意，等待选择游戏",
            GameCrashCaptureSessionState.Starting => "正在开启…",
            GameCrashCaptureSessionState.Monitoring => "正在监测",
            GameCrashCaptureSessionState.StartFailed => "开启失败",
            GameCrashCaptureSessionState.SavingReport => "正在保存现场…",
            GameCrashCaptureSessionState.ReportPending => "报告待处理",
            GameCrashCaptureSessionState.Ended => "已结束",
            GameCrashCaptureSessionState.Stopped => "已停止",
            _ => ""
        };
        StatusMessage = _service.StatusMessage;
        CanEnable = _service.State is GameCrashCaptureSessionState.Disabled
            or GameCrashCaptureSessionState.StartFailed
            or GameCrashCaptureSessionState.Ended
            or GameCrashCaptureSessionState.Stopped;
        CanCancel = _service.State is GameCrashCaptureSessionState.WaitingForTarget
            or GameCrashCaptureSessionState.Starting
            or GameCrashCaptureSessionState.Monitoring;
        var pending = _service.PendingReports.FirstOrDefault();
        HasPendingReport = pending is not null;
        ReportSummary = pending is null ? "" : BuildReportSummary(pending);
        EnableCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        ExportReportCommand.RaiseCanExecuteChanged();
        DeleteReportCommand.RaiseCanExecuteChanged();
        DismissReportCommand.RaiseCanExecuteChanged();
    }

    private static string BuildReportSummary(GameCrashReportBundle bundle)
    {
        var report = bundle.Report;
        var capturedAt = DateTimeOffset.TryParse(report.CompletedAtUtc, out var completed)
            ? completed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "未知时间";
        var size = report.DumpSizeBytes is { } bytes ? $"，Dump {bytes / (1024.0 * 1024.0):F1} MB" : "";
        var kind = report.Status switch
        {
            GameCrashReportStatus.Captured => $"异常 {report.ExceptionCode ?? "未知"}（线程 {report.ExceptionThreadId ?? 0}）",
            GameCrashReportStatus.NoCapture => "游戏退出，未捕获到异常现场",
            _ => $"采集失败：{report.FailureReason ?? "未知原因"}"
        };
        var includes = report.Status == GameCrashReportStatus.Captured
            ? "包含：游戏内存现场（Dump）、崩前诊断快照、近期事件、版本信息"
            : "包含：崩前诊断快照、近期事件、版本信息（无 Dump）";
        return $"{capturedAt} · {kind}{size}\n{includes}";
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        EnableCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        ExportReportCommand.RaiseCanExecuteChanged();
        DeleteReportCommand.RaiseCanExecuteChanged();
    }
}
