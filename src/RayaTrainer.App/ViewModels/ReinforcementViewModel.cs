using System.Collections.ObjectModel;
using System.Windows;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 增援域：增援输入框、预设、队列、单位选择器、模板替换面板。
/// ReinforcementPage 全部内容归本 VM。
/// </summary>
public sealed class ReinforcementViewModel : ViewModelBase
{
    private readonly Func<ITrainerFeatureController?> _getFeatureController;
    private readonly Func<bool> _arePatchesInstalled;
    private readonly Func<bool> _isBusy;
    private readonly Func<bool> _isQueueRunning;
    private readonly Action<bool> _setQueueRunning;
    private readonly Action<string> _setStatus;
    private readonly Action _persistSettings;
    private readonly TrainerFeature _getMeBaseFeature;
    private readonly TrainerFeature _weNeedBackFeature;
    private readonly TrainerFeature _copyForMeFeature;
    private IReadOnlyList<ReinforcementUnitEntry> _reinforcementUnits;
    private string _reinforcementUnitIdText;
    private string _reinforcementCountText;
    private string _reinforcementRankText;
    private string _presetNameText;
    private ReinforcementPreset? _selectedReinforcementPreset;
    private ReinforcementUnitEntry? _selectedReinforcementUnit;
    // 动作热键相关按钮文本（热重载时可写刷新）。构造函数通过属性 setter 赋初值，这里用 null! 抑制编译器 nullable 流分析告警。
    private string _executeReinforcementQueueButtonText = null!;
    private string _readSelectedUnitCodeButtonText = null!;
    private string _getMeBaseButtonText = null!;
    private string _executeReinforcementButtonText = null!;
    private string _copySelectedUnitButtonText = null!;

    public ReinforcementViewModel(
        Func<ITrainerFeatureController?> getFeatureController,
        Func<bool> arePatchesInstalled,
        Func<bool> isBusy,
        Func<bool> isQueueRunning,
        Action<bool> setQueueRunning,
        Action<string> setStatus,
        Action persistSettings,
        TemplateReplacementPanelViewModel templateReplacement,
        TrainerFeature getMeBaseFeature,
        TrainerFeature weNeedBackFeature,
        TrainerFeature copyForMeFeature,
        string executeReinforcementQueueButtonText,
        string readSelectedUnitCodeButtonText,
        string getMeBaseButtonText,
        string executeReinforcementButtonText,
        string copySelectedUnitButtonText,
        TrainerAppSettings settings)
    {
        _getFeatureController = getFeatureController;
        _arePatchesInstalled = arePatchesInstalled;
        _isBusy = isBusy;
        _isQueueRunning = isQueueRunning;
        _setQueueRunning = setQueueRunning;
        _setStatus = setStatus;
        _persistSettings = persistSettings;
        _getMeBaseFeature = getMeBaseFeature;
        _weNeedBackFeature = weNeedBackFeature;
        _copyForMeFeature = copyForMeFeature;
        _reinforcementUnits = ReinforcementUnitCatalog.LoadWithCustomFile();
        _reinforcementUnitIdText = $"0x{ReinforcementSettings.DefaultUnitId:X8}";
        _reinforcementCountText = ReinforcementSettings.DefaultCount.ToString();
        _reinforcementRankText = ReinforcementSettings.DefaultRank.ToString();
        _presetNameText = string.Empty;

        TemplateReplacement = templateReplacement;
        ExecuteReinforcementQueueButtonText = executeReinforcementQueueButtonText;
        ReadSelectedUnitCodeButtonText = readSelectedUnitCodeButtonText;
        GetMeBaseButtonText = getMeBaseButtonText;
        ExecuteReinforcementButtonText = executeReinforcementButtonText;
        CopySelectedUnitButtonText = copySelectedUnitButtonText;

        ReinforcementPresets = new ObservableCollection<ReinforcementPreset>(settings.ReinforcementPresets);
        ReinforcementQueue = new ObservableCollection<ReinforcementQueueItemViewModel>();
        ReinforcementQueue.CollectionChanged += (_, _) => { RaiseCommandStates(); OnPropertyChanged(nameof(ReinforcementQueueCount)); };

        OpenReinforcementUnitPickerCommand = new RelayCommand(OpenReinforcementUnitPicker, () => !_isBusy());
        ReadSelectedUnitCodeCommand = new RelayCommand(ReadSelectedUnitCode, () => _getFeatureController() is not null);
        GetMeBaseCommand = new RelayCommand(() => _ = ExecuteReinforcementPanelActionAsync(_getMeBaseFeature, "给玩家基地车", () => null), CanExecuteReinforcementPanelAction);
        ExecuteReinforcementCommand = new RelayCommand(() => _ = ExecuteReinforcementPanelActionAsync(_weNeedBackFeature, "呼叫战场增援", GetReinforcementSettings), CanExecuteReinforcementPanelAction);
        CopySelectedUnitCommand = new RelayCommand(() => _ = ExecuteReinforcementPanelActionAsync(_copyForMeFeature, "复制选中单位", () => null), CanExecuteReinforcementPanelAction);
        ExecuteReinforcementQueueCommand = new RelayCommand(() => _ = ExecuteReinforcementQueueAsync(), () => _arePatchesInstalled() && _getFeatureController() is not null && !_isQueueRunning() && ReinforcementQueue.Count > 0);
        ClearReinforcementQueueCommand = new RelayCommand(ClearReinforcementQueue, () => !_isQueueRunning() && ReinforcementQueue.Count > 0);
        SaveReinforcementPresetCommand = new RelayCommand(SaveReinforcementPreset, () => !_isQueueRunning());
        ApplyReinforcementPresetCommand = new RelayCommand(ApplySelectedReinforcementPreset, () => SelectedReinforcementPreset is not null && !_isQueueRunning());
        AppendReinforcementPresetCommand = new RelayCommand(AppendSelectedReinforcementPreset, () => SelectedReinforcementPreset is not null && !_isQueueRunning());
        AddCurrentReinforcementToQueueCommand = new RelayCommand(AddCurrentReinforcementToQueue, () => !_isQueueRunning());
    }

    public TemplateReplacementPanelViewModel TemplateReplacement { get; }

    public ObservableCollection<ReinforcementPreset> ReinforcementPresets { get; }
    public ObservableCollection<ReinforcementQueueItemViewModel> ReinforcementQueue { get; }
    public int ReinforcementQueueCount => ReinforcementQueue.Count;

    public string ReinforcementUnitIdText
    {
        get => _reinforcementUnitIdText;
        set
        {
            _reinforcementUnitIdText = value;
            if (_selectedReinforcementUnit is not null &&
                (!UnitCodeParser.TryParse(value, out var unitId) || unitId != _selectedReinforcementUnit.Code))
            {
                _selectedReinforcementUnit = null;
            }
            OnPropertyChanged();
        }
    }
    public string ReinforcementCountText { get => _reinforcementCountText; set { _reinforcementCountText = value; OnPropertyChanged(); } }
    public string ReinforcementRankText { get => _reinforcementRankText; set { _reinforcementRankText = value; OnPropertyChanged(); } }
    public string PresetNameText { get => _presetNameText; set { _presetNameText = value; OnPropertyChanged(); } }
    public ReinforcementPreset? SelectedReinforcementPreset { get => _selectedReinforcementPreset; set { _selectedReinforcementPreset = value; OnPropertyChanged(); RaiseCommandStates(); } }

    // 按钮文本支持热重载：快捷键改键后无需重启即可刷新显示。
    public string ExecuteReinforcementQueueButtonText { get => _executeReinforcementQueueButtonText; set { _executeReinforcementQueueButtonText = value; OnPropertyChanged(); } }
    public string ReadSelectedUnitCodeButtonText { get => _readSelectedUnitCodeButtonText; set { _readSelectedUnitCodeButtonText = value; OnPropertyChanged(); } }
    public string GetMeBaseButtonText { get => _getMeBaseButtonText; set { _getMeBaseButtonText = value; OnPropertyChanged(); } }
    public string ExecuteReinforcementButtonText { get => _executeReinforcementButtonText; set { _executeReinforcementButtonText = value; OnPropertyChanged(); } }
    public string CopySelectedUnitButtonText { get => _copySelectedUnitButtonText; set { _copySelectedUnitButtonText = value; OnPropertyChanged(); } }

    public string ReinforcementUnitIdHelpText => "增援要生成的单位代码；可由单位列表或读取选中单位按钮填入。";
    public string ReinforcementCountHelpText => "每次呼叫增援生成的单位数量。";
    public string ReinforcementRankHelpText => "增援生成后的经验等级，范围 0-3。";
    public string OpenReinforcementUnitPickerHelpText => "打开内置/自定义单位代码列表；选中后把代码写入增援单位ID文本框。";
    public string ReadSelectedUnitCodeHelpText => "读取游戏里当前选中单位的单位代码，并写入增援单位ID文本框，供呼叫战场增援、当前入队和保存预设使用。";
    public string GetMeBaseHelpText => "在鼠标地图坐标附近给玩家生成玩家基地车组合；需要进入对局并已安装 patch。";
    public string ExecuteReinforcementHelpText => "立即按当前增援单位ID、数量和星级呼叫一次战场增援；不需要先加入队列。";
    public string CopySelectedUnitHelpText => "以当前选中单位类型为模板，在鼠标地图坐标附近给玩家复制一个单位或建筑。";
    public string PresetNameHelpText => "仅用于保存增援队列预设；当前入队项名称由单位ID对应的单位名称决定。为空时使用第一项单位名称。";
    public string ReinforcementPresetHelpText => "选择已保存的增援队列预设，可应用（清空并恢复）或追加到当前队列。";
    public string SaveReinforcementPresetHelpText => "把当前增援队列保存为命名预设；同名预设会被覆盖。";
    public string ApplyReinforcementPresetHelpText => "清空当前增援队列并恢复选中预设的全部条目。";
    public string AppendReinforcementPresetHelpText => "把选中预设的全部条目追加到当前增援队列末尾。";
    public string AddCurrentReinforcementToQueueHelpText => "把当前输入框里的单位ID、数量和星级追加到增援队列。";
    public string ExecuteReinforcementQueueHelpText => "按队列顺序逐项呼叫增援；无效项会标记跳过，后续项继续执行。";
    public string ClearReinforcementQueueHelpText => "清空尚未执行或已执行的队列显示，不删除已保存预设。";

    public RelayCommand OpenReinforcementUnitPickerCommand { get; }
    public RelayCommand ReadSelectedUnitCodeCommand { get; }
    public RelayCommand GetMeBaseCommand { get; }
    public RelayCommand ExecuteReinforcementCommand { get; }
    public RelayCommand CopySelectedUnitCommand { get; }
    public RelayCommand ExecuteReinforcementQueueCommand { get; }
    public RelayCommand ClearReinforcementQueueCommand { get; }
    public RelayCommand SaveReinforcementPresetCommand { get; }
    public RelayCommand ApplyReinforcementPresetCommand { get; }
    public RelayCommand AppendReinforcementPresetCommand { get; }
    public RelayCommand AddCurrentReinforcementToQueueCommand { get; }

    /// <summary>供 MainVM.CurrentSettings() 收集预设快照。</summary>
    public IReadOnlyList<ReinforcementPreset> GetReinforcementPresetsSnapshot() => ReinforcementPresets.ToArray();

    public ReinforcementSettings GetReinforcementSettings() =>
        ReinforcementSettings.Parse(ReinforcementUnitIdText, ReinforcementCountText, ReinforcementRankText);

    public void ReadSelectedUnitCode()
    {
        var controller = _getFeatureController();
        if (controller is null) { _setStatus("请先检测进程并安装 patch。"); return; }
        try
        {
            var unitCode = controller.ReadSelectedUnitCode();
            ReinforcementUnitIdText = $"0x{unitCode:X8}";
            _setStatus($"已读取选中单位代码：{ReinforcementUnitIdText}");
        }
        catch (Exception ex) { _setStatus($"读取选中单位代码失败：{ex.Message}"); }
    }

    public void OpenReinforcementUnitPicker()
    {
        try
        {
            var picker = new ReinforcementUnitPickerViewModel(_reinforcementUnits, AppContext.BaseDirectory);
            var window = new Views.ReinforcementUnitPickerWindow { Owner = Application.Current?.MainWindow, DataContext = picker };
            var result = window.ShowDialog();
            _reinforcementUnits = picker.Units.ToArray();
            if (result == true && picker.SelectedUnit is not null)
            {
                ReinforcementUnitIdText = picker.SelectedUnit.CodeText;
                _selectedReinforcementUnit = picker.SelectedUnit;
                _setStatus($"已选择单位：{picker.SelectedUnit.Name} ({picker.SelectedUnit.CodeText})");
            }
        }
        catch (Exception ex) { _setStatus($"打开单位列表失败：{ex.Message}"); }
    }

    private async Task ExecuteReinforcementPanelActionAsync(
        TrainerFeature feature, string actionName, Func<ReinforcementSettings?> createSettings)
    {
        var controller = _getFeatureController();
        if (controller is null) { _setStatus("请先检测进程并安装 patch。"); return; }
        try
        {
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                feature, createSettings(),
                FeatureDispatchDefaults.Timeout, FeatureDispatchDefaults.PollInterval,
                onWaitStatusChanged: status => _setStatus(DispatchWaitStatusText(status, actionName)));
            _setStatus(result switch
            {
                ActionDispatchResult.Consumed => $"已执行{actionName}。",
                ActionDispatchResult.NotRequired => $"{actionName}命令已触发。",
                ActionDispatchResult.TimedOut => $"{actionName}动作已写入，但尚未被游戏循环消费。",
                ActionDispatchResult.AbortedDueToPause => $"{actionName}已放弃：游戏保持暂停状态。",
                _ => $"{actionName}返回未知状态。"
            });
        }
        catch (Exception ex) { _setStatus($"{actionName}失败：{ex.Message}"); }
    }

    private async Task ExecuteReinforcementQueueAsync()
    {
        var controller = _getFeatureController();
        if (controller is null) { _setStatus("请先检测进程并安装 patch。"); return; }
        _setQueueRunning(true);
        try
        {
            var executed = 0;
            var skipped = 0;
            foreach (var item in ReinforcementQueue)
            {
                item.Status = "执行中";
                item.Message = string.Empty;
                var results = await ReinforcementQueueRunner.ExecuteAsync(
                    [item.ToEntry()], controller, _weNeedBackFeature,
                    FeatureDispatchDefaults.Timeout, FeatureDispatchDefaults.PollInterval,
                    onWaitStatusChanged: status => _setStatus(DispatchWaitStatusText(status, "增援队列")));
                var result = results[0];
                item.ApplyResult(result);
                if (result.Status == ReinforcementQueueItemStatus.Executed) executed++;
                else if (result.Status == ReinforcementQueueItemStatus.Skipped) skipped++;
            }
            _setStatus($"增援队列执行完成：成功 {executed}，跳过 {skipped}。");
        }
        catch (Exception ex) { _setStatus($"增援队列执行失败：{ex.Message}"); }
        finally { _setQueueRunning(false); }
    }

    private void ClearReinforcementQueue() { ReinforcementQueue.Clear(); _setStatus("增援队列已清空。"); }

    private void SaveReinforcementPreset()
    {
        try
        {
            if (ReinforcementQueue.Count == 0)
            {
                _setStatus("增援队列为空，无法保存预设。");
                return;
            }

            var entries = ReinforcementQueue
                .Select(item => ReinforcementPresetEntry.FromQueueEntry(item.ToEntry()))
                .ToArray();
            var name = string.IsNullOrWhiteSpace(PresetNameText)
                ? entries[0].Name
                : PresetNameText.Trim();
            var preset = new ReinforcementPreset(name, entries);
            var existingIndex = IndexOfPreset(preset.Name);
            if (existingIndex >= 0) ReinforcementPresets[existingIndex] = preset;
            else ReinforcementPresets.Add(preset);
            SelectedReinforcementPreset = preset;
            _persistSettings();
            _setStatus($"已保存增援队列预设：{preset.Name}（{preset.Entries.Count} 项）");
        }
        catch (Exception ex) { _setStatus($"保存增援队列预设失败：{ex.Message}"); }
    }

    private void ApplySelectedReinforcementPreset()
    {
        if (SelectedReinforcementPreset is null) return;
        if (SelectedReinforcementPreset.Entries.Count == 0)
        {
            _setStatus("选中预设没有条目。");
            return;
        }

        ReinforcementQueue.Clear();
        AppendPresetEntries(SelectedReinforcementPreset);
        _setStatus($"已应用增援队列预设：{SelectedReinforcementPreset.Name}");
    }

    private void AppendSelectedReinforcementPreset()
    {
        if (SelectedReinforcementPreset is null) return;
        if (SelectedReinforcementPreset.Entries.Count == 0)
        {
            _setStatus("选中预设没有条目。");
            return;
        }

        AppendPresetEntries(SelectedReinforcementPreset);
        _setStatus($"已追加增援队列预设：{SelectedReinforcementPreset.Name}（{SelectedReinforcementPreset.Entries.Count} 项）");
    }

    private void AddCurrentReinforcementToQueue()
    {
        var name = ResolveCurrentReinforcementUnitName();
        AddQueueEntry(new ReinforcementQueueEntry(name, ReinforcementUnitIdText, ReinforcementCountText, ReinforcementRankText));
    }

    private string ResolveCurrentReinforcementUnitName()
    {
        if (!UnitCodeParser.TryParse(ReinforcementUnitIdText, out var unitId))
        {
            return ReinforcementUnitIdText;
        }

        if (_selectedReinforcementUnit?.Code == unitId)
        {
            return _selectedReinforcementUnit.Name;
        }

        return _reinforcementUnits.FirstOrDefault(unit => unit.Code == unitId)?.Name
            ?? UnitCodeParser.Format(unitId);
    }

    private void AppendPresetEntries(ReinforcementPreset preset)
    {
        foreach (var entry in preset.Entries)
        {
            AddQueueEntry(entry.ToQueueEntry(), updateStatus: false);
        }
    }

    private void AddQueueEntry(ReinforcementQueueEntry entry, bool updateStatus = true)
    {
        ReinforcementQueue.Add(new ReinforcementQueueItemViewModel(
            entry.Name, entry.UnitIdText, entry.CountText, entry.RankText,
            RemoveQueueItem, () => !_isQueueRunning()));
        if (updateStatus) _setStatus($"已加入增援队列：{entry.Name}");
    }

    private void RemoveQueueItem(ReinforcementQueueItemViewModel item) => ReinforcementQueue.Remove(item);

    private int IndexOfPreset(string name)
    {
        for (var index = 0; index < ReinforcementPresets.Count; index++)
            if (ReinforcementPresets[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private bool CanExecuteReinforcementPanelAction() =>
        _arePatchesInstalled() && _getFeatureController() is not null && !_isQueueRunning();

    /// <summary>
    /// Maps a pause-aware wait status to a user-facing status-bar string.
    /// R2c: surfaces "waiting for resume" feedback while the trainer holds
    /// a dispatch open during a paused game.
    /// </summary>
    private static string DispatchWaitStatusText(DispatchWaitStatus status, string label)
    {
        return status switch
        {
            DispatchWaitStatus.PausedWaiting => $"游戏已暂停，等待恢复…（{label}）",
            DispatchWaitStatus.Resumed => $"游戏已恢复，继续执行…（{label}）",
            DispatchWaitStatus.GraceExpired => $"等待超时，已放弃当前操作。（{label}）",
            _ => $"{label}执行中…"
        };
    }

    public void RaiseCommandStates()
    {
        OpenReinforcementUnitPickerCommand.RaiseCanExecuteChanged();
        ReadSelectedUnitCodeCommand.RaiseCanExecuteChanged();
        GetMeBaseCommand.RaiseCanExecuteChanged();
        ExecuteReinforcementCommand.RaiseCanExecuteChanged();
        CopySelectedUnitCommand.RaiseCanExecuteChanged();
        ExecuteReinforcementQueueCommand.RaiseCanExecuteChanged();
        ClearReinforcementQueueCommand.RaiseCanExecuteChanged();
        SaveReinforcementPresetCommand.RaiseCanExecuteChanged();
        ApplyReinforcementPresetCommand.RaiseCanExecuteChanged();
        AppendReinforcementPresetCommand.RaiseCanExecuteChanged();
        AddCurrentReinforcementToQueueCommand.RaiseCanExecuteChanged();
        TemplateReplacement.RaiseCommandStates();
        foreach (var item in ReinforcementQueue) item.RaiseCommandState();
    }
}
