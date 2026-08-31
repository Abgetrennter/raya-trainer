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

// Which side of the fight a modifier targets. The numeric values ARE the wire faction codes
// (AscensionFaction: AllyOnly=0 / EnemyOnly=1 / AllFactions=2): each side owns an independent
// copy of the matrix, so "我方伤害 ×2" and "敌方伤害 ×0.5" coexist in one desired-state commit.
public enum AscensionSide
{
    Ally = 0,
    Enemy = 1,
    All = 2,
}

// AscensionViewModel — the "属性修改" full-type modifier matrix page (redesign 2026-08-31).
// Three side tabs (我方/敌方/全部单位) each own an independent matrix; Apply merges every side
// into one desired-state table whose wire rows carry per-row faction
// ("attributeType:valueBits:scopeMask:faction", decimal). Scope semantics per row: 选中 is a
// submit-time one-shot and is MUTUALLY EXCLUSIVE with the persistent scopes (全部/新注册),
// which stack.
//
// Pending vs committed state: a row switch marks a PENDING modification. Apply commits the
// desired-state table = committed persistent entries + pending rows (pending wins per
// (side, attributeType) key, because apply.batch REPLACES the whole Agent table) and then
// resets every submitted switch. The committed state drives the "已生效" badges, the overview
// lanes and clear/re-commit; the overview offers per-item and per-side clearing (re-commit the
// remaining committed rows; restore when nothing remains).
public sealed class AscensionViewModel : ViewModelBase
{
    private readonly Action<string> _reportStatus;
    private readonly IAscensionSubmitGateway _gateway;
    private readonly Action _persistSettings;
    private const string DefaultStatusText = "在各页签打开开关并点击应用；提交成功后开关自动复位，生效项见总览区。还原卸载全部已提交修正。";
    private string _statusText = DefaultStatusText;
    private bool _isBusy;
    private string _presetNameText = string.Empty;
    private AscensionPreset? _selectedPreset;
    private string _presetStatusText = "保存页签配置后，可在这里装载；装载不会自动执行。";
    // Committed persistent state, keyed by (side, attributeType): quantized template values +
    // their wire scope mask. Session teardown and successful restore clear it; read-back
    // replaces it with the Agent truth.
    private Dictionary<(AscensionSide Side, int Type), CommittedEntry> _committed = new();
    private string? _activeSource;

    private sealed record CommittedEntry(IReadOnlyList<double> TemplateValues, uint ScopeMask);

    public AscensionViewModel(
        Action<string> reportStatus,
        IAscensionSubmitGateway gateway,
        IReadOnlyList<AscensionPreset>? presets = null,
        Action? persistSettings = null)
    {
        _reportStatus = reportStatus;
        _gateway = gateway;
        _persistSettings = persistSettings ?? (() => { });
        Sides =
        [
            new AscensionSideViewModel(AscensionSide.Ally, "我方", ClearSideAsync),
            new AscensionSideViewModel(AscensionSide.Enemy, "敌方", ClearSideAsync),
            new AscensionSideViewModel(AscensionSide.All, "全部单位", ClearSideAsync),
        ];
        Sides[0].IsSelected = true;
        Presets = new ObservableCollection<AscensionPreset>(presets ?? Array.Empty<AscensionPreset>());
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => !IsBusy);
        RestoreCommand = new RelayCommand(() => _ = RestoreAsync(), () => !IsBusy);
        RefreshActiveStateCommand = new RelayCommand(() => _ = RefreshActiveStateAsync(silent: false), () => !IsBusy);
        ClearRowsCommand = new RelayCommand<IEnumerable<AscensionRowViewModel>>(
            rows => _ = ClearRowsAsync(rows ?? []), _ => !IsBusy);
        SavePresetCommand = new RelayCommand(SavePreset, () => !IsBusy && !string.IsNullOrWhiteSpace(PresetNameText));
        LoadPresetCommand = new RelayCommand(LoadSelectedPreset, () => !IsBusy && SelectedPreset is not null);
        DeletePresetCommand = new RelayCommand(DeleteSelectedPreset, () => !IsBusy && SelectedPreset is not null);
        RenamePresetCommand = new RelayCommand(RenameSelectedPreset,
            () => !IsBusy && SelectedPreset is not null && !string.IsNullOrWhiteSpace(PresetNameText));
    }

    public IReadOnlyList<AscensionSideViewModel> Sides { get; }

    public AscensionSideViewModel SelectedSide => Sides.Single(s => s.IsSelected);

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
            ClearRowsCommand.RaiseCanExecuteChanged();
            foreach (var side in Sides)
            {
                foreach (var item in side.ActiveItems)
                {
                    item.ClearCommand.RaiseCanExecuteChanged();
                }
            }
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

    /// <summary>
    /// Clear the given rows from the committed table and immediately re-commit the remaining
    /// committed entries (or restore when nothing remains). Command parameter:
    /// IEnumerable&lt;AscensionRowViewModel&gt;.
    /// </summary>
    public RelayCommand<IEnumerable<AscensionRowViewModel>> ClearRowsCommand { get; }

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

    public IEnumerable<AscensionRowViewModel> AllRows() => Sides.SelectMany(s => s.AllRows());

    public IReadOnlyList<AscensionPreset> GetPresetsSnapshot() => Presets.ToArray();

    public void SelectSide(AscensionSide side)
    {
        foreach (var s in Sides)
        {
            s.IsSelected = s.Side == side;
        }
    }

    internal async Task ApplyAsync()
    {
        var pending = AllRows().Where(r => r.IsEnabled).ToList();
        if (pending.Count == 0)
        {
            StatusText = _committed.Count > 0
                ? "没有待应用的修正行；已提交项见生效总览区。"
                : "没有启用任何修正行；先在矩阵里打开开关。";
            _reportStatus(StatusText);
            return;
        }

        var invalid = new List<string>();
        var noScope = new List<string>();
        foreach (var row in pending)
        {
            if (!row.Quantization.IsValid)
            {
                invalid.Add($"{row.SideTitle}·{row.Entry.DisplayName}：{row.Quantization.Message}");
            }
            else if (!row.ScopeSelected && !row.ScopeAll && !row.ScopeNewRegistered)
            {
                noScope.Add($"{row.SideTitle}·{row.Entry.DisplayName}");
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

        // apply.batch REPLACES the whole Agent table, so the commit re-includes every committed
        // entry; pending persistent rows override their (side, type) key. Selected-only rows stay
        // one-shots and never enter the committed state.
        var pendingPersistent = pending.Where(r => r.ScopeAll || r.ScopeNewRegistered).ToList();
        var overridden = pendingPersistent.Select(KeyOf).ToHashSet();
        var merged = _committed
            .Where(kv => !overridden.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var row in pendingPersistent)
        {
            merged[KeyOf(row)] = new CommittedEntry(row.Quantization.WeightValues.ToList(), ScopeMaskOf(row));
        }
        var oneShots = pending.Where(r => r.ScopeSelected).ToList();
        var wireRows = BuildCommittedWireRows(merged).Concat(BuildWireRows(oneShots)).ToList();

        var perSide = string.Join(" · ", Sides
            .Select(s => $"{s.Title} {s.AllRows().Count(r => r.IsEnabled)}"));
        if (!await SubmitWireRowsAsync(wireRows, needsSelectedIds: oneShots.Count > 0))
        {
            return;
        }

        // The committed table now carries the persistent state: pending switches flip back off
        // and the overview/badges become the committed-state display.
        ApplyCommittedState(merged);
        _activeSource = merged.Count > 0 ? "本地提交" : null;
        foreach (var row in pending)
        {
            row.IsEnabled = false;
        }

        StatusText = $"已应用 {pending.Count} 行修正（{perSide}）。";
        if (oneShots.Count > 0)
        {
            StatusText += $"另对选中单位一次性应用 {oneShots.Count} 行（不显示为生效态）。";
        }
        StatusText += $"当前生效 {merged.Count} 项。";
        _reportStatus(StatusText);
    }

    /// <summary>
    /// Remove the given rows from the committed table and re-commit the remaining committed
    /// entries at once. When nothing remains, submit a full restore. One-shot (选中) rows are
    /// deliberately never re-committed here so a clear never re-fires a one-shot against the
    /// current selection.
    /// </summary>
    internal async Task ClearRowsAsync(IEnumerable<AscensionRowViewModel> rowsToClear)
    {
        var targets = rowsToClear.Where(r => _committed.ContainsKey(KeyOf(r))).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var clearedKeys = targets.Select(KeyOf).Distinct().ToHashSet();
        var remaining = _committed
            .Where(kv => !clearedKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (remaining.Count == 0)
        {
            await RestoreAsync();
            StatusText = $"已清除 {targets.Count} 行，无其余生效项，已提交全部还原。";
            _reportStatus(StatusText);
            return;
        }

        if (!await SubmitWireRowsAsync(BuildCommittedWireRows(remaining), needsSelectedIds: false))
        {
            return;
        }
        ApplyCommittedState(remaining);
        var clearedNames = string.Join("、", targets.Select(r => r.Entry.DisplayName).Distinct().Take(3));
        var suffix = clearedKeys.Count > 3 ? "…" : string.Empty;
        StatusText = $"已清除 {clearedNames}{suffix}，剩余 {remaining.Count} 行已重新提交。";
        _reportStatus(StatusText);
    }

    internal async Task ClearSideAsync(AscensionSideViewModel side)
    {
        var committedRows = side.AllRows().Where(r => _committed.ContainsKey(KeyOf(r))).ToList();
        if (committedRows.Count == 0)
        {
            StatusText = $"{side.Title}没有生效中的修正项。";
            _reportStatus(StatusText);
            return;
        }

        StatusText = $"正在清空{side.Title}…";
        await ClearRowsAsync(committedRows);
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

    // Shared submit core: gateway call + busy state + failure status. Success handling
    // (committed-state update, switch reset, success status) belongs to the caller.
    private async Task<bool> SubmitWireRowsAsync(IReadOnlyList<string> wireRows, bool needsSelectedIds)
    {
        IsBusy = true;
        try
        {
            var (success, message) = await _gateway.ApplyAsync(wireRows, needsSelectedIds);
            if (!success)
            {
                StatusText = message;
                _reportStatus(StatusText);
            }
            return success;
        }
        catch (Exception exception)
        {
            StatusText = $"下发属性修改矩阵失败：{exception.Message}";
            _reportStatus(StatusText);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
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
                // The Agent table also carries Selected-only one-shot tuples; the committed
                // state mirrors only the persistent entries (All|NewRegistered bits = 2|4).
                const uint persistentMask = 2u | 4u;
                var committed = new Dictionary<(AscensionSide, int), CommittedEntry>();
                foreach (var group in entries.Where(e => (e.ScopeMask & persistentMask) != 0)
                             .GroupBy(e => (SideFromCode(e.Faction), (int)e.AttributeType)))
                {
                    committed[group.Key] = new CommittedEntry(
                        group.Select(e => (double)BitConverter.UInt32BitsToSingle(e.ValueBits)).ToList(),
                        group.First().ScopeMask & persistentMask);
                }
                ApplyCommittedState(committed);
                _activeSource = committed.Count > 0 ? "读回" : null;
                StatusText = $"读回完成：当前生效 {committed.Count} 项属性修正。";
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
        ApplyCommittedState(new Dictionary<(AscensionSide Side, int Type), CommittedEntry>());
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

    private void ApplyCommittedState(IReadOnlyDictionary<(AscensionSide Side, int Type), CommittedEntry> committed)
    {
        _committed = new Dictionary<(AscensionSide, int), CommittedEntry>(committed);
        foreach (var row in AllRows())
        {
            row.ActiveNote = _committed.TryGetValue(KeyOf(row), out var entry) && entry.TemplateValues.Count > 0
                ? "已生效：" + string.Join("，", entry.TemplateValues.Select(FormatTemplateValue))
                : string.Empty;
        }
        RebuildOverview();
    }

    // Overview lanes mirror the committed state; each chip carries a per-item clear command that
    // re-commits the remaining committed table without that row. The scope text derives from the
    // committed mask (the submit-time truth), not from the row's live chips.
    private void RebuildOverview()
    {
        foreach (var side in Sides)
        {
            side.ActiveItems.Clear();
            foreach (var row in side.AllRows())
            {
                if (!_committed.TryGetValue((side.Side, row.Entry.AttributeType), out var entry) || entry.TemplateValues.Count == 0)
                {
                    continue;
                }
                side.ActiveItems.Add(new AscensionActiveItemViewModel(
                    row.Entry.DisplayName,
                    string.Join("，", entry.TemplateValues.Select(FormatTemplateValue)),
                    ScopeMaskSummary(entry.ScopeMask),
                    new RelayCommand(() => _ = ClearRowsAsync([row]), () => !IsBusy)));
            }
            side.RaiseActiveCountChanged();
        }
    }

    private static string ScopeMaskSummary(uint mask) => string.Join("·", new[]
    {
        (mask & 2u) != 0 ? "全部" : null,
        (mask & 4u) != 0 ? "新注册" : null,
    }.Where(s => s is not null)!);

    private static string FormatTemplateValue(double value) => value >= 0
        ? $"×{value.ToString("0.##", CultureInfo.InvariantCulture)}"
        : value.ToString("0.##", CultureInfo.InvariantCulture);

    // One wire row per quantized template: "attributeType:valueBits:scopeMask:faction"; each row
    // carries the faction code of the side tab it was configured on. The scope mask mirrors the
    // Native AscensionScope bits (Selected=1/All=2/NewRegistered=4).
    internal static IReadOnlyList<string> BuildWireRows(IReadOnlyList<AscensionRowViewModel> enabledRows)
    {
        var rows = new List<string>();
        foreach (var row in enabledRows)
        {
            var scopeMask = ScopeMaskOf(row);
            foreach (var weightValue in row.Quantization.WeightValues)
            {
                var valueBits = BitConverter.SingleToUInt32Bits((float)weightValue);
                rows.Add($"{row.Entry.AttributeType}:{valueBits}:{scopeMask}:{(int)row.Side}");
            }
        }
        return rows;
    }

    // Wire rows straight from the committed model (clear/re-commit path): same row format, but
    // values and scope masks are the submit-time truth, not the rows' live chip state.
    private static IReadOnlyList<string> BuildCommittedWireRows(
        IReadOnlyDictionary<(AscensionSide Side, int Type), CommittedEntry> committed)
    {
        var rows = new List<string>();
        foreach (var kv in committed)
        {
            var (side, type) = kv.Key;
            foreach (var value in kv.Value.TemplateValues)
            {
                rows.Add($"{type}:{BitConverter.SingleToUInt32Bits((float)value)}:{kv.Value.ScopeMask}:{(int)side}");
            }
        }
        return rows;
    }

    private static (AscensionSide Side, int Type) KeyOf(AscensionRowViewModel row) =>
        (row.Side, row.Entry.AttributeType);

    private static uint ScopeMaskOf(AscensionRowViewModel row) =>
        (row.ScopeSelected ? 1u : 0u) | (row.ScopeAll ? 2u : 0u) | (row.ScopeNewRegistered ? 4u : 0u);

    private static AscensionSide SideFromCode(uint factionCode) => factionCode switch
    {
        1 => AscensionSide.Enemy,
        2 => AscensionSide.All,
        _ => AscensionSide.Ally,
    };

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
        PresetStatusText = $"已保存属性预设「{preset.Name}」（{SelectedSide.Title}，{preset.Rows.Count(r => r.IsEnabled)} 行启用）。";
        _persistSettings();
    }

    private void LoadSelectedPreset()
    {
        if (SelectedPreset is null) return;

        var side = Sides.Single(s => (int)s.Side == SelectedPreset.Faction);
        SelectSide(side.Side);
        var savedRows = new Dictionary<int, AscensionPresetRow>();
        foreach (var row in SelectedPreset.Rows)
        {
            savedRows[row.AttributeType] = row;
        }
        foreach (var row in side.AllRows())
        {
            row.RestorePreset(savedRows.GetValueOrDefault(row.Entry.AttributeType));
        }

        PresetStatusText = $"已装载属性预设「{SelectedPreset.Name}」到{side.Title}；确认后点击“应用”下发。";
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

    private AscensionPreset CapturePreset(string name, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        var side = SelectedSide;
        return new AscensionPreset(
            name,
            (int)side.Side,
            side.AllRows()
                .Select(r => r.CapturePreset(_committed.ContainsKey((side.Side, r.Entry.AttributeType))))
                .ToArray(),
            createdAt,
            updatedAt);
    }
}

// One side tab (我方/敌方/全部单位): owns an independent copy of the modifier matrix, its
// enabled-count badge, its active-state overview lane, and its side-level clear command.
public sealed class AscensionSideViewModel : ViewModelBase
{
    private readonly Func<AscensionSideViewModel, Task> _clearSide;
    private bool _isSelected;
    private int _enabledCount;

    public AscensionSideViewModel(AscensionSide side, string title, Func<AscensionSideViewModel, Task> clearSide)
    {
        Side = side;
        Title = title;
        _clearSide = clearSide;
        Groups = BuildGroups(side);
        ActiveItems = [];
        ClearSideCommand = new RelayCommand(() => _ = _clearSide(this));
        foreach (var row in AllRows())
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AscensionRowViewModel.IsEnabled))
                {
                    RaiseEnabledCountChanged();
                }
            };
        }
        RaiseEnabledCountChanged();
    }

    public AscensionSide Side { get; }

    public string Title { get; }

    public IReadOnlyList<AscensionGroupViewModel> Groups { get; }

    public ObservableCollection<AscensionActiveItemViewModel> ActiveItems { get; }

    public RelayCommand ClearSideCommand { get; }

    public int ActiveCount => ActiveItems.Count;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public int EnabledCount => _enabledCount;

    public IEnumerable<AscensionRowViewModel> AllRows() => Groups.SelectMany(g => g.Rows);

    internal void RaiseEnabledCountChanged()
    {
        var count = AllRows().Count(r => r.IsEnabled);
        if (_enabledCount == count)
        {
            return;
        }
        _enabledCount = count;
        OnPropertyChanged(nameof(EnabledCount));
    }

    internal void RaiseActiveCountChanged() => OnPropertyChanged(nameof(ActiveCount));

    private static IReadOnlyList<AscensionGroupViewModel> BuildGroups(AscensionSide side)
    {
        var groups = new List<AscensionGroupViewModel>();
        foreach (var (groupId, title) in AscensionModifierCatalog.Groups)
        {
            var rows = AscensionModifierCatalog.All
                .Where(e => e.GroupId == groupId)
                .Select(e => new AscensionRowViewModel(e, side))
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

// One chip in the active-state overview lane: the committed persistent value plus a per-item
// clear command (re-commit the remaining table without this row).
public sealed class AscensionActiveItemViewModel
{
    public AscensionActiveItemViewModel(string displayName, string valueText, string scopeText, RelayCommand clearCommand)
    {
        DisplayName = displayName;
        ValueText = valueText;
        ScopeText = scopeText;
        ClearCommand = clearCommand;
    }

    public string DisplayName { get; }

    public string ValueText { get; }

    public string ScopeText { get; }

    public RelayCommand ClearCommand { get; }
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

    public AscensionRowViewModel(AscensionModifierEntry entry, AscensionSide side)
    {
        Entry = entry;
        Side = side;
        _quantization = AscensionQuantizer.Quantize(entry.Aggregation, _multiplierText, invert: !_isUp);
    }

    public AscensionModifierEntry Entry { get; }

    public AscensionSide Side { get; }

    public string SideTitle => Side switch
    {
        AscensionSide.Enemy => "敌方",
        AscensionSide.All => "全部单位",
        _ => "我方",
    };

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

    // Scope contract (redesign 2026-08-31): 选中 is a submit-time one-shot and MUTUALLY
    // EXCLUSIVE with the persistent scopes — enabling it drops 全部/新注册, and enabling either
    // persistent scope drops 选中. 全部 + 新注册 stack. The wire scope mask still carries all
    // three bits for Native contract compatibility.
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
            if (value)
            {
                if (_scopeAll)
                {
                    _scopeAll = false;
                    OnPropertyChanged(nameof(ScopeAll));
                }
                if (_scopeNewRegistered)
                {
                    _scopeNewRegistered = false;
                    OnPropertyChanged(nameof(ScopeNewRegistered));
                }
            }
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
            if (value && _scopeSelected)
            {
                _scopeSelected = false;
                OnPropertyChanged(nameof(ScopeSelected));
            }
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
            if (value && _scopeSelected)
            {
                _scopeSelected = false;
                OnPropertyChanged(nameof(ScopeSelected));
            }
        }
    }

    private string _activeNote = string.Empty;

    /// <summary>
    /// Active-state badge text for this row ("已生效：×3，-0.25"); empty when the committed
    /// policy table carries nothing for the row's side + attribute type. Display-only: the truth
    /// is the Agent table (manual read-back reconciles).
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

    // A row counts as enabled when its switch is on OR its config is already committed: apply
    // resets switches, so committed rows would otherwise vanish from a preset saved right after.
    internal AscensionPresetRow CapturePreset(bool committedActive) => new(
        Entry.AttributeType,
        IsEnabled || committedActive,
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
        // Legacy presets may carry mixed scope masks; normalize to the exclusive contract —
        // any persistent scope wins over the one-shot 选中.
        var all = preset?.ScopeAll ?? false;
        var news = preset?.ScopeNewRegistered ?? false;
        ApplyScopeState(preset is not null ? preset.ScopeSelected && !all && !news : true, all, news);
    }

    // Raw scope assignment bypassing the exclusivity setters; preset restore normalization and
    // wire-contract tests use it.
    internal void ApplyScopeState(bool selected, bool all, bool newRegistered)
    {
        void Set(ref bool field, bool value, string name)
        {
            if (field == value)
            {
                return;
            }
            field = value;
            OnPropertyChanged(name);
        }
        Set(ref _scopeAll, all, nameof(ScopeAll));
        Set(ref _scopeNewRegistered, newRegistered, nameof(ScopeNewRegistered));
        Set(ref _scopeSelected, selected, nameof(ScopeSelected));
    }

    private void RecalculateQuantization()
    {
        _quantization = AscensionQuantizer.Quantize(Entry.Aggregation, _multiplierText, invert: !_isUp);
        OnPropertyChanged(nameof(Quantization));
        OnPropertyChanged(nameof(QuantizationText));
    }
}
