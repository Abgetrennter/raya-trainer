using System.Collections.ObjectModel;
using System.Windows.Input;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 快捷键设置页 ViewModel：聚合所有功能 + 动作热键的可编辑行，提供冲突检测、保存与恢复默认。
/// 保存时通过 <see cref="ApplyChanges"/> 回调把新字典回传给 MainViewModel 触发运行时热重载。
/// </summary>
public sealed class HotkeySettingsViewModel : ViewModelBase
{
    // 两个动作热键作为虚拟 feature 纳入设置页统一管理（与功能热键共用同一冲突检测/保存逻辑）。
    public const string ExecuteReinforcementQueueRawName = TrainerFeatureIds.ExecuteReinforcementQueue;
    public const string ReadSelectedUnitCodeRawName = TrainerFeatureIds.ReadSelectedUnitCode;
    // 主控操作热键：全局注册（Win32 RegisterHotKey），与游戏前台无关，单独分组便于用户识别。
    public const string DetectProcessRawName = TrainerFeatureIds.DetectProcess;
    public const string LaunchAndLoadRawName = TrainerFeatureIds.LaunchAndLoad;
    private const string ActionGroupName = "动作热键";
    private const string TrainerControlGroupName = "主控操作（全局）";

    private readonly Action<IReadOnlyDictionary<string, string>> _applyChanges;

    public ObservableCollection<HotkeyRowViewModel> Rows { get; } = new();

    public ObservableCollection<HotkeyGroupViewModel> Groups { get; } = new();

    public ICommand ResetAllCommand { get; }

    public ICommand SaveCommand { get; }

    /// <summary>是否存在未保存改动（用于提示用户离开前保存）。</summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            _hasUnsavedChanges = value;
            OnPropertyChanged();
        }
    }

    private bool _hasUnsavedChanges;

    public HotkeySettingsViewModel(
        IReadOnlyList<TrainerFeature> features,
        IReadOnlyDictionary<string, string> currentHotkeys,
        IReadOnlyDictionary<string, string> defaultHotkeys,
        Action<IReadOnlyDictionary<string, string>> applyChanges)
    {
        _applyChanges = applyChanges;
        ResetAllCommand = new RelayCommand(ResetAll, () => HasUnsavedChanges || HasConflict);
        SaveCommand = new RelayCommand(Save, () => !HasConflict && HasUnsavedChanges);
        BuildRows(features, currentHotkeys, defaultHotkeys);
        RecomputeConflicts();
    }

    /// <summary>全行冲突扫描后是否仍存在冲突（保存按钮门控用）。</summary>
    public bool HasConflict => Rows.Any(row => row.HasConflict);

    private void BuildRows(
        IReadOnlyList<TrainerFeature> features,
        IReadOnlyDictionary<string, string> currentHotkeys,
        IReadOnlyDictionary<string, string> defaultHotkeys)
    {
        foreach (var feature in features)
        {
            var current = Resolve(currentHotkeys, feature.RawName);
            var defaultValue = Resolve(defaultHotkeys, feature.RawName);
            var row = new HotkeyRowViewModel(
                feature.RawName,
                feature.DisplayName,
                TrainerFeatureGroupCatalog.GetGroupName(feature),
                current,
                defaultValue,
                feature.RawName);
            row.CurrentHotkeyChanged += OnRowHotkeyChanged;
            Rows.Add(row);
        }

        // 动作热键：执行队列 / 读取单位代码。
        AddActionRow(ExecuteReinforcementQueueRawName, "执行队列（增援）", currentHotkeys, defaultHotkeys);
        AddActionRow(ReadSelectedUnitCodeRawName, "读取选中单位代码", currentHotkeys, defaultHotkeys);

        // 主控操作热键：全局注册，单独分组。括注"全局"提示用户这两个键不要求游戏前台。
        AddActionRow(DetectProcessRawName, "立刻检测（全局）", currentHotkeys, defaultHotkeys, TrainerControlGroupName);
        AddActionRow(LaunchAndLoadRawName, "装载并启动（全局）", currentHotkeys, defaultHotkeys, TrainerControlGroupName);

        // 按分组聚合（保留 Rows 顺序，分组只是 UI 视图）。
        foreach (var group in Rows.GroupBy(r => r.Group, StringComparer.Ordinal))
        {
            Groups.Add(new HotkeyGroupViewModel(group.Key, group.ToList()));
        }
    }

    private void AddActionRow(string rawName, string displayName,
        IReadOnlyDictionary<string, string> currentHotkeys,
        IReadOnlyDictionary<string, string> defaultHotkeys,
        string groupName = ActionGroupName)
    {
        var current = Resolve(currentHotkeys, rawName);
        var defaultValue = Resolve(defaultHotkeys, rawName);
        var row = new HotkeyRowViewModel(rawName, displayName, groupName, current, defaultValue, rawName);
        row.CurrentHotkeyChanged += OnRowHotkeyChanged;
        Rows.Add(row);
    }

    private static string? Resolve(IReadOnlyDictionary<string, string> hotkeys, string key)
    {
        return hotkeys.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private void OnRowHotkeyChanged(HotkeyRowViewModel changed)
    {
        HasUnsavedChanges = true;
        RecomputeConflicts();
        // 行变化可能影响 ResetAll/Save 可用性。
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 全局冲突重算：对每个非空 CurrentHotkey，若它与其它行的非空值相同（区分大小写）则标记冲突。
    /// 空值（未分配）不参与冲突。
    /// </summary>
    public void RecomputeConflicts()
    {
        foreach (var row in Rows)
        {
            row.HasConflict = false;
            row.ConflictWith = null;
        }

        var nonEmpty = Rows.Where(r => !string.IsNullOrWhiteSpace(r.CurrentHotkey)).ToList();
        for (var i = 0; i < nonEmpty.Count; i++)
        {
            for (var j = i + 1; j < nonEmpty.Count; j++)
            {
                if (string.Equals(nonEmpty[i].CurrentHotkey, nonEmpty[j].CurrentHotkey, StringComparison.Ordinal))
                {
                    nonEmpty[i].HasConflict = true;
                    nonEmpty[j].HasConflict = true;
                    nonEmpty[i].ConflictWith ??= nonEmpty[j].DisplayName;
                    nonEmpty[j].ConflictWith ??= nonEmpty[i].DisplayName;
                }
            }
        }
    }

    private void Save()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            // 空值用空串写入，与配置层「空串=未分配」契约一致。
            dict[row.RawName] = string.IsNullOrWhiteSpace(row.CurrentHotkey) ? string.Empty : row.CurrentHotkey;
        }

        _applyChanges(dict);
        HasUnsavedChanges = false;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ResetAll()
    {
        foreach (var row in Rows)
        {
            row.CurrentHotkey = row.DefaultHotkey;
        }

        HasUnsavedChanges = true;
        RecomputeConflicts();
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}

/// <summary>单行：一个功能/动作的快捷键可编辑数据。</summary>
public sealed class HotkeyRowViewModel : ViewModelBase
{
    private string? _currentHotkey;
    private bool _hasConflict;
    private string? _conflictWith;

    public HotkeyRowViewModel(string rawName, string displayName, string group, string? currentHotkey, string? defaultHotkey, string stableKey)
    {
        RawName = rawName;
        DisplayName = displayName;
        Group = group;
        DefaultHotkey = defaultHotkey;
        StableKey = stableKey;
        _currentHotkey = currentHotkey;
        ClearCommand = new RelayCommand(ClearHotkey, () => !string.IsNullOrWhiteSpace(CurrentHotkey));
    }

    public string RawName { get; }
    public string StableKey { get; }
    public string DisplayName { get; }
    public string Group { get; }
    public string? DefaultHotkey { get; }

    /// <summary>清除本行快捷键（置空），便于禁用不需要的功能。</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>默认键的显示占位（空时显示「无」）。</summary>
    public string DefaultHotKeyPlaceholder => string.IsNullOrWhiteSpace(DefaultHotkey) ? "无" : DefaultHotkey;

    /// <summary>当前是否已分配热键（清除按钮可见性门控）。</summary>
    public bool HasHotkey => !string.IsNullOrWhiteSpace(CurrentHotkey);

    public string? CurrentHotkey
    {
        get => _currentHotkey;
        set
        {
            if (string.Equals(_currentHotkey, value, StringComparison.Ordinal))
            {
                return;
            }

            _currentHotkey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHotkey));
            ClearCommand.RaiseCanExecuteChanged();
            CurrentHotkeyChanged?.Invoke(this);
        }
    }

    private void ClearHotkey()
    {
        CurrentHotkey = null;
    }

    public bool HasConflict
    {
        get => _hasConflict;
        set
        {
            if (_hasConflict == value)
            {
                return;
            }

            _hasConflict = value;
            OnPropertyChanged();
        }
    }

    public string? ConflictWith
    {
        get => _conflictWith;
        set
        {
            if (string.Equals(_conflictWith, value, StringComparison.Ordinal))
            {
                return;
            }

            _conflictWith = value;
            OnPropertyChanged();
        }
    }

    /// <summary>本行热键被用户修改时触发（含清空）。</summary>
    public event Action<HotkeyRowViewModel>? CurrentHotkeyChanged;
}

/// <summary>分组视图：组名 + 该组行列表。</summary>
public sealed record HotkeyGroupViewModel(string Name, IReadOnlyList<HotkeyRowViewModel> Rows);
