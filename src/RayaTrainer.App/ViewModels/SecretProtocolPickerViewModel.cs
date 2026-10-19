using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using RayaTrainer.Core.Features;

namespace RayaTrainer.App.ViewModels;

public sealed class SecretProtocolPickerViewModel : ViewModelBase
{
    private const string AllFactionsName = "全部";
    private const string OfficialSecretProtocolModName = "原版 RA3";

    private readonly List<SecretProtocolEntry> _protocols;
    private readonly string? _customProtocolDirectory;
    private SecretProtocolTabViewModel? _selectedMod;
    private SecretProtocolTabViewModel? _selectedFaction;
    private SecretProtocolOptionViewModel? _selectedProtocolOption;
    private string _statusMessage;

    public SecretProtocolPickerViewModel(
        IEnumerable<SecretProtocolEntry> protocols,
        string? customProtocolDirectory = null)
    {
        _customProtocolDirectory = customProtocolDirectory;
        _protocols = SecretProtocolCatalog.Merge(Array.Empty<SecretProtocolEntry>(), protocols).ToList();
        _statusMessage = FormatStatusMessage();
        Mods = new ObservableCollection<SecretProtocolTabViewModel>();
        Factions = new ObservableCollection<SecretProtocolTabViewModel>();
        FilteredProtocols = new ObservableCollection<SecretProtocolOptionViewModel>();
        ImportCommand = new RelayCommand(ImportFromFile);
        ConfirmCommand = new RelayCommand(Confirm, () => SelectedProtocol?.CanGrant == true);
        CancelCommand = new RelayCommand(Cancel);
        RefreshGroups();
    }

    public event Action<bool?>? RequestClose;

    public ObservableCollection<SecretProtocolTabViewModel> Mods { get; }

    public ObservableCollection<SecretProtocolTabViewModel> Factions { get; }

    public ObservableCollection<SecretProtocolOptionViewModel> FilteredProtocols { get; }

    public IReadOnlyList<SecretProtocolEntry> Protocols => _protocols;

    public SecretProtocolEntry? SelectedProtocol => SelectedProtocolOption?.Protocol;

    public SecretProtocolTabViewModel? SelectedMod
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

    public SecretProtocolTabViewModel? SelectedFaction
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

    public SecretProtocolOptionViewModel? SelectedProtocolOption
    {
        get => _selectedProtocolOption;
        set
        {
            if (ReferenceEquals(_selectedProtocolOption, value))
            {
                return;
            }

            _selectedProtocolOption = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProtocol));
            ConfirmCommand.RaiseCanExecuteChanged();
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

    public string ImportHelpText => $"导入文本或 CSV 协议文件，合并到自定义协议列表并保存到 {SecretProtocolCatalog.CustomFileName}。";

    public string ConfirmHelpText => "把当前选中的秘密协议带回主窗口秘密协议授予栏。";

    public string CancelHelpText => "关闭秘密协议列表，不修改主窗口。";

    public void LoadFromFile(string path)
    {
        var result = SecretProtocolCatalog.ImportToCustomFile(
            _customProtocolDirectory,
            File.ReadLines(path),
            _protocols);
        if (result.AddedCount == 0 && result.DuplicateCount == 0 && result.InvalidCount == 0)
        {
            StatusMessage = "没有读取到可用协议。";
            return;
        }

        var merged = SecretProtocolCatalog.Merge(_protocols, result.AddedEntries);
        _protocols.Clear();
        _protocols.AddRange(merged);
        RefreshGroups();
        StatusMessage = $"已导入 {result.AddedCount} 条协议，跳过重复 {result.DuplicateCount} 条，无效 {result.InvalidCount} 行。已保存到 {SecretProtocolCatalog.CustomFileName}。";
    }

    private void ImportFromFile()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "导入秘密协议列表",
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
            StatusMessage = $"导入秘密协议失败：{ex.Message}";
        }
    }

    private void RefreshGroups()
    {
        var selectedModName = SelectedMod?.Name;
        var modNames = _protocols
            .Select(protocol => protocol.Mod)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Mods.Clear();
        foreach (var modName in modNames)
        {
            Mods.Add(new SecretProtocolTabViewModel(modName));
        }

        SelectedMod = Mods.FirstOrDefault(mod => mod.Name.Equals(selectedModName, StringComparison.Ordinal)) ??
            Mods.FirstOrDefault();
        RefreshFactions();
        RefreshFilter();

        if (SelectedProtocol is not null && !_protocols.Contains(SelectedProtocol))
        {
            SelectedProtocolOption = null;
        }

        StatusMessage = FormatStatusMessage();
    }

    private void RefreshFactions()
    {
        var selectedFactionName = SelectedFaction?.Name;
        var selectedModName = SelectedMod?.Name;
        var factionNames = _protocols
            .Where(protocol => string.IsNullOrWhiteSpace(selectedModName) ||
                protocol.Mod.Equals(selectedModName, StringComparison.Ordinal))
            .Select(protocol => protocol.Faction)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(FactionOrder)
            .ThenBy(faction => faction, StringComparer.Ordinal)
            .ToArray();

        Factions.Clear();
        if (selectedModName is not null)
        {
            Factions.Add(new SecretProtocolTabViewModel(AllFactionsName));
            foreach (var factionName in factionNames)
            {
                Factions.Add(new SecretProtocolTabViewModel(factionName));
            }
        }

        SelectedFaction = Factions.FirstOrDefault(faction => faction.Name.Equals(selectedFactionName, StringComparison.Ordinal)) ??
            Factions.FirstOrDefault();
    }

    private void RefreshFilter()
    {
        var selectedModName = SelectedMod?.Name;
        var selectedFactionName = SelectedFaction?.Name;
        var filtered = _protocols.Where(protocol =>
            (string.IsNullOrWhiteSpace(selectedModName) || protocol.Mod.Equals(selectedModName, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(selectedFactionName) ||
                selectedFactionName.Equals(AllFactionsName, StringComparison.Ordinal) ||
                protocol.Faction.Equals(selectedFactionName, StringComparison.Ordinal)));

        FilteredProtocols.Clear();
        foreach (var protocol in filtered)
        {
            FilteredProtocols.Add(CreateOption(protocol));
        }

        var currentOption = SelectedProtocol is null ? null : FindOption(SelectedProtocol);
        if (SelectedProtocol is not null && currentOption is null)
        {
            SelectedProtocolOption = null;
        }
        else if (currentOption is not null && !ReferenceEquals(SelectedProtocolOption, currentOption))
        {
            SelectedProtocolOption = currentOption;
        }
    }

    private SecretProtocolOptionViewModel CreateOption(SecretProtocolEntry protocol)
    {
        return new SecretProtocolOptionViewModel(protocol, SelectProtocol);
    }

    private void SelectProtocol(SecretProtocolEntry protocol)
    {
        SelectedProtocolOption = FindOption(protocol);
    }

    private void Confirm()
    {
        RequestClose?.Invoke(true);
    }

    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    private SecretProtocolOptionViewModel? FindOption(SecretProtocolEntry protocol)
    {
        return FilteredProtocols
            .FirstOrDefault(option => EqualityComparer<SecretProtocolEntry>.Default.Equals(option.Protocol, protocol));
    }

    private string FormatStatusMessage()
    {
        var officialCount = _protocols.Count(protocol => protocol.Mod.Equals(OfficialSecretProtocolModName, StringComparison.Ordinal));
        var modCount = _protocols.Count - officialCount;
        return $"官方协议 {officialCount} 条，MOD/额外协议 {modCount} 条。";
    }

    private static int FactionOrder(string faction)
    {
        return faction switch
        {
            "盟军" => 0,
            "苏联" => 1,
            "升阳" => 2,
            _ => 100
        };
    }
}

public sealed record SecretProtocolTabViewModel(string Name);

public sealed class SecretProtocolOptionViewModel
{
    public SecretProtocolOptionViewModel(
        SecretProtocolEntry protocol,
        Action<SecretProtocolEntry> select)
    {
        Protocol = protocol;
        SelectCommand = new RelayCommand(() => select(protocol));
    }

    public SecretProtocolEntry Protocol { get; }

    public string Mod => Protocol.Mod;

    public string Faction => Protocol.Faction;

    public string Name => Protocol.Name;

    public string? PlayerTech => Protocol.PlayerTech;

    public string PlayerTechIdText => Protocol.PlayerTechIdText;

    public string UpgradeText => Protocol.UpgradeText;

    public string GrantStateText => Protocol.CanGrant ? "可授予" : "仅参考";

    public RelayCommand SelectCommand { get; }
}
