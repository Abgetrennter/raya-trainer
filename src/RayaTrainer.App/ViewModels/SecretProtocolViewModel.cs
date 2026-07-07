using System.Collections.ObjectModel;
using System.Windows;
using RayaTrainer.App.Views;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Hashing;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 秘密协议域：协议与技能开关（SecretProtocolToggles）、协议授予、队列、预设、哈希计算、协议选择器。
/// SecretProtocolPage 全部内容归本 VM。SecretProtocolToggles 通过 AllFeatureItems() 参与 MainVM 热键聚合。
/// </summary>
public sealed class SecretProtocolViewModel : ViewModelBase
{
    private const string SuperPowerRawName = TrainerFeatureIds.SuperPower;
    private const string SecretProtocolDependencyBypassRawName = TrainerFeatureIds.SecretProtocolDependencyBypass;
    private const string DisableAllSpRawName = TrainerFeatureIds.DisableAllSecretProtocols;

    private readonly IFeatureHost _host;
    private readonly Func<ITrainerFeatureController?> _getFeatureController;
    private readonly Func<bool> _arePatchesInstalled;
    private readonly Func<bool> _isBusy;
    private readonly Func<bool> _isQueueRunning;
    private readonly Action<bool> _setQueueRunning;
    private readonly Action<string> _setStatus;
    private readonly Action _persistSettings;
    private readonly TrainerFeature _secretProtocolGrantFeature;
    private readonly TrainerFeature _selectedObjectUpgradeGrantFeature;
    private IReadOnlyList<SecretProtocolEntry> _secretProtocols = SecretProtocolCatalog.LoadWithCustomFile();
    private string _secretProtocolNameText = string.Empty;
    private string _secretProtocolPlayerTechIdText = "0x00000000";
    private string _secretProtocolUpgradeIdText = "0x00000000";
    private string _secretProtocolHashSourceText = string.Empty;
    private SecretProtocolEntry? _selectedSecretProtocol;
    private SecretProtocolQueuePreset? _selectedSecretProtocolPreset;
    private string _secretProtocolPresetNameText = string.Empty;

    public SecretProtocolViewModel(
        IFeatureHost host,
        Func<ITrainerFeatureController?> getFeatureController,
        Func<bool> arePatchesInstalled,
        Func<bool> isBusy,
        Func<bool> isQueueRunning,
        Action<bool> setQueueRunning,
        Action<string> setStatus,
        Action persistSettings,
        TrainerFeature secretProtocolGrantFeature,
        TrainerFeature selectedObjectUpgradeGrantFeature,
        IEnumerable<TrainerFeature> configuredFeatures,
        TrainerAppSettings settings)
    {
        _host = host;
        _getFeatureController = getFeatureController;
        _arePatchesInstalled = arePatchesInstalled;
        _isBusy = isBusy;
        _isQueueRunning = isQueueRunning;
        _setQueueRunning = setQueueRunning;
        _setStatus = setStatus;
        _persistSettings = persistSettings;
        _secretProtocolGrantFeature = secretProtocolGrantFeature;
        _selectedObjectUpgradeGrantFeature = selectedObjectUpgradeGrantFeature;

        SecretProtocolToggles = CreateSecretProtocolToggles(configuredFeatures);
        SecretProtocolPresets = new ObservableCollection<SecretProtocolQueuePreset>(settings.SecretProtocolPresets);
        SecretProtocolQueue = new ObservableCollection<SecretProtocolQueueItemViewModel>();
        SecretProtocolQueue.CollectionChanged += (_, _) => { RaiseCommandStates(); OnPropertyChanged(nameof(SecretProtocolQueueCount)); };

        OpenSecretProtocolPickerCommand = new RelayCommand(OpenSecretProtocolPicker, () => !_isBusy() && !_isQueueRunning());
        HashSecretProtocolSourceCommand = new RelayCommand(HashSecretProtocolSource);
        AddCurrentSecretProtocolToQueueCommand = new RelayCommand(AddCurrentSecretProtocolToQueue, () => !_isQueueRunning());
        GrantCurrentSecretProtocolCommand = new RelayCommand(() => _ = GrantCurrentSecretProtocolAsync(), () => _arePatchesInstalled() && _getFeatureController() is not null && !_isQueueRunning());
        GrantSelectedObjectUpgradeCommand = new RelayCommand(() => _ = GrantSelectedObjectUpgradeAsync(), () => _arePatchesInstalled() && _getFeatureController() is not null && !_isQueueRunning());
        GrantSecretProtocolQueueCommand = new RelayCommand(() => _ = GrantSecretProtocolQueueAsync(), () => _arePatchesInstalled() && _getFeatureController() is not null && SecretProtocolQueue.Count > 0 && !_isQueueRunning());
        ClearSecretProtocolQueueCommand = new RelayCommand(ClearSecretProtocolQueue, () => SecretProtocolQueue.Count > 0 && !_isQueueRunning());
        SaveSecretProtocolPresetCommand = new RelayCommand(SaveSecretProtocolPreset, () => !_isQueueRunning());
        ApplySecretProtocolPresetCommand = new RelayCommand(ApplySecretProtocolPreset, () => _selectedSecretProtocolPreset is not null && !_isQueueRunning());
        AppendSecretProtocolPresetCommand = new RelayCommand(AppendSecretProtocolPreset, () => _selectedSecretProtocolPreset is not null && !_isQueueRunning());
    }

    public IReadOnlyList<FeatureItemViewModel> SecretProtocolToggles { get; }
    public ObservableCollection<SecretProtocolQueuePreset> SecretProtocolPresets { get; }
    public ObservableCollection<SecretProtocolQueueItemViewModel> SecretProtocolQueue { get; }
    public int SecretProtocolQueueCount => SecretProtocolQueue.Count;

    public string SecretProtocolNameText { get => _secretProtocolNameText; set { _secretProtocolNameText = value; OnPropertyChanged(); } }
    public string SecretProtocolPlayerTechIdText { get => _secretProtocolPlayerTechIdText; set { _secretProtocolPlayerTechIdText = value; OnPropertyChanged(); } }
    public string SecretProtocolUpgradeIdText { get => _secretProtocolUpgradeIdText; set { _secretProtocolUpgradeIdText = value; OnPropertyChanged(); } }
    public string SecretProtocolHashSourceText { get => _secretProtocolHashSourceText; set { _secretProtocolHashSourceText = value; OnPropertyChanged(); } }
    public SecretProtocolEntry? SelectedSecretProtocol
    {
        get => _selectedSecretProtocol;
        set
        {
            if (EqualityComparer<SecretProtocolEntry?>.Default.Equals(_selectedSecretProtocol, value))
            {
                return;
            }

            _selectedSecretProtocol = value;
            OnPropertyChanged();
            if (value is not null)
            {
                ApplySecretProtocolToGrantFields(value);
            }
            RaiseCommandStates();
        }
    }

    public string SecretProtocolPresetNameText { get => _secretProtocolPresetNameText; set { _secretProtocolPresetNameText = value; OnPropertyChanged(); } }

    public SecretProtocolQueuePreset? SelectedSecretProtocolPreset { get => _selectedSecretProtocolPreset; set { _selectedSecretProtocolPreset = value; OnPropertyChanged(); RaiseCommandStates(); } }

    public RelayCommand OpenSecretProtocolPickerCommand { get; }
    public RelayCommand HashSecretProtocolSourceCommand { get; }
    public RelayCommand AddCurrentSecretProtocolToQueueCommand { get; }
    public RelayCommand GrantCurrentSecretProtocolCommand { get; }
    public RelayCommand GrantSelectedObjectUpgradeCommand { get; }
    public RelayCommand GrantSecretProtocolQueueCommand { get; }
    public RelayCommand ClearSecretProtocolQueueCommand { get; }
    public RelayCommand SaveSecretProtocolPresetCommand { get; }
    public RelayCommand ApplySecretProtocolPresetCommand { get; }
    public RelayCommand AppendSecretProtocolPresetCommand { get; }

    public string OpenSecretProtocolPickerHelpText => "打开官方和 MOD 秘密协议列表；选中后把 PlayerTech/Upgrade ID 写入当前授予栏。";
    public string SecretProtocolNameHelpText => "当前秘密协议授予项名称；从列表选择协议时会自动填入，也可手动命名。";
    public string SecretProtocolPlayerTechIdHelpText => "要授予的 PlayerTech 哈希 ID；可从协议列表自动填入，0 表示不授予 PlayerTech。";
    public string SecretProtocolUpgradeIdHelpText => "要补发的 Upgrade 哈希 ID；可从协议列表自动填入，0 表示不补发 Upgrade。";
    public string SecretProtocolHashSourceHelpText => "输入 PlayerTech_ 或 Upgrade_ 名称后计算 RA3 哈希；PlayerTech 写入 PlayerTech ID，Upgrade 写入 Upgrade ID。";
    public string AddCurrentSecretProtocolToQueueHelpText => "把当前秘密协议授予栏里的 PlayerTech/Upgrade ID 加入列表，不会立即写入游戏。";
    public string GrantCurrentSecretProtocolHelpText => "立即按当前秘密协议授予栏里的 PlayerTech/Upgrade ID 执行；需要进入对局并已安装 patch。";
    public string GrantSelectedObjectUpgradeHelpText => "把当前 Upgrade ID 授予游戏里选中建筑；用于升阳兵营/战车工厂这类单建筑实例升级。";
    public string GrantSecretProtocolQueueHelpText => "按列表顺序授予秘密协议；每项都会写入 PlayerTech，并在有映射时补发 Upgrade。";
    public string ClearSecretProtocolQueueHelpText => "清空秘密协议添加列表，不撤销已经授予的协议。";
    public string SecretProtocolPresetNameHelpText => "保存秘密协议队列预设使用的名称；为空时使用第一个队列条目名称。";
    public string SecretProtocolPresetHelpText => "选择已保存的秘密协议队列预设，可应用（清空并恢复）或追加到当前队列。";
    public string SaveSecretProtocolPresetHelpText => "把当前秘密协议授予队列保存为命名预设；同名预设会被覆盖。";
    public string ApplySecretProtocolPresetHelpText => "清空当前授予队列并恢复选中预设的条目。";
    public string AppendSecretProtocolPresetHelpText => "把选中预设的条目追加到当前授予队列末尾，不覆盖已有条目。";

    /// <summary>供 MainVM.AllFeatureItems 跨 VM 聚合（热键注册/状态刷新）。</summary>
    public IEnumerable<FeatureItemViewModel> AllFeatureItems() => SecretProtocolToggles;

    /// <summary>供 MainVM.ITrainerPresetSource.GetSecretProtocolPresets / CurrentSettings 收集快照。</summary>
    public IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresetsSnapshot() => SecretProtocolPresets.ToArray();

    public void OpenSecretProtocolPicker()
    {
        try
        {
            var picker = new SecretProtocolPickerViewModel(_secretProtocols, AppContext.BaseDirectory);
            var window = new SecretProtocolPickerWindow { Owner = Application.Current?.MainWindow, DataContext = picker };
            var result = window.ShowDialog();
            _secretProtocols = picker.Protocols.ToArray();
            if (result == true && picker.SelectedProtocol is not null)
            {
                ApplySecretProtocolFromPicker(picker.SelectedProtocol);
                _setStatus($"已选择秘密协议：{picker.SelectedProtocol.Faction} - {picker.SelectedProtocol.Name}");
            }
        }
        catch (Exception ex) { _setStatus($"打开秘密协议列表失败：{ex.Message}"); }
    }

    public void ApplySecretProtocolFromPicker(SecretProtocolEntry protocol)
    {
        SelectedSecretProtocol = protocol;
    }

    private void HashSecretProtocolSource()
    {
        var source = SecretProtocolHashSourceText.Trim();
        if (source.Length == 0)
        {
            _setStatus("请输入要计算的 PlayerTech 或 Upgrade 名称。");
            return;
        }

        var hashText = UnitCodeParser.Format(Ra3InstanceIdHash.Compute(source));
        if (source.StartsWith("PlayerTech_", StringComparison.Ordinal))
        {
            SecretProtocolPlayerTechIdText = hashText;
            _setStatus($"已计算 PlayerTech 哈希：{source} = {hashText}");
            return;
        }

        if (source.StartsWith("Upgrade_", StringComparison.Ordinal))
        {
            SecretProtocolUpgradeIdText = hashText;
            _setStatus($"已计算 Upgrade 哈希：{source} = {hashText}");
            return;
        }

        _setStatus($"已计算哈希：{source} = {hashText}。名称不是 PlayerTech_ 或 Upgrade_ 开头，未写入授予栏。");
    }

    private void AddCurrentSecretProtocolToQueue()
    {
        try
        {
            AddSecretProtocolQueueEntry(CreateCurrentSecretProtocolEntry());
        }
        catch (Exception ex)
        {
            _setStatus($"加入秘密协议列表失败：{ex.Message}");
        }
    }

    private async Task GrantCurrentSecretProtocolAsync()
    {
        try
        {
            var protocol = CreateCurrentSecretProtocolEntry();
            await ExecuteSecretProtocolEntriesAsync([new SecretProtocolQueueItemViewModel(protocol, _ => { }, () => false)]);
        }
        catch (Exception ex)
        {
            _setStatus($"秘密协议授予失败：{ex.Message}");
        }
    }

    private async Task GrantSelectedObjectUpgradeAsync()
    {
        var controller = _getFeatureController();
        if (controller is null) { _setStatus("请先检测进程并安装 patch。"); return; }
        try
        {
            var upgradeId = ParseOptionalSecretProtocolId(SecretProtocolUpgradeIdText, nameof(SecretProtocolUpgradeIdText));
            if (upgradeId == 0)
            {
                throw new FormatException("Upgrade ID 不能为 0。");
            }

            controller.WriteSecretProtocolGrantSettings(new SecretProtocolGrantSettings(0, upgradeId));
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                _selectedObjectUpgradeGrantFeature,
                timeout: FeatureDispatchDefaults.Timeout,
                pollInterval: FeatureDispatchDefaults.PollInterval,
                onWaitStatusChanged: status => _setStatus(DispatchWaitStatusText(status, "选中建筑 Upgrade")));
            _setStatus(result == ActionDispatchResult.Consumed
                ? $"已向选中建筑授予 Upgrade：{UnitCodeParser.Format(upgradeId)}。"
                : "选中建筑 Upgrade 授予动作已写入，但尚未被游戏循环消费。");
        }
        catch (Exception ex)
        {
            _setStatus($"选中建筑 Upgrade 授予失败：{ex.Message}");
        }
    }

    private async Task GrantSecretProtocolQueueAsync()
    {
        await ExecuteSecretProtocolEntriesAsync(SecretProtocolQueue);
    }

    private void ClearSecretProtocolQueue() { SecretProtocolQueue.Clear(); _setStatus("秘密协议添加列表已清空。"); }

    private void SaveSecretProtocolPreset()
    {
        try
        {
            if (SecretProtocolQueue.Count == 0)
            {
                _setStatus("秘密协议授予队列为空，无法保存预设。");
                return;
            }

            var entries = SecretProtocolQueue
                .Select(item => SecretProtocolPresetEntry.FromProtocol(item.Protocol))
                .ToArray();
            var name = string.IsNullOrWhiteSpace(_secretProtocolPresetNameText)
                ? SecretProtocolQueue[0].Protocol.Name
                : _secretProtocolPresetNameText.Trim();
            var preset = new SecretProtocolQueuePreset(name, entries);

            var existingIndex = IndexOfSecretProtocolPreset(preset.Name);
            if (existingIndex >= 0) SecretProtocolPresets[existingIndex] = preset;
            else SecretProtocolPresets.Add(preset);
            SelectedSecretProtocolPreset = preset;
            _persistSettings();
            _setStatus($"已保存秘密协议队列预设：{preset.Name}");
        }
        catch (Exception ex) { _setStatus($"保存秘密协议队列预设失败：{ex.Message}"); }
    }

    private void ApplySecretProtocolPreset()
    {
        if (_selectedSecretProtocolPreset is null) return;
        if (_selectedSecretProtocolPreset.Entries.Count == 0)
        {
            _setStatus("选中预设没有条目。");
            return;
        }

        SecretProtocolQueue.Clear();
        foreach (var entry in _selectedSecretProtocolPreset.Entries)
        {
            SecretProtocolQueue.Add(new SecretProtocolQueueItemViewModel(
                entry.ToProtocol(),
                RemoveSecretProtocolQueueItem,
                () => !_isQueueRunning()));
        }

        _setStatus($"已应用秘密协议队列预设：{_selectedSecretProtocolPreset.Name}");
    }

    private void AppendSecretProtocolPreset()
    {
        if (_selectedSecretProtocolPreset is null) return;
        if (_selectedSecretProtocolPreset.Entries.Count == 0)
        {
            _setStatus("选中预设没有条目。");
            return;
        }

        foreach (var entry in _selectedSecretProtocolPreset.Entries)
        {
            SecretProtocolQueue.Add(new SecretProtocolQueueItemViewModel(
                entry.ToProtocol(),
                RemoveSecretProtocolQueueItem,
                () => !_isQueueRunning()));
        }

        _setStatus($"已追加秘密协议队列预设：{_selectedSecretProtocolPreset.Name}（{_selectedSecretProtocolPreset.Entries.Count} 项）");
    }

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

    /// <summary>刷新本 VM 所有命令的 CanExecute + 队列项。供 MainVM.RaiseCommandStates() 委托。</summary>
    public void RaiseCommandStates()
    {
        OpenSecretProtocolPickerCommand.RaiseCanExecuteChanged();
        HashSecretProtocolSourceCommand.RaiseCanExecuteChanged();
        AddCurrentSecretProtocolToQueueCommand.RaiseCanExecuteChanged();
        GrantCurrentSecretProtocolCommand.RaiseCanExecuteChanged();
        GrantSelectedObjectUpgradeCommand.RaiseCanExecuteChanged();
        GrantSecretProtocolQueueCommand.RaiseCanExecuteChanged();
        ClearSecretProtocolQueueCommand.RaiseCanExecuteChanged();
        SaveSecretProtocolPresetCommand.RaiseCanExecuteChanged();
        ApplySecretProtocolPresetCommand.RaiseCanExecuteChanged();
        AppendSecretProtocolPresetCommand.RaiseCanExecuteChanged();
        foreach (var item in SecretProtocolQueue) item.RaiseCommandState();
    }

    /// <summary>刷新 SecretProtocolToggles 命令状态。供 MainVM.RaiseFeatureCommandStates() 委托。</summary>
    public void RaiseFeatureCommandStates()
    {
        foreach (var item in SecretProtocolToggles) item.RaiseCommandState();
    }

    private void ApplySecretProtocolToGrantFields(SecretProtocolEntry protocol)
    {
        SecretProtocolNameText = protocol.Name;
        SecretProtocolPlayerTechIdText = UnitCodeParser.Format(protocol.PlayerTechId);
        SecretProtocolUpgradeIdText = UnitCodeParser.Format(protocol.UpgradeId);
    }

    private SecretProtocolEntry CreateCurrentSecretProtocolEntry()
    {
        var playerTechId = ParseOptionalSecretProtocolId(SecretProtocolPlayerTechIdText, nameof(SecretProtocolPlayerTechIdText));
        var upgradeId = ParseOptionalSecretProtocolId(SecretProtocolUpgradeIdText, nameof(SecretProtocolUpgradeIdText));
        if (playerTechId == 0 && upgradeId == 0)
        {
            throw new FormatException("PlayerTech ID 和 Upgrade ID 不能同时为 0。");
        }

        var name = string.IsNullOrWhiteSpace(SecretProtocolNameText)
            ? $"{UnitCodeParser.Format(playerTechId)}/{UnitCodeParser.Format(upgradeId)}"
            : SecretProtocolNameText.Trim();
        return new SecretProtocolEntry("手动", "自定义", name, null, null, null, playerTechId, upgradeId);
    }

    private static uint ParseOptionalSecretProtocolId(string text, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (!UnitCodeParser.TryParse(text, out var value))
        {
            throw new FormatException($"{parameterName} must be a hexadecimal value.");
        }

        return value;
    }

    private void AddSecretProtocolQueueEntry(SecretProtocolEntry protocol) { SecretProtocolQueue.Add(new SecretProtocolQueueItemViewModel(protocol, RemoveSecretProtocolQueueItem, () => !_isQueueRunning())); _setStatus($"已加入秘密协议添加列表：{protocol.Faction} - {protocol.Name}"); }

    private void RemoveSecretProtocolQueueItem(SecretProtocolQueueItemViewModel item) => SecretProtocolQueue.Remove(item);

    private async Task ExecuteSecretProtocolEntriesAsync(IEnumerable<SecretProtocolQueueItemViewModel> items)
    {
        var controller = _getFeatureController();
        if (controller is null) { _setStatus("请先检测进程并安装 patch。"); return; }
        var queueItems = items.ToArray();
        if (queueItems.Length == 0) return;
        _setQueueRunning(true);
        try
        {
            foreach (var item in queueItems)
            {
                item.Status = "执行中";
                item.Message = string.Empty;
            }

            var results = await SecretProtocolQueueRunner.ExecuteAsync(
                queueItems.Select(item => item.ToEntry()),
                controller,
                _secretProtocolGrantFeature,
                FeatureDispatchDefaults.Timeout,
                FeatureDispatchDefaults.PollInterval,
                onWaitStatusChanged: status => _setStatus(DispatchWaitStatusText(status, "秘密协议")));
            for (var index = 0; index < results.Count; index++)
            {
                queueItems[index].ApplyResult(results[index]);
            }

            var executed = results.Count(result => result.Status == SecretProtocolQueueItemStatus.Executed);
            _setStatus($"秘密协议授予完成：成功 {executed}/{queueItems.Length}。");
        }
        catch (Exception ex) { _setStatus($"秘密协议授予失败：{ex.Message}"); }
        finally { _setQueueRunning(false); }
    }

    private int IndexOfSecretProtocolPreset(string name)
    {
        for (var index = 0; index < SecretProtocolPresets.Count; index++)
            if (SecretProtocolPresets[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private IReadOnlyList<FeatureItemViewModel> CreateSecretProtocolToggles(IEnumerable<TrainerFeature> features)
    {
        var items = features.Select(feature => new FeatureItemViewModel(feature, _host)).ToArray();
        return
        [
            RequireFeatureItem(items, SuperPowerRawName),
            RequireFeatureItem(items, SecretProtocolDependencyBypassRawName),
            RequireFeatureItem(items, DisableAllSpRawName)
        ];
    }

    private static FeatureItemViewModel RequireFeatureItem(IEnumerable<FeatureItemViewModel> items, string rawName)
    {
        return items.Single(item => item.Feature.RawName.Equals(rawName, StringComparison.Ordinal));
    }
}
