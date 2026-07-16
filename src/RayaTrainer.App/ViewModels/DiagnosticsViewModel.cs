using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using RayaTrainer.App.Services;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase, IDisposable
{
    private readonly ITrainerDiagnosticsSource? _source;
    private readonly IReadOnlyList<TrainerFeature> _features;
    private readonly Action<string> _setStatus;
    private readonly Action? _retrySession;
    private readonly Action? _installPatches;
    private readonly DispatcherTimer _timer;
    private TrainerDiagnosticSnapshot _snapshot;
    private bool _isRefreshing;
    private bool _disposed;

    // LAA 状态（独立于快照，由用户交互触发）
    private string? _laaModulePath;
    private bool? _laaResult;
    private string? _laaError;

    public DiagnosticsViewModel(
        ITrainerDiagnosticsSource? source,
        IReadOnlyList<TrainerFeature> features,
        Action<string> setStatus,
        Action? retrySession = null,
        Action? installPatches = null)
    {
        _source = source;
        _features = features;
        _setStatus = setStatus;
        _retrySession = retrySession;
        _installPatches = installPatches;
        _snapshot = source?.GetDiagnosticSnapshot(features) ?? TrainerDiagnosticSnapshot.Offline;
        Stages = new ObservableCollection<DiagnosticStageItemViewModel>();
        CapabilityGroups = new ObservableCollection<DiagnosticCapabilityGroupViewModel>();
        Events = new ObservableCollection<TrainerDiagnosticEvent>();
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !_isRefreshing);
        ExportCommand = new RelayCommand(ExportDiagnostics);
        OpenReportCommand = new RelayCommand(OpenLatestReport, () => HasReport);
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += OnTimerTick;
        if (_source is not null)
        {
            _source.DiagnosticsChanged += OnDiagnosticsChanged;
        }

        CheckLaaCommand = new RelayCommand(CheckLaa, () => CanCheckLaa);
        PatchLaaCommand = new RelayCommand(PatchLaa, () => ShowLaaPatchButton);

        ApplySnapshot(_snapshot);
    }

    public event EventHandler? SnapshotChanged;

    public TrainerDiagnosticSnapshot Snapshot
    {
        get => _snapshot;
        private set
        {
            _snapshot = value;
            OnPropertyChanged();
            RaiseSummaryProperties();
        }
    }

    public ObservableCollection<DiagnosticStageItemViewModel> Stages { get; }

    public ObservableCollection<DiagnosticCapabilityGroupViewModel> CapabilityGroups { get; }

    public ObservableCollection<TrainerDiagnosticEvent> Events { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand OpenReportCommand { get; }

    // --- LAA 命令 ---
    public RelayCommand CheckLaaCommand { get; }
    public RelayCommand PatchLaaCommand { get; }

    public TrainerDiagnosticHealth Health => Snapshot.Health;

    public string HealthLabel => Snapshot.Health switch
    {
        TrainerDiagnosticHealth.Healthy => "就绪",
        TrainerDiagnosticHealth.Attention => "注意",
        TrainerDiagnosticHealth.Error => "故障",
        _ => "离线"
    };

    public string Summary => Snapshot.Summary;

    public string CapturedAtText => Snapshot.CapturedAt == DateTimeOffset.MinValue
        ? "尚未采集"
        : Snapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss");

    public int AttentionCount => Snapshot.Stages.Count(stage =>
        stage.State is DiagnosticStageState.Warning or DiagnosticStageState.Error);

    public bool HasAttention => AttentionCount > 0;

    public bool HasReport => !string.IsNullOrWhiteSpace(Snapshot.LastReportPath) && File.Exists(Snapshot.LastReportPath);

    public string TargetDetails => Snapshot.Target is null
        ? "未连接目标"
        : $"{Snapshot.Target.ProfileName}" +
          (Snapshot.Target.SignatureCompatibilityMode ? " · 签名兼容" : string.Empty) +
          $" · PID={Snapshot.Target.ProcessId} · {Snapshot.Target.Runtime}\n{Snapshot.Target.ModulePath}";

    public string AgentDetails => Snapshot.Agent.Summary;

    public string SignatureDetails => Snapshot.Signatures.Summary;

    public string PatchDetails => Snapshot.Patch.Summary;

    public string GameDetails => Snapshot.Game.Summary;

    // --- LAA 属性 ---

    public bool CanCheckLaa => !string.IsNullOrWhiteSpace(_laaModulePath) && !_isRefreshing;

    public bool ShowLaaPatchButton => _laaResult == false && !string.IsNullOrWhiteSpace(_laaModulePath);

    public bool HasLaaBackup =>
        _laaModulePath is not null && LargeAddressAwarePatcher.HasBackup(_laaModulePath);

    public string LaaSummary
    {
        get
        {
            if (_laaModulePath is null)
                return "请先连接游戏目标。";
            if (_laaError is not null)
                return $"检查失败：{_laaError}";
            return _laaResult switch
            {
                null => "点击「检查 LAA」查看当前标记状态。",
                true => "已标记 Large Address Aware（可访问 4GB 内存）。无需额外操作。",
                false => "未标记 Large Address Aware。32 位程序最高只能使用 2GB 内存。" +
                         "标记后可提升稳定性，尤其在加载大量 Mod 或长时间对局后。"
            };
        }
    }

    public string? LaaModulePath => _laaModulePath;

    public string TechnicalDetails
    {
        get
        {
            var target = Snapshot.Target;
            var agent = Snapshot.Agent;
            var signatures = Snapshot.Signatures;
            var patch = Snapshot.Patch;
            var lines = new List<string>
            {
                $"target.module={target?.ModulePath ?? "-"}",
                $"target.base={target?.ModuleBase ?? "-"}",
                $"target.signatureCompatibility={target?.SignatureCompatibilityMode ?? false}",
                $"agent.module={agent.ModuleBase}",
                $"agent.nativeCapabilities=0x{agent.NativeRuntimeCapabilities:X8}",
                $"signature.required_unresolved={string.Join(", ", signatures.RequiredUnresolved)}",
                $"signature.optional_unresolved={string.Join(", ", signatures.OptionalUnresolved)}",
                $"signature.superseded={string.Join(", ", signatures.SupersededSymbols)}",
                $"patch.report={patch.ReportPath ?? "-"}"
            };
            lines.AddRange(patch.SkippedHooks.Select(hook =>
                $"patch.skipped={hook.Name} @ {hook.Address}: {hook.Reason}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    public void SetActive(bool active)
    {
        if (_disposed)
        {
            return;
        }

        if (active)
        {
            _timer.Start();
            _ = RefreshAsync();
        }
        else
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        if (_source is not null)
        {
            _source.DiagnosticsChanged -= OnDiagnosticsChanged;
        }
    }

    // --- LAA 操作 ---

    private void CheckLaa()
    {
        if (string.IsNullOrWhiteSpace(_laaModulePath))
            return;

        _laaError = null;
        _laaResult = LargeAddressAwarePatcher.CheckFlag(_laaModulePath);
        if (_laaResult is null)
            _laaError = "无法读取 PE 头，文件可能已被锁定或不是有效的 PE 文件。";

        _source?.RecordDiagnosticEvent(
            _laaResult is null
                ? DiagnosticEventSeverity.Error
                : _laaResult.Value
                    ? DiagnosticEventSeverity.Info
                    : DiagnosticEventSeverity.Warning,
            _laaResult is null
                ? "laa.check_failed"
                : _laaResult.Value
                    ? "laa.found"
                    : "laa.missing",
            _laaResult switch
            {
                null => "LAA 标记检查失败。",
                true => "LAA 标记已存在。",
                false => "LAA 标记缺失。"
            },
            _laaModulePath);

        RaiseLaaProperties();
        CheckLaaCommand.RaiseCanExecuteChanged();
        PatchLaaCommand.RaiseCanExecuteChanged();
    }

    private void PatchLaa()
    {
        if (string.IsNullOrWhiteSpace(_laaModulePath))
            return;

        _laaError = null;
        var result = LargeAddressAwarePatcher.ApplyFlag(_laaModulePath);
        if (result.Success)
        {
            _laaResult = true;
            _source?.RecordDiagnosticEvent(
                DiagnosticEventSeverity.Info,
                "laa.patched",
                "LAA 标记已成功写入。",
                _laaModulePath);
        }
        else
        {
            _laaError = result.ErrorMessage;
            _source?.RecordDiagnosticEvent(
                DiagnosticEventSeverity.Error,
                "laa.patch_failed",
                "LAA 标记写入失败。",
                result.ErrorMessage);
        }

        RaiseLaaProperties();
        PatchLaaCommand.RaiseCanExecuteChanged();
    }

    private void RaiseLaaProperties()
    {
        OnPropertyChanged(nameof(LaaSummary));
        OnPropertyChanged(nameof(CanCheckLaa));
        OnPropertyChanged(nameof(ShowLaaPatchButton));
        OnPropertyChanged(nameof(HasLaaBackup));
        OnPropertyChanged(nameof(LaaModulePath));
    }

    private async Task RefreshAsync()
    {
        if (_source is null || _isRefreshing || _disposed)
        {
            return;
        }

        _isRefreshing = true;
        RefreshCommand.RaiseCanExecuteChanged();
        try
        {
            var snapshot = await _source.RefreshDiagnosticsAsync(_features).ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        finally
        {
            _isRefreshing = false;
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 RAЯ Trainer 诊断包",
            Filter = "ZIP 诊断包 (*.zip)|*.zip",
            FileName = $"ra3-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = ".zip"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            TrainerDiagnosticExporter.Export(
                dialog.FileName,
                Snapshot,
                _source?.DiagnosticEvents ?? Snapshot.RecentEvents,
                Snapshot.LastReportPath);
            _source?.RecordDiagnosticEvent(
                DiagnosticEventSeverity.Info,
                "diagnostics.exported",
                "诊断包已导出。",
                dialog.FileName);
            _setStatus($"诊断包已导出：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            _source?.RecordDiagnosticEvent(
                DiagnosticEventSeverity.Error,
                "diagnostics.export_failed",
                "诊断包导出失败。",
                ex.Message);
            _setStatus($"诊断包导出失败：{ex.Message}");
        }
    }

    private void OpenLatestReport()
    {
        var reportPath = Snapshot.LastReportPath;
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            _setStatus("当前没有可打开的诊断报告。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _setStatus($"无法打开诊断报告：{ex.Message}");
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) => _ = RefreshAsync();

    private void OnDiagnosticsChanged(object? sender, EventArgs e)
    {
        if (_source is null || _disposed)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplySnapshot(_source.GetDiagnosticSnapshot(_features)));
            return;
        }

        ApplySnapshot(_source.GetDiagnosticSnapshot(_features));
    }

    private void ApplySnapshot(TrainerDiagnosticSnapshot snapshot)
    {
        Snapshot = snapshot;
        Replace(Stages, snapshot.Stages.Select(CreateStageItem));
        var groups = snapshot.Capabilities
            .GroupBy(capability => capability.GroupName, StringComparer.Ordinal)
            .Select(group => new DiagnosticCapabilityGroupViewModel(group.Key, group.ToArray()))
            .ToArray();
        Replace(CapabilityGroups, groups);
        Replace(Events, snapshot.RecentEvents.Reverse());

        // 首次获取模块路径后，填充 LAA 检查目标（不覆盖已有检查结果）
        if (_laaModulePath is null && snapshot.Laa.ModulePath is not null)
        {
            _laaModulePath = snapshot.Laa.ModulePath;
            CheckLaaCommand.RaiseCanExecuteChanged();
            RaiseLaaProperties();
        }

        OpenReportCommand.RaiseCanExecuteChanged();
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private DiagnosticStageItemViewModel CreateStageItem(DiagnosticStageSnapshot stage)
    {
        var actionLabel = stage.State is DiagnosticStageState.Healthy or DiagnosticStageState.NotApplicable
            ? null
            : stage.Id switch
            {
                "target" => "重新查找游戏",
                "agent" => "重新连接",
                "signature" => HasReport ? "打开检查报告" : "重新连接",
                "patch" when Snapshot.Target is not null => Snapshot.Patch.InstalledHookCount == 0
                    ? "启用修改器"
                    : HasReport ? "打开检查报告" : "再检查一次",
                "game" when Snapshot.Patch.InstalledHookCount > 0 => "再检查一次",
                _ => null
            };
        return new DiagnosticStageItemViewModel(stage, actionLabel, () => RunStageAction(stage));
    }

    private void RunStageAction(DiagnosticStageSnapshot stage)
    {
        switch (stage.Id)
        {
            case "target":
            case "agent":
                _retrySession?.Invoke();
                break;
            case "signature":
                if (HasReport)
                {
                    OpenLatestReport();
                }
                else
                {
                    _retrySession?.Invoke();
                }
                break;
            case "patch":
                if (Snapshot.Patch.InstalledHookCount == 0)
                {
                    _installPatches?.Invoke();
                }
                else if (HasReport)
                {
                    OpenLatestReport();
                }
                else
                {
                    _ = RefreshAsync();
                }
                break;
            case "game":
                _ = RefreshAsync();
                break;
        }
    }

    private void RaiseSummaryProperties()
    {
        OnPropertyChanged(nameof(Health));
        OnPropertyChanged(nameof(HealthLabel));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CapturedAtText));
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(TargetDetails));
        OnPropertyChanged(nameof(AgentDetails));
        OnPropertyChanged(nameof(SignatureDetails));
        OnPropertyChanged(nameof(PatchDetails));
        OnPropertyChanged(nameof(GameDetails));
        OnPropertyChanged(nameof(TechnicalDetails));
        OnPropertyChanged(nameof(LaaSummary));
        OnPropertyChanged(nameof(CanCheckLaa));
        OnPropertyChanged(nameof(ShowLaaPatchButton));
        OnPropertyChanged(nameof(HasLaaBackup));
        OnPropertyChanged(nameof(LaaModulePath));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

public sealed record DiagnosticCapabilityGroupViewModel(
    string Name,
    IReadOnlyList<FeatureCapabilitySnapshot> Capabilities);

public sealed class DiagnosticStageItemViewModel
{
    public DiagnosticStageItemViewModel(
        DiagnosticStageSnapshot stage,
        string? actionLabel,
        Action action)
    {
        Stage = stage;
        ActionLabel = actionLabel;
        ActionCommand = new RelayCommand(action, () => HasAction);
    }

    public DiagnosticStageSnapshot Stage { get; }

    public string Id => Stage.Id;

    public string TechnicalLabel => Stage.Label;

    public string Label => Stage.Id switch
    {
        "target" => "找到游戏",
        "profile" => "识别版本",
        "agent" => "连接组件",
        "signature" => "检查兼容性",
        "patch" => "启用功能",
        "game" => "读取游戏状态",
        _ => Stage.Label
    };

    public DiagnosticStageState State => Stage.State;

    public string StateLabel => Stage.State switch
    {
        DiagnosticStageState.Healthy => "正常",
        DiagnosticStageState.Warning => "需要注意",
        DiagnosticStageState.Error => "没有通过",
        DiagnosticStageState.NotApplicable => "无需处理",
        _ => "等待中"
    };

    public string Summary => Stage.Summary;

    public string Guidance => Stage.RecommendedAction ?? Stage.State switch
    {
        DiagnosticStageState.Healthy => "这一项已经准备好。",
        DiagnosticStageState.NotApplicable => "当前模式不需要这一步。",
        _ => "请按提示继续。"
    };

    public string? ActionLabel { get; }

    public bool HasAction => !string.IsNullOrWhiteSpace(ActionLabel);

    public RelayCommand ActionCommand { get; }
}
