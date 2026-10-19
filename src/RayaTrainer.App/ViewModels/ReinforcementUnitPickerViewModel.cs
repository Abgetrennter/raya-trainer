using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels;

public sealed class ReinforcementUnitPickerViewModel : ViewModelBase
{
    private const string AllFactionsName = "全部";

    private readonly List<ReinforcementUnitEntry> _units;
    private readonly string? _customUnitDirectory;
    private string _searchText = string.Empty;
    private ReinforcementUnitGroupViewModel? _selectedMod;
    private ReinforcementUnitGroupViewModel? _selectedFaction;
    private ReinforcementUnitEntry? _selectedUnit;
    private string _statusMessage;

    public ReinforcementUnitPickerViewModel(
        IEnumerable<ReinforcementUnitEntry> units,
        string? customUnitDirectory = null)
    {
        _customUnitDirectory = customUnitDirectory;
        _units = ReinforcementUnitCatalog.Merge(Array.Empty<ReinforcementUnitEntry>(), units).ToList();
        _statusMessage = $"共 {_units.Count} 条单位码。";
        Mods = new ObservableCollection<ReinforcementUnitGroupViewModel>();
        Factions = new ObservableCollection<ReinforcementUnitGroupViewModel>();
        FilteredUnits = new ObservableCollection<ReinforcementUnitEntry>();
        SelectedUnitVariants = new ObservableCollection<ReinforcementUnitVariantViewModel>();
        ConfirmCommand = new RelayCommand(Confirm, () => SelectedUnit is not null);
        CancelCommand = new RelayCommand(Cancel);
        ImportCommand = new RelayCommand(ImportFromFile);
        RefreshGroups();
        RefreshFilter();
    }

    public event Action<bool?>? RequestClose;

    public ObservableCollection<ReinforcementUnitGroupViewModel> Mods { get; }

    public ObservableCollection<ReinforcementUnitGroupViewModel> Factions { get; }

    public ObservableCollection<ReinforcementUnitEntry> FilteredUnits { get; }

    public ObservableCollection<ReinforcementUnitVariantViewModel> SelectedUnitVariants { get; }

    public IReadOnlyList<ReinforcementUnitEntry> Units => _units;

    public bool HasSelectedUnitVariants => SelectedUnitVariants.Count > 1;

    public ReinforcementUnitGroupViewModel? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (ReferenceEquals(_selectedMod, value))
            {
                return;
            }

            _selectedMod = value;
            OnPropertyChanged();
            RefreshFactions();
            RefreshFilter();
        }
    }

    public ReinforcementUnitGroupViewModel? SelectedFaction
    {
        get => _selectedFaction;
        set
        {
            if (ReferenceEquals(_selectedFaction, value))
            {
                return;
            }

            _selectedFaction = value;
            OnPropertyChanged();
            RefreshFilter();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value ?? string.Empty;
            OnPropertyChanged();
            RefreshFilter();
        }
    }

    public ReinforcementUnitEntry? SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            SetSelectedUnit(value, refreshVariants: true);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public RelayCommand ImportCommand { get; }

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand CancelCommand { get; }

    public string SearchHelpText => "按单位名、单位代码、模组或阵营过滤单位代码列表。";

    public string SearchPlaceholderText => "按单位名、单位代码、模组或阵营搜索";

    public string ImportHelpText => $"导入文本或 CSV 单位代码文件，合并到自定义单位代码列表并保存到 {ReinforcementUnitCatalog.CustomFileName}。";

    public string ConfirmHelpText => "把当前选中的单位代码带回主窗口增援单位ID文本框。";

    public string CancelHelpText => "关闭单位列表，不修改主窗口增援单位ID。";

    public void LoadFromFile(string path)
    {
        var result = ReinforcementUnitCatalog.ImportToCustomFile(
            _customUnitDirectory,
            File.ReadLines(path),
            _units);
        if (result.AddedCount == 0 && result.DuplicateCount == 0 && result.InvalidCount == 0)
        {
            StatusMessage = "没有读取到可用单位码。";
            return;
        }

        var merged = ReinforcementUnitCatalog.Merge(_units, result.AddedEntries);
        _units.Clear();
        _units.AddRange(merged);

        RefreshGroups();
        RefreshFilter();
        StatusMessage = $"已导入 {result.AddedCount} 条单位码，跳过重复 {result.DuplicateCount} 条，无效 {result.InvalidCount} 行。已保存到 {ReinforcementUnitCatalog.CustomFileName}。";
    }

    public void SelectUnitVariant(ReinforcementUnitVariantViewModel variant)
    {
        if (!SelectedUnitVariants.Contains(variant))
        {
            return;
        }

        SetSelectedUnit(variant.Unit, refreshVariants: false);
        ClearSelectedUnitVariants();
    }

    private void ImportFromFile()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择单位代码文件",
                Filter = "文本文件 (*.txt;*.csv)|*.txt;*.csv|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                LoadFromFile(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入单位代码失败：{ex.Message}";
        }
    }

    private void RefreshFilter()
    {
        var selectedModName = SelectedMod?.Name;
        var selectedFactionName = SelectedFaction?.Name;
        var scopedUnits = _units.Where(unit =>
            (string.IsNullOrWhiteSpace(selectedModName) || unit.Mod.Equals(selectedModName, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(selectedFactionName) ||
                selectedFactionName.Equals(AllFactionsName, StringComparison.Ordinal) ||
                unit.Faction.Equals(selectedFactionName, StringComparison.Ordinal)));
        var filtered = ReinforcementUnitCatalog.Filter(scopedUnits, SearchText);

        var previouslySelected = SelectedUnit;

        FilteredUnits.Clear();
        foreach (var unit in filtered)
        {
            FilteredUnits.Add(unit);
        }

        if (previouslySelected is not null && !FilteredUnits.Contains(previouslySelected))
        {
            SelectedUnit = null;
        }
    }

    private void SetSelectedUnit(ReinforcementUnitEntry? value, bool refreshVariants)
    {
        if (EqualityComparer<ReinforcementUnitEntry?>.Default.Equals(_selectedUnit, value))
        {
            if (refreshVariants)
            {
                RefreshSelectedUnitVariants(value);
            }

            return;
        }

        _selectedUnit = value;
        OnPropertyChanged(nameof(SelectedUnit));
        ConfirmCommand.RaiseCanExecuteChanged();
        if (refreshVariants)
        {
            RefreshSelectedUnitVariants(value);
        }
    }

    private void RefreshSelectedUnitVariants(ReinforcementUnitEntry? selectedUnit)
    {
        ClearSelectedUnitVariants();
        if (selectedUnit is null)
        {
            return;
        }

        var variants = _units
            .Where(unit =>
                unit.Mod.Equals(selectedUnit.Mod, StringComparison.Ordinal) &&
                unit.Name.Equals(selectedUnit.Name, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(unit.SourceId))
            .OrderBy(unit => unit.SourceId, StringComparer.Ordinal)
            .Select(unit => new ReinforcementUnitVariantViewModel(unit))
            .ToArray();

        if (variants.Length <= 1)
        {
            return;
        }

        foreach (var variant in variants)
        {
            SelectedUnitVariants.Add(variant);
        }

        OnPropertyChanged(nameof(HasSelectedUnitVariants));
    }

    private void ClearSelectedUnitVariants()
    {
        if (SelectedUnitVariants.Count == 0)
        {
            return;
        }

        SelectedUnitVariants.Clear();
        OnPropertyChanged(nameof(HasSelectedUnitVariants));
    }

    private void RefreshGroups()
    {
        var selectedModName = SelectedMod?.Name;
        var modNames = _units
            .Select(unit => unit.Mod)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Mods.Clear();
        foreach (var modName in modNames)
        {
            Mods.Add(new ReinforcementUnitGroupViewModel(modName));
        }

        SelectedMod = Mods.FirstOrDefault(mod => mod.Name.Equals(selectedModName, StringComparison.Ordinal)) ??
            Mods.FirstOrDefault();
        RefreshFactions();
    }

    private void RefreshFactions()
    {
        var selectedFactionName = SelectedFaction?.Name;
        var selectedModName = SelectedMod?.Name;
        var factionNames = _units
            .Where(unit => string.IsNullOrWhiteSpace(selectedModName) ||
                unit.Mod.Equals(selectedModName, StringComparison.Ordinal))
            .Select(unit => unit.Faction)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Factions.Clear();
        if (selectedModName is not null)
        {
            Factions.Add(new ReinforcementUnitGroupViewModel(AllFactionsName));
            foreach (var factionName in factionNames)
            {
                Factions.Add(new ReinforcementUnitGroupViewModel(factionName));
            }
        }

        SelectedFaction = Factions.FirstOrDefault(faction => faction.Name.Equals(selectedFactionName, StringComparison.Ordinal)) ??
            Factions.FirstOrDefault();
    }

    private void Confirm()
    {
        RequestClose?.Invoke(true);
    }

    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

public sealed record ReinforcementUnitGroupViewModel(string Name);

public sealed record ReinforcementUnitVariantViewModel(ReinforcementUnitEntry Unit)
{
    public string SourceId => Unit.SourceId ?? string.Empty;

    public string CodeText => Unit.CodeText;
}
