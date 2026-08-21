using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using RayaTrainer.App.Services;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.RuntimeAssets.AttributeModifiers;

namespace RayaTrainer.App.ViewModels;

// AscensionViewModel — the "属性修改" full-type modifier matrix page (design doc 2026-08-17).
// The 34-kind matrix with live bit-weight quantization preview and the three scope channels per
// row; Apply/Restore submit the ascension.apply.batch / ascension.restore.batch desired-state
// products through IAscensionSubmitGateway (slice 2d). Wire rows are
// "attributeType:valueBits:scopeMask:faction" (decimal): valueBits is the IEEE-754 encoding of
// the quantizer's template value, which the Agent resolves through the frozen v4 table.
public sealed class AscensionViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> FactionFilters = ["我方", "敌方", "全部"];

    private readonly Action<string> _reportStatus;
    private readonly IAscensionSubmitGateway _gateway;
    private readonly Action _persistSettings;
    private string _factionFilter = FactionFilters[0];
    private const string DefaultStatusText = "勾选修正行并点击应用；还原会卸载本页提交的全部修正。";
    private string _statusText = DefaultStatusText;
    private bool _isBusy;
    private string _presetNameText = string.Empty;
    private AscensionPreset? _selectedPreset;
    private string _presetStatusText = "保存矩阵配置后，可在这里装载；装载不会自动执行。";
    // Source label of the currently displayed active-state notes ("本地提交" / "读回"), null when
    // no active state is shown. Session teardown and successful restore clear it; re-entering the
    // page with notes present triggers a silent read-back so a match change (which clears the
    // Agent table via ClearForMapEnd) reconciles on the next visit.
    private string? _activeSource;

    public AscensionViewModel(
        Action<string> reportStatus,
        IAscensionSubmitGateway gateway,
        IReadOnlyList<AscensionPreset>? presets = null,
        Action? persistSettings = null)
    {
        _reportStatus = reportStatus;
        _gateway = gateway;
        _persistSettings = persistSettings ?? (() => { });
        Groups = BuildGroups();
        Presets = new ObservableCollection<AscensionPreset>(presets ?? Array.Empty<AscensionPreset>());
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => !IsBusy);
        RestoreCommand = new RelayCommand(() => _ = RestoreAsync(), () => !IsBusy);
        RefreshActiveStateCommand = new RelayCommand(() => _ = RefreshActiveStateAsync(silent: false), () => !IsBusy);
        SavePresetCommand = new RelayCommand(SavePreset, () => !IsBusy && !string.IsNullOrWhiteSpace(PresetNameText));
        LoadPresetCommand = new RelayCommand(LoadSelectedPreset, () => !IsBusy && SelectedPreset is not null);
        DeletePresetCommand = new RelayCommand(DeleteSelectedPreset, () => !IsBusy && SelectedPreset is not null);
        RenamePresetCommand = new RelayCommand(RenameSelectedPreset,
            () => !IsBusy && SelectedPreset is not null && !string.IsNullOrWhiteSpace(PresetNameText));
    }

    public IReadOnlyList<AscensionGroupViewModel> Groups { get; }

    public IReadOnlyList<string> FactionFilterOptions => FactionFilters;

    public string FactionFilter
    {
        get => _factionFilter;
        set
        {
            if (_factionFilter == value)
            {
                return;
            }
            _factionFilter = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }
            _isBusy = value;
            OnPropertyChanged();
            ApplyCommand.RaiseCanExecuteChanged();
            RestoreCommand.RaiseCanExecuteChanged();
            RefreshActiveStateCommand.RaiseCanExecuteChanged();
            SavePresetCommand.RaiseCanExecuteChanged();
            LoadPresetCommand.RaiseCanExecuteChanged();
            DeletePresetCommand.RaiseCanExecuteChanged();
            RenamePresetCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand RestoreCommand { get; }

    /// <summary>Manual read-back: replace the displayed active state with the Agent's committed table.</summary>
    public RelayCommand RefreshActiveStateCommand { get; }

    public ObservableCollection<AscensionPreset> Presets { get; }

    public string PresetNameText
    {
        get => _presetNameText;
        set
        {
            if (_presetNameText == value) return;
            _presetNameText = value;
            OnPropertyChanged();
            SavePresetCommand.RaiseCanExecuteChanged();
            RenamePresetCommand.RaiseCanExecuteChanged();
        }
    }

    public AscensionPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (ReferenceEquals(_selectedPreset, value)) return;
            _selectedPreset = value;
            OnPropertyChanged();
            LoadPresetCommand.RaiseCanExecuteChanged();
            DeletePresetCommand.RaiseCanExecuteChanged();
            RenamePresetCommand.RaiseCanExecuteChanged();
        }
    }

    public string PresetStatusText
    {
        get => _presetStatusText;
        private set
        {
            if (_presetStatusText == value) return;
            _presetStatusText = value;
            OnPropertyChanged();
        }
    }

    public RelayCommand SavePresetCommand { get; }

    public RelayCommand LoadPresetCommand { get; }

    public RelayCommand DeletePresetCommand { get; }

    public RelayCommand RenamePresetCommand { get; }

    public IEnumerable<AscensionRowViewModel> AllRows() => Groups.SelectMany(g => g.Rows);

    public IReadOnlyList<AscensionPreset> GetPresetsSnapshot() => Presets.ToArray();

    private void SavePreset()
    {
        var name = PresetNameText.Trim();
        var now = DateTimeOffset.UtcNow;
        var existingIndex = NamedPresetList.FindIndex(Presets, name);
        var existing = existingIndex >= 0 ? Presets[existingIndex] : null;
        var preset = CapturePreset(name, existing?.CreatedAtUtc ?? now, now);
        NamedPresetList.Upsert(Presets, preset);

        SelectedPreset = preset;
        PresetNameText = string.Empty;
        PresetStatusText = $"已保存属性预设「{preset.Name}」（{preset.Rows.Count(r => r.IsEnabled)} 行启用）。";
        _persistSettings();
    }

    private void LoadSelectedPreset()
    {
        if (SelectedPreset is null) return;

        FactionFilter = FactionFromCode(SelectedPreset.Faction);
        var savedRows = new Dictionary<int, AscensionPresetRow>();
        foreach (var row in SelectedPreset.Rows)
        {
            savedRows[row.AttributeType] = row;
        }
        foreach (var row in AllRows())
        {
            row.RestorePreset(savedRows.GetValueOrDefault(row.Entry.AttributeType));
        }

        PresetStatusText = $"已装载属性预设「{SelectedPreset.Name}」；确认后点击“应用”下发。";
    }

    private void DeleteSelectedPreset()
    {
        if (SelectedPreset is null) return;

        var name = SelectedPreset.Name;
        NamedPresetList.RemoveByName(Presets, name);
        SelectedPreset = Presets.FirstOrDefault();
        PresetStatusText = $"已删除属性预设「{name}」。";
        _persistSettings();
    }

    private void RenameSelectedPreset()
    {
        if (SelectedPreset is null) return;

        var newName = PresetNameText.Trim();
        var selectedIndex = Presets.IndexOf(SelectedPreset);
        if (NamedPresetList.ContainsName(Presets, newName, selectedIndex))
        {
            PresetStatusText = $"属性预设「{newName}」已存在，无法重命名。";
            return;
        }

        var renamed = SelectedPreset with { Name = newName, UpdatedAtUtc = DateTimeOffset.UtcNow };
        Presets[Presets.IndexOf(SelectedPreset)] = renamed;
        SelectedPreset = renamed;
        PresetNameText = string.Empty;
        PresetStatusText = $"已重命名为「{newName}」。";
        _persistSettings();
    }

    private AscensionPreset CapturePreset(string name, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new(
            name,
            FactionToCode(FactionFilter),
            AllRows().Select(r => r.CapturePreset()).ToArray(),
            createdAt,
            updatedAt);

    private static int FactionToCode(string factionFilter) => factionFilter switch
    {
        "敌方" => 1,
        "全部" => 2,
        _ => 0,
    };

    private static string FactionFromCode(int faction) => faction switch
    {
        1 => "敌方",
        2 => "全部",
        _ => "我方",
    };

    internal async Task ApplyAsync()
    {
        var enabled = AllRows().Where(r => r.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            StatusText = "没有勾选任何修正行；先在矩阵里勾选要启用的类型。";
            _reportStatus(StatusText);
            return;
        }

        var invalid = new List<string>();
        var noScope = new List<string>();
        foreach (var row in enabled)
        {
            if (!row.Quantization.IsValid)
            {
                invalid.Add($"{row.Entry.DisplayName}：{row.Quantization.Message}");
            }
            else if (!row.ScopeSelected && !row.ScopeAll && !row.ScopeNewRegistered)
            {
                noScope.Add(row.Entry.DisplayName);
            }
        }

        if (invalid.Count > 0 || noScope.Count > 0)
        {
            var parts = new List<string>();
            if (invalid.Count > 0)
            {
                parts.Add($"倍率无效 {invalid.Count} 行（{string.Join("；", invalid.Take(3))}…）");
            }
            if (noScope.Count > 0)
            {
                parts.Add($"未选作用范围 {noScope.Count} 行（{string.Join("、", noScope.Take(3))}…）");
            }
            StatusText = $"配置未完成：{string.Join("；", parts)}";
            _reportStatus(StatusText);
            return;
        }

        var faction = FactionToCode(_factionFilter);
        var rows = BuildWireRows(enabled, faction);
        var needsSelectedIds = enabled.Any(r => r.ScopeSelected);

        IsBusy = true;
        try
        {
            var (success, message) = await _gateway.ApplyAsync(rows, needsSelectedIds);
            if (success)
            {
                // Desired-state commit succeeded. The "已生效" badges mirror only the PERSISTENT
                // state (全部 / 新注册 scopes): a Selected-only row is a one-shot against the
                // submit-time selection and must not display as lasting state.
                var persistent = enabled.Where(r => r.ScopeAll || r.ScopeNewRegistered).ToList();
                var notes = persistent
                    .GroupBy(r => r.Entry.AttributeType)
                    .ToDictionary(
                        g => g.Key,
                        g => (IReadOnlyList<double>)g.SelectMany(r => r.Quantization.WeightValues).ToList());
                ApplyActiveNotes(notes);
                _activeSource = persistent.Count > 0 ? "本地提交" : null;
                var oneShotCount = enabled.Count(r =>
                    r.ScopeSelected && !r.ScopeAll && !r.ScopeNewRegistered);
                var oneShotNote = oneShotCount > 0
                    ? $"另对选中单位一次性应用 {oneShotCount} 行（不显示为生效态）。"
                    : string.Empty;
                StatusText = $"已应用 {enabled.Count} 行修正（阵营={_factionFilter}），当前生效 {notes.Count} 类属性。{oneShotNote}{message}";
            }
            else
            {
                StatusText = message;
            }
        }
        catch (Exception exception)
        {
            StatusText = $"下发属性修改矩阵失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        _reportStatus(StatusText);
    }

    internal async Task RestoreAsync()
    {
        IsBusy = true;
        try
        {
            var (success, message) = await _gateway.RestoreAsync();
            if (success)
            {
                // Restore unloads the whole committed table: the displayed active state goes with it.
                ClearActiveState();
                StatusText = $"已提交全部还原。{message}";
            }
            else
            {
                StatusText = message;
            }
        }
        catch (Exception exception)
        {
            StatusText = $"下发属性修改还原失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        _reportStatus(StatusText);
    }

    /// <summary>
    /// Manual read-back: replace the displayed active state with the Agent's committed policy
    /// table (command 71). This is the reconciliation point after any out-of-band state change
    /// (a match change clears the Agent table via ClearForMapEnd).
    /// </summary>
    internal async Task RefreshActiveStateAsync(bool silent)
    {
        IsBusy = true;
        try
        {
            var (success, message, entries) = await _gateway.ReadbackAsync().ConfigureAwait(true);
            if (success && entries is not null)
            {
                // The Agent table also carries Selected-only one-shot tuples; the badges mirror
                // only the persistent state (All|NewRegistered bits = 2|4).
                const uint persistentMask = 2u | 4u;
                var notes = entries
                    .Where(e => (e.ScopeMask & persistentMask) != 0)
                    .GroupBy(e => (int)e.AttributeType)
                    .ToDictionary(
                        g => g.Key,
                        g => (IReadOnlyList<double>)g
                            .Select(e => (double)BitConverter.UInt32BitsToSingle(e.ValueBits))
                            .ToList());
                ApplyActiveNotes(notes);
                _activeSource = notes.Count > 0 ? "读回" : null;
                StatusText = $"读回完成：当前生效 {notes.Count} 类属性修正。";
            }
            else
            {
                // Silent read-back failures (e.g. page-enter reconciliation while offline) keep
                // the previous notes and status untouched.
                if (!silent)
                {
                    StatusText = message;
                }
            }
        }
        catch (Exception exception)
        {
            if (!silent)
            {
                StatusText = $"读回属性修改状态失败：{exception.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
        _reportStatus(StatusText);
    }

    /// <summary>
    /// Drop the displayed active state (session teardown / successful restore). The Agent side
    /// follows its own lifecycle (reinstall Reset, ClearForMapEnd).
    /// </summary>
    public void ClearActiveState()
    {
        _activeSource = null;
        ApplyActiveNotes(new Dictionary<int, IReadOnlyList<double>>());
        StatusText = DefaultStatusText;
    }

    /// <summary>
    /// Page-enter hook: when active notes are on display, silently reconcile them against the
    /// Agent so a finished/changed match never leaves a stale "已生效" badge.
    /// </summary>
    internal void NotifyPageShown()
    {
        if (_activeSource is not null && !IsBusy)
        {
            _ = RefreshActiveStateAsync(silent: true);
        }
    }

    private void ApplyActiveNotes(IReadOnlyDictionary<int, IReadOnlyList<double>> notesByType)
    {
        foreach (var row in AllRows())
        {
            row.ActiveNote = notesByType.TryGetValue(row.Entry.AttributeType, out var values) && values.Count > 0
                ? "已生效：" + string.Join("，", values.Select(FormatTemplateValue))
                : string.Empty;
        }
    }

    private static string FormatTemplateValue(double value) => value >= 0
        ? $"×{value.ToString("0.##", CultureInfo.InvariantCulture)}"
        : value.ToString("0.##", CultureInfo.InvariantCulture);

    // One wire row per quantized template: "attributeType:valueBits:scopeMask:faction". The
    // scope mask mirrors the Native AscensionScope bits (Selected=1/All=2/NewRegistered=4) and
    // the faction mirrors AscensionFaction (AllyOnly=0/EnemyOnly=1/AllFactions=2).
    internal static IReadOnlyList<string> BuildWireRows(
        IReadOnlyList<AscensionRowViewModel> enabledRows, int faction)
    {
        var rows = new List<string>();
        foreach (var row in enabledRows)
        {
            var scopeMask = (row.ScopeSelected ? 1u : 0u) |
                            (row.ScopeAll ? 2u : 0u) |
                            (row.ScopeNewRegistered ? 4u : 0u);
            foreach (var weightValue in row.Quantization.WeightValues)
            {
                var valueBits = BitConverter.SingleToUInt32Bits((float)weightValue);
                rows.Add($"{row.Entry.AttributeType}:{valueBits}:{scopeMask}:{faction}");
            }
        }
        return rows;
    }

    private static IReadOnlyList<AscensionGroupViewModel> BuildGroups()
    {
        var groups = new List<AscensionGroupViewModel>();
        foreach (var (groupId, title) in AscensionModifierCatalog.Groups)
        {
            var rows = AscensionModifierCatalog.All
                .Where(e => e.GroupId == groupId)
                .Select(e => new AscensionRowViewModel(e))
                .ToList();
            if (rows.Count > 0)
            {
                groups.Add(new AscensionGroupViewModel(title, rows));
            }
        }
        return groups;
    }
}

public sealed class AscensionGroupViewModel
{
    public AscensionGroupViewModel(string title, IReadOnlyList<AscensionRowViewModel> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }

    public IReadOnlyList<AscensionRowViewModel> Rows { get; }
}

public sealed class AscensionRowViewModel : ViewModelBase
{
    private bool _isEnabled;
    private bool _isUp = true;
    private string _multiplierText = "2";
    private bool _scopeSelected = true;
    private bool _scopeAll;
    private bool _scopeNewRegistered;
    private AscensionQuantization _quantization;

    public AscensionRowViewModel(AscensionModifierEntry entry)
    {
        Entry = entry;
        _quantization = AscensionQuantizer.Quantize(entry.Aggregation, _multiplierText, invert: !_isUp);
    }

    public AscensionModifierEntry Entry { get; }

    public AscensionQuantization Quantization => _quantization;

    public string QuantizationText => _quantization.Message;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }
            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsUp
    {
        get => _isUp;
        set
        {
            if (_isUp == value)
            {
                return;
            }
            _isUp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDown));
            RecalculateQuantization();
        }
    }

    public bool IsDown
    {
        get => !_isUp;
        set => IsUp = !value;
    }

    public string MultiplierText
    {
        get => _multiplierText;
        set
        {
            if (_multiplierText == value)
            {
                return;
            }
            _multiplierText = value;
            OnPropertyChanged();
            RecalculateQuantization();
        }
    }

    public bool ScopeSelected
    {
        get => _scopeSelected;
        set
        {
            if (_scopeSelected == value)
            {
                return;
            }
            _scopeSelected = value;
            OnPropertyChanged();
        }
    }

    public bool ScopeAll
    {
        get => _scopeAll;
        set
        {
            if (_scopeAll == value)
            {
                return;
            }
            _scopeAll = value;
            OnPropertyChanged();
        }
    }

    public bool ScopeNewRegistered
    {
        get => _scopeNewRegistered;
        set
        {
            if (_scopeNewRegistered == value)
            {
                return;
            }
            _scopeNewRegistered = value;
            OnPropertyChanged();
        }
    }

    private string _activeNote = string.Empty;

    /// <summary>
    /// Active-state badge text for this row ("已生效：×3，-0.25"); empty when the committed
    /// policy table carries nothing for the row's attribute type. Display-only: the truth is
    /// the Agent table (manual read-back reconciles).
    /// </summary>
    public string ActiveNote
    {
        get => _activeNote;
        set
        {
            if (_activeNote == value)
            {
                return;
            }
            _activeNote = value;
            OnPropertyChanged();
        }
    }

    internal AscensionPresetRow CapturePreset() => new(
        Entry.AttributeType,
        IsEnabled,
        IsUp,
        MultiplierText,
        ScopeSelected,
        ScopeAll,
        ScopeNewRegistered);

    internal void RestorePreset(AscensionPresetRow? preset)
    {
        IsEnabled = preset?.IsEnabled ?? false;
        IsUp = preset?.IsUp ?? true;
        MultiplierText = preset?.MultiplierText ?? "2";
        ScopeSelected = preset?.ScopeSelected ?? true;
        ScopeAll = preset?.ScopeAll ?? false;
        ScopeNewRegistered = preset?.ScopeNewRegistered ?? false;
    }

    private void RecalculateQuantization()
    {
        _quantization = AscensionQuantizer.Quantize(Entry.Aggregation, _multiplierText, invert: !_isUp);
        OnPropertyChanged(nameof(Quantization));
        OnPropertyChanged(nameof(QuantizationText));
    }
}
