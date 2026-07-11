using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows;
using RayaTrainer.App.Services;
using RayaTrainer.App.Web;
using RayaTrainer.App.Views;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Hashing;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Hotkeys;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IFeatureHost, ITrainerPresetSource, IDisposable
{
    // 动作热键的配置 key 使用稳定 RawName。
    private const string ExecuteReinforcementQueueHotkeyName = TrainerFeatureIds.ExecuteReinforcementQueue;
    private const string ReadSelectedUnitCodeHotkeyName = TrainerFeatureIds.ReadSelectedUnitCode;
    private const string GetMeBaseRawName = TrainerFeatureIds.GetBase;
    private const string WeNeedBackRawName = TrainerFeatureIds.Reinforcement;
    private const string CopyForMeRawName = TrainerFeatureIds.CopySelectedUnit;
    private const string SecretProtocolGrantRawName = TrainerFeatureIds.GrantSecretProtocol;
    private const string SelectedObjectUpgradeGrantRawName = TrainerFeatureIds.GrantSelectedObjectUpgrade;
    private const string TemplateModelReplacementRawName = TrainerFeatureIds.ReplaceTemplateModel;
    private const string TemplateWeaponReplacementRawName = TrainerFeatureIds.ReplaceTemplateWeapon;
    private const string SetTargetHealthRawName = TrainerFeatureIds.SetSelectedUnitTargetHealth;
    private readonly TrainerManifest _manifest;
    private readonly TrainerAppSettingsStore _settingsStore;
    private IReadOnlyDictionary<string, string> _hotkeys;
    // 缓存 UI feature 列表（应用 SourceTrainerOverrides 后），供 ReloadHotkeys 重新解析覆盖使用。
    private readonly IReadOnlyList<TrainerFeature> _uiFeatures;
    // 默认热键字典（基于源数据生成），供设置页「恢复默认」使用。
    private readonly IReadOnlyDictionary<string, string> _defaultHotkeys;
    // 增援动作热键：构建逻辑留 MainVM（设计稿契约），命令转发 Reinforcement 子 VM 执行。
    // 运行时热重载时这两个字段会被重新解析，故不设为 readonly。
    private HotkeyGesture? _executeReinforcementQueueHotkey;
    private HotkeyGesture? _readSelectedUnitCodeHotkey;
    private readonly TrainerFeature _getMeBaseFeature;
    private readonly TrainerFeature _weNeedBackFeature;
    private readonly TrainerFeature _copyForMeFeature;
    private readonly TrainerProcessLocator _locator;
    private readonly GameLauncher _launcher = new();
    private readonly HotkeyCoordinator _hotkeyCoordinator;
    private readonly AutoRepairPulseService _autoRepair;
    private readonly GameSessionViewModel _gameSession;
    private readonly ITrainerSessionService _sessionManager;
    private readonly SessionWorkflowViewModel _sessionWorkflow;
    private readonly TargetProcessHeartbeatMonitor _targetHeartbeat;
    private long _targetHeartbeatGeneration;
    private string _statusMessage = "还没有连接游戏。点击上方主按钮开始。";
    private readonly int _attachTimeoutSeconds;
    private bool _isBusy;
    private bool _isQueueRunning;
    private bool _isGameSetupExpanded;
    private bool _hidePrimaryActionCard;
    private string _currentTargetInfo = string.Empty;
    private IReadOnlyList<DetectedRa3Target> _selectableCandidates = Array.Empty<DetectedRa3Target>();

    private MainViewModel(
        TrainerManifest manifest,
        TrainerAppSettingsStore settingsStore,
        IUpdateChecker? updateChecker = null,
        IApplicationVersionProvider? versionProvider = null,
        ITrainerSessionService? sessionManager = null,
        IMobileRemoteLinkProvider? mobileRemoteLinkProvider = null,
        IQrCodeImageFactory? qrCodeImageFactory = null,
        IMobileRemoteAvailability? mobileRemoteAvailability = null,
        TrainerProcessLocator? locator = null)
    {
        _manifest = manifest;
        _settingsStore = settingsStore;
        _sessionManager = sessionManager ?? new TrainerSessionManager();
        _sessionWorkflow = new SessionWorkflowViewModel(_sessionManager);
        _targetHeartbeat = new TargetProcessHeartbeatMonitor();
        _targetHeartbeat.OfflineDetected += OnTargetProcessOffline;
        _locator = locator ?? new TrainerProcessLocator();
        Tools = new ToolsViewModel(updateChecker, versionProvider, mobileRemoteLinkProvider, qrCodeImageFactory, mobileRemoteAvailability, message => StatusMessage = message);
        var uiFeatures = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features);
        _uiFeatures = uiFeatures;
        var panelActions = TrainerFeatureCatalog.CreatePanelActions();
        var secretProtocolGrantFeature = RequirePanelAction(panelActions, SecretProtocolGrantRawName);
        var selectedObjectUpgradeGrantFeature = RequirePanelAction(panelActions, SelectedObjectUpgradeGrantRawName);
        var defaultHotkeys = CreateDefaultHotkeys(uiFeatures);
        _defaultHotkeys = defaultHotkeys;
        var settings = settingsStore.Load(defaultHotkeys);
        var hotkeys = settings.Hotkeys;
        var configuredFeatures = TrainerFeatureCatalog.ApplyHotkeyOverrides(uiFeatures, hotkeys);
        _hotkeys = hotkeys;
        // 预过滤：把选中单位分组的功能分给 SelectedUnitViewModel，
        // 避免 FeatureToggleViewModel 的 GetGroupName fallback 错误分组。
        var selectedUnitGroupNames = TrainerFeatureGroupCatalog.SelectedUnitGroupingNames;
        var selectedUnitFeatures = configuredFeatures
            .Where(f => selectedUnitGroupNames.Contains(f.DisplayName, StringComparer.Ordinal))
            .ToList();
        var selectedUnitNameSet = selectedUnitFeatures
            .Select(f => f.DisplayName)
            .ToHashSet(StringComparer.Ordinal);
        var mainFeatures = configuredFeatures
            .Where(f => !selectedUnitNameSet.Contains(f.DisplayName))
            .ToList();
        GameLaunch = new GameLaunchViewModel(settings, () => IsBusy, message => StatusMessage = message, SaveLauncherSettings);
        _attachTimeoutSeconds = settings.AttachTimeoutSeconds;
        _hidePrimaryActionCard = settings.HidePrimaryActionCard;
        FeatureToggle = new FeatureToggleViewModel(this, mainFeatures, settings);
        SelectedUnit = new SelectedUnitViewModel(this, selectedUnitFeatures);
        var executeReinforcementQueueHotkey = ResolveConfiguredHotkey(hotkeys, ExecuteReinforcementQueueHotkeyName);
        var readSelectedUnitCodeHotkey = ResolveConfiguredHotkey(hotkeys, ReadSelectedUnitCodeHotkeyName);
        var getMeBaseFeature = RequireFeature(configuredFeatures, GetMeBaseRawName);
        var weNeedBackFeature = RequireFeature(configuredFeatures, WeNeedBackRawName);
        var copyForMeFeature = RequireFeature(configuredFeatures, CopyForMeRawName);
        // 提升为字段：CreateActionHotkeyBindings 需在 InstallPatches 时构建动作热键绑定
        _executeReinforcementQueueHotkey = executeReinforcementQueueHotkey;
        _readSelectedUnitCodeHotkey = readSelectedUnitCodeHotkey;
        _getMeBaseFeature = getMeBaseFeature;
        _weNeedBackFeature = weNeedBackFeature;
        _copyForMeFeature = copyForMeFeature;
        Reinforcement = new ReinforcementViewModel(
            () => FeatureController,
            () => ArePatchesInstalled,
            () => IsBusy,
            () => IsQueueRunning,
            v => IsQueueRunning = v,
            message => StatusMessage = message,
            PersistSettings,
            new TemplateReplacementPanelViewModel(
                RequirePanelAction(panelActions, TemplateModelReplacementRawName),
                RequirePanelAction(panelActions, TemplateWeaponReplacementRawName),
                () => FeatureController,
                () => ArePatchesInstalled && !IsQueueRunning,
                message => StatusMessage = message),
            getMeBaseFeature,
            weNeedBackFeature,
            copyForMeFeature,
            executeReinforcementQueueHotkey is null ? "执行队列" : $"执行队列 ({executeReinforcementQueueHotkey.DisplayText})",
            readSelectedUnitCodeHotkey is null ? "读取选中单位" : $"读取选中单位 ({readSelectedUnitCodeHotkey.DisplayText})",
            FormatActionButtonText(getMeBaseFeature, "给玩家基地车"),
            FormatActionButtonText(weNeedBackFeature, "呼叫战场增援"),
            FormatActionButtonText(copyForMeFeature, "复制选中单位"),
            settings);
        SecretProtocol = new SecretProtocolViewModel(
            this,
            () => FeatureController,
            () => ArePatchesInstalled,
            () => IsBusy,
            () => IsQueueRunning,
            v => IsQueueRunning = v,
            message => StatusMessage = message,
            PersistSettings,
            secretProtocolGrantFeature,
            selectedObjectUpgradeGrantFeature,
            configuredFeatures,
            settings);
        Diagnostics = new DiagnosticsViewModel(
            _sessionManager as ITrainerDiagnosticsSource,
            AllFeatures().ToArray(),
            message => StatusMessage = message,
            retrySession: RefreshProcess,
            installPatches: InstallPatches);
        RefreshCommand = new RelayCommand(RefreshProcess);
        RefreshFeatureStatesCommand = new RelayCommand(RefreshFeatureStates);
        SaveLauncherSettingsCommand = new RelayCommand(SaveLauncherSettings);
        LaunchAndLoadCommand = new RelayCommand(() => _ = LaunchAndLoadAsync(), () => !IsBusy);
        StatusBitEditor = new StatusBitEditorPanelViewModel(
            StatusBitCatalog.All,
            ApplySelectedStatusBitAsync,
            () => CanUseStatusEditor && ArePatchesInstalled && !IsQueueRunning,
            message => StatusMessage = message);
        InstallPatchesCommand = new RelayCommand(InstallPatches, () => _sessionManager.CanUseFeatures && !ArePatchesInstalled);
        RestorePatchesCommand = new RelayCommand(RestorePatches);
        SelectCandidateCommand = new RelayCommand<DetectedRa3Target>(SelectCandidate);
        OpenDiagnosticsCommand = new RelayCommand(() => SelectedPageIndex = 6);
        PrimaryActionCommand = new RelayCommand(
            () => _ = ExecutePrimaryActionAsync(),
            () => !IsBusy && !HasSelectableCandidates);
        HidePrimaryActionCardCommand = new RelayCommand(HidePrimaryActionCard);
        Diagnostics.SnapshotChanged += OnDiagnosticsSnapshotChanged;
        GameLaunch.PropertyChanged += OnGameLaunchPropertyChanged;
        Theme = new ThemeViewModel();
        _gameSession = new GameSessionViewModel(
            () => ArePatchesInstalled,
            () => FeatureController,
            message => StatusMessage = message);
        _autoRepair = new AutoRepairPulseService(
            () => ArePatchesInstalled,
            () => FeatureController,
            message => StatusMessage = message);
        _hotkeyCoordinator = new HotkeyCoordinator(() => _sessionManager.IsTargetGameForeground());
        HotkeySettings = new HotkeySettingsViewModel(
            _uiFeatures,
            _hotkeys,
            _defaultHotkeys,
            ReloadHotkeys);
    }

    public static MainViewModel LoadDefault() => Load(TrainerRuntimeAssets.LoadManifest(), new TrainerAppSettingsStore());
    public static MainViewModel Load(
        TrainerManifest manifest,
        TrainerAppSettingsStore settingsStore,
        IUpdateChecker? updateChecker = null,
        IApplicationVersionProvider? versionProvider = null,
        ITrainerSessionService? sessionManager = null,
        IMobileRemoteLinkProvider? mobileRemoteLinkProvider = null,
        IQrCodeImageFactory? qrCodeImageFactory = null,
        IMobileRemoteAvailability? mobileRemoteAvailability = null,
        TrainerProcessLocator? locator = null) => new(
            manifest,
            settingsStore,
            updateChecker,
            versionProvider,
            sessionManager,
            mobileRemoteLinkProvider,
            qrCodeImageFactory,
            mobileRemoteAvailability,
            locator);

    public IReadOnlyList<ReinforcementPreset> GetReinforcementPresets() => Reinforcement.GetReinforcementPresetsSnapshot();

    public IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets() => SecretProtocol.GetSecretProtocolPresetsSnapshot();
    public StatusBitEditorPanelViewModel StatusBitEditor { get; }
    public GameLaunchViewModel GameLaunch { get; }
    public ToolsViewModel Tools { get; }
    public ThemeViewModel Theme { get; }
    public GameSessionViewModel GameSession => _gameSession;
    public FeatureToggleViewModel FeatureToggle { get; }
    public SelectedUnitViewModel SelectedUnit { get; }
    public ReinforcementViewModel Reinforcement { get; }
    public SecretProtocolViewModel SecretProtocol { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public HotkeySettingsViewModel HotkeySettings { get; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RefreshFeatureStatesCommand { get; }
    public RelayCommand SaveLauncherSettingsCommand { get; }
    public RelayCommand LaunchAndLoadCommand { get; }
    public RelayCommand InstallPatchesCommand { get; }
    public RelayCommand RestorePatchesCommand { get; }
    public RelayCommand<DetectedRa3Target> SelectCandidateCommand { get; }
    public RelayCommand OpenDiagnosticsCommand { get; }
    public RelayCommand PrimaryActionCommand { get; }
    public RelayCommand HidePrimaryActionCardCommand { get; }

    public string RefreshProcessHelpText => "立刻扫描 RA3 进程并刷新附加状态；不会安装或恢复 patch。";
    public string InstallPatchesHelpText => "把修改器引导代码和 hook 写入当前 RA3 进程，安装后功能与快捷键才会派发。";
    public string RestorePatchesHelpText => "还原已写入的 hook 和远程状态，关闭当前会话的修改器功能。";
    public string SaveLauncherSettingsHelpText => "保存 RA3 路径、启动参数、自定义 Mods 根目录、资源值、增援预设和快捷键到本地 settings。";
    public string LaunchAndLoadHelpText => "按最终参数启动并装载；参数包含 -ui 时走 RA3.exe，否则直接启动原版或选中/-modConfig 指定的 MOD .game，并在检测到可安装的 RA3 版本后自动安装 patch。";
    public ITrainerFeatureController? FeatureController => _sessionManager.FeatureController;
    public bool CanUseStatusEditor =>
        FeatureController is IAgentFeatureController { SupportsDirectGameApi: true };
    public bool ArePatchesInstalled => _sessionManager.ArePatchesInstalled;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            RaiseCommandStates();
            RaisePrimaryActionState();
        }
    }
    public bool IsQueueRunning { get => _isQueueRunning; private set { _isQueueRunning = value; OnPropertyChanged(); RaiseCommandStates(); } }
    public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

    private int _selectedPageIndex;
    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set
        {
            if (_selectedPageIndex == value)
            {
                return;
            }

            _selectedPageIndex = value;
            OnPropertyChanged();
            Diagnostics.SetActive(value == 5);
        }
    }

    /// <summary>
    /// Structured summary of the currently attached target (version / PID / path / profile / backend),
    /// surfaced as a dedicated bindable field instead of being buried inside <see cref="StatusMessage"/>.
    /// </summary>
    public string CurrentTargetInfo
    {
        get => _currentTargetInfo;
        private set { _currentTargetInfo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Installable RA3 targets the user must choose among when more than one is running.
    /// Populated only for <see cref="TargetSelectionStatus.AmbiguousRequiresUserChoice"/>;
    /// the session is not attached until the user picks one via <see cref="SelectCandidateCommand"/>.
    /// </summary>
    public IReadOnlyList<DetectedRa3Target> SelectableCandidates
    {
        get => _selectableCandidates;
        private set
        {
            _selectableCandidates = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectableCandidates));
            RaisePrimaryActionState();
        }
    }

    public bool HasSelectableCandidates => _selectableCandidates.Count > 0;

    public bool IsGameSetupExpanded
    {
        get => _isGameSetupExpanded;
        set
        {
            if (_isGameSetupExpanded == value)
            {
                return;
            }

            _isGameSetupExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsPrimaryActionCardVisible
    {
        get => !_hidePrimaryActionCard;
        set
        {
            var hidden = !value;
            if (_hidePrimaryActionCard == hidden)
            {
                return;
            }

            _hidePrimaryActionCard = hidden;
            OnPropertyChanged();
        }
    }

    public string PrimaryActionStepText
    {
        get
        {
            if (HasSelectableCandidates)
            {
                return "需要你选择";
            }

            if (_sessionManager.TargetProcessId is null)
            {
                return Diagnostics.Health == TrainerDiagnosticHealth.Error ? "连接遇到问题" : "第 1 步，共 3 步";
            }

            if (!ArePatchesInstalled)
            {
                return "第 2 步，共 3 步";
            }

            return Diagnostics.Health is TrainerDiagnosticHealth.Error or TrainerDiagnosticHealth.Attention
                ? "还需要处理 1 个问题"
                : "准备完成";
        }
    }

    public string PrimaryActionTitle
    {
        get
        {
            if (IsBusy)
            {
                return "正在准备，请稍候…";
            }

            if (HasSelectableCandidates)
            {
                return "请在左侧选择一个游戏";
            }

            if (_sessionManager.TargetProcessId is null)
            {
                if (Diagnostics.Health == TrainerDiagnosticHealth.Error)
                {
                    return "查看为什么没有连接成功";
                }

                return HasConfiguredGamePath ? "启动游戏并自动准备" : "查找已经打开的红警 3";
            }

            if (!ArePatchesInstalled)
            {
                return "启用修改器功能";
            }

            return Diagnostics.Health is TrainerDiagnosticHealth.Error or TrainerDiagnosticHealth.Attention
                ? "查看并解决当前问题"
                : "开始使用修改器";
        }
    }

    public string PrimaryActionDescription
    {
        get
        {
            if (HasSelectableCandidates)
            {
                return "检测到多个游戏进程。点击左侧带版本和 PID 的选项即可继续，修改器不会替你猜。";
            }

            if (_sessionManager.TargetProcessId is null)
            {
                if (Diagnostics.Health == TrainerDiagnosticHealth.Error)
                {
                    return "修改器保留了失败原因。打开诊断后按页面中的修复按钮操作即可。";
                }

                return HasConfiguredGamePath
                    ? "会自动启动游戏、识别版本、连接 DLL Agent 并启用功能。整个过程不需要手动选择技术选项。"
                    : "请先打开红警 3，然后点击按钮。找不到游戏时，下方会自动展开“游戏位置”设置。";
            }

            if (!ArePatchesInstalled)
            {
                return "游戏已经连接。点击后会安装与当前版本匹配的功能组件。";
            }

            return Diagnostics.Health is TrainerDiagnosticHealth.Error or TrainerDiagnosticHealth.Attention
                ? "功能没有完全准备好。诊断页会直接告诉你问题在哪里以及下一步点什么。"
                : "游戏和修改器都已准备好。现在可以在下方选择需要的功能。";
        }
    }

    private bool HasConfiguredGamePath =>
        !string.IsNullOrWhiteSpace(GameLaunch.LauncherPath) && File.Exists(GameLaunch.LauncherPath);

    public int ReinforcementQueueCount => Reinforcement.ReinforcementQueueCount;
    public int SecretProtocolQueueCount => SecretProtocol.SecretProtocolQueueCount;

    public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature) =>
        _sessionManager.GetFeatureCapability(feature);
    private IEnumerable<TrainerFeature> AllFeatures()
    {
        return AllFeatureItems().Select(item => item.Feature);
    }

    private void RaiseAvailabilityChangedForAllFeatures()
    {
        foreach (var item in AllFeatureItems())
        {
            item.RaiseAvailabilityChanged();
        }
    }

    public void RefreshFeatureStates()
    {
        if (FeatureController is null)
        {
            _gameSession.ResetGameState();
            StatusMessage = "无法刷新：请先检测进程并安装 patch。";
            return;
        }

        var count = 0;
        foreach (var feature in AllFeatureItems())
        {
            if (feature.IsToggle)
            {
                feature.RefreshToggleState();
                count++;
            }
        }

        _gameSession.RefreshGameState();
        StatusMessage = $"已刷新 {count} 个功能状态。";
    }

    public void SaveLauncherSettings()
    {
        try { _settingsStore.Save(CurrentSettings()); StatusMessage = "设置已保存。"; }
        catch (Exception ex) { StatusMessage = $"保存启动器路径失败：{ex.Message}"; }
    }

    public void InstallPatches()
    {
        if (!_sessionManager.CanUseFeatures) { StatusMessage = "请先检测进程。"; return; }
        try
        {
            var resourceValues = FeatureToggle.GetResourceValueSettings();
            var installOutcome = _sessionWorkflow.Install(_manifest, DefaultDiagnosticsDirectory(), resourceValues);
            NotifySessionStateChanged();
            RaiseAvailabilityChangedForAllFeatures();
            ActivateInstalledSession();
            StatusMessage = installOutcome.StatusMessage;
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    public void RestorePatches()
    {
        _autoRepair.Stop();
        _gameSession.ResetGameState();
        FeatureToggle.ResetToggleStates();
        SelectedUnit.ResetToggleStates();
        _sessionWorkflow.End(targetOffline: false);
        NotifySessionStateChanged();
        RaiseAvailabilityChangedForAllFeatures();
        StopHotkeys();
        StatusMessage = "Patch 已恢复。";
        RaiseCommandStates();
    }

    public void CompleteActionIfNeeded(TrainerFeature feature, ActionDispatchResult dispatchResult)
        => _gameSession.CompleteActionIfNeeded(feature, dispatchResult);

    internal void OnFeatureToggleChanged(TrainerFeature feature, bool enabled)
    {
        if (!feature.RawName.Equals(AutoRepairPulseService.AutoRepairRawName, StringComparison.Ordinal)) return;
        _autoRepair.SetEnabled(enabled);
    }

    private Task<GameApiDispatchStatus> ApplySelectedStatusBitAsync(StatusBitDefinition definition, bool enabled)
    {
        if (FeatureController is not IAgentFeatureController { SupportsDirectGameApi: true } agentController)
        {
            throw new InvalidOperationException("状态位编辑器需要 DLL Agent 后端。");
        }

        return Task.Run(() => agentController.SetSelectedStatusBit(
            (uint)definition.Domain,
            definition.BitIndex,
            enabled ? 1u : 0u));
    }

    // IFeatureHost 显式实现：协调者按职责委托各子 VM
    bool IFeatureHost.ArePatchesInstalled => ArePatchesInstalled;
    ITrainerFeatureController? IFeatureHost.FeatureController => FeatureController;
    string IFeatureHost.StatusMessage { set => StatusMessage = value; }
    FeatureCapabilitySnapshot IFeatureHost.GetFeatureCapability(TrainerFeature feature) => GetFeatureCapability(feature);
    void IFeatureHost.WriteResourceValuesIfNeeded(TrainerFeature feature) => FeatureToggle.WriteResourceValuesIfNeeded(feature);
    void IFeatureHost.WriteTargetHealthIfNeeded(TrainerFeature feature) => FeatureToggle.WriteTargetHealthIfNeeded(feature);
    void IFeatureHost.OnFeatureToggleChanged(TrainerFeature feature, bool enabled) => OnFeatureToggleChanged(feature, enabled);
    void IFeatureHost.CompleteActionIfNeeded(TrainerFeature feature, ActionDispatchResult result) => CompleteActionIfNeeded(feature, result);
    ReinforcementSettings IFeatureHost.GetReinforcementSettings() => Reinforcement.GetReinforcementSettings();
    void IFeatureHost.OpenHotkeySettings() => SelectedPageIndex = 7;

    void IFeatureHost.ClearHotkey(TrainerFeature feature)
    {
        // 复制当前字典，把目标功能置空，走统一的热重载入口（刷新显示 + 重建 bindings + 持久化）。
        var updated = new Dictionary<string, string>(_hotkeys, StringComparer.Ordinal)
        {
            [feature.RawName] = string.Empty
        };
        ReloadHotkeys(updated);
        StatusMessage = $"已清除「{feature.DisplayName}」的快捷键。";
    }

    public void Dispose()
    {
        _targetHeartbeat.OfflineDetected -= OnTargetProcessOffline;
        _targetHeartbeat.Dispose();
        Diagnostics.SnapshotChanged -= OnDiagnosticsSnapshotChanged;
        GameLaunch.PropertyChanged -= OnGameLaunchPropertyChanged;
        Diagnostics.Dispose();
        DisposeSession();
        _hotkeyCoordinator.Dispose();
        _autoRepair.Dispose();
    }

    private void DisposeSession(bool targetOffline = false)
    {
        _targetHeartbeat.Stop();
        _autoRepair.Stop();
        _gameSession.ResetGameState();
        StopHotkeys();
        FeatureToggle.ResetToggleStates();
        SelectedUnit.ResetToggleStates();
        _sessionWorkflow.End(targetOffline);

        NotifySessionStateChanged();
        RaiseAvailabilityChangedForAllFeatures();
        CurrentTargetInfo = string.Empty;
        SelectableCandidates = Array.Empty<DetectedRa3Target>();
    }

    private void RaiseCommandStates()
    {
        GameLaunch.RaiseCommandStates();
        FeatureToggle.RaiseFeatureCommandStates();
        SelectedUnit.RaiseFeatureCommandStates();
        Reinforcement.RaiseCommandStates();
        SecretProtocol.RaiseCommandStates();
        LaunchAndLoadCommand.RaiseCanExecuteChanged();
        StatusBitEditor.RaiseCommandStates();
        InstallPatchesCommand.RaiseCanExecuteChanged();
        RestorePatchesCommand.RaiseCanExecuteChanged();
        Tools.RaiseCommandStates();
    }

    private void RaiseFeatureCommandStates()
    {
        FeatureToggle.RaiseFeatureCommandStates();
        SelectedUnit.RaiseFeatureCommandStates();
        SecretProtocol.RaiseFeatureCommandStates();
    }

    private void NotifySessionStateChanged()
    {
        OnPropertyChanged(nameof(FeatureController));
        OnPropertyChanged(nameof(ArePatchesInstalled));
        OnPropertyChanged(nameof(CanUseStatusEditor));
        RaiseCommandStates();
        RaisePrimaryActionState();
    }

    private void OnDiagnosticsSnapshotChanged(object? sender, EventArgs e) => RaisePrimaryActionState();

    private void OnGameLaunchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameLaunch.LauncherPath))
        {
            RaisePrimaryActionState();
        }
    }

    private void RaisePrimaryActionState()
    {
        OnPropertyChanged(nameof(PrimaryActionStepText));
        OnPropertyChanged(nameof(PrimaryActionTitle));
        OnPropertyChanged(nameof(PrimaryActionDescription));
        PrimaryActionCommand?.RaiseCanExecuteChanged();
    }

    private void ActivateInstalledSession()
    {
        if (_sessionManager.FeatureController is null)
        {
            return;
        }

        StartHotkeys();
        _autoRepair.Start();
        _gameSession.RefreshGameState();
        // Sync toggle checkboxes with bootstrap-initialized flags (e.g. RunInBackground
        // defaults to on via its defaultBytes initializer) so the UI matches live state.
        FeatureToggle.RefreshToggleStates();
        SelectedUnit.RefreshToggleStates();
    }

    private void OnTargetProcessOffline(object? sender, TargetProcessOfflineEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => HandleTargetProcessOffline(e));
            return;
        }

        HandleTargetProcessOffline(e);
    }

    private void HandleTargetProcessOffline(TargetProcessOfflineEventArgs e)
    {
        if (_sessionManager.TargetProcessId != e.ProcessId ||
            e.Generation != _targetHeartbeatGeneration)
        {
            return;
        }

        (_sessionManager as ITrainerDiagnosticsSource)?.RecordDiagnosticEvent(
            DiagnosticEventSeverity.Warning,
            "target.offline",
            "连续多次未检测到游戏进程，已自动离线。",
            $"PID={e.ProcessId}; misses={e.ConsecutiveFailures}");
        DisposeSession(targetOffline: true);
        StatusMessage = "检测到游戏已经关闭，修改器已自动离线。重新打开游戏后，点击上方主按钮即可继续。";
    }

    private static TrainerFeature RequirePanelAction(IEnumerable<TrainerFeature> panelActions, string rawName)
    {
        return panelActions.Single(feature => feature.RawName.Equals(rawName, StringComparison.Ordinal));
    }

    private static TrainerFeature RequireFeature(IEnumerable<TrainerFeature> features, string rawName)
    {
        return features.Single(feature => feature.RawName.Equals(rawName, StringComparison.Ordinal));
    }

    private TrainerAppSettings CurrentSettings()
    {
        var launch = GameLaunch.GetSettingsSnapshot();
        return new TrainerAppSettings(
            launch.LauncherPath,
            launch.LauncherArguments,
            _attachTimeoutSeconds,
            FeatureToggle.GetResourceValueSettings(),
            GetReinforcementPresets(),
            _hotkeys,
            launch.ModsRootPath,
            launch.SelectedModSkudefPath ?? string.Empty,
            GetSecretProtocolPresets(),
            _hidePrimaryActionCard);
    }

    private void PersistSettings() => _settingsStore.Save(CurrentSettings());

    private void HidePrimaryActionCard()
    {
        IsPrimaryActionCardVisible = false;
        PersistSettings();
    }

    private static string DefaultDiagnosticsDirectory() => Path.Combine(AppContext.BaseDirectory, "artifacts", "diagnostics");

    private void StartHotkeys()
    {
        _hotkeyCoordinator.Start(AllFeatureItems(), CreateActionHotkeyBindings());
    }

    private static IReadOnlyDictionary<string, string> CreateDefaultHotkeys(IReadOnlyList<TrainerFeature> features)
    {
        var hotkeys = new Dictionary<string, string>(TrainerFeatureCatalog.CreateDefaultHotkeys(features), StringComparer.Ordinal) { [ReadSelectedUnitCodeHotkeyName] = "Home", [ExecuteReinforcementQueueHotkeyName] = "Insert" };
        return hotkeys;
    }

    private static HotkeyGesture? ResolveConfiguredHotkey(IReadOnlyDictionary<string, string> hotkeys, string name)
    {
        if (hotkeys.TryGetValue(name, out var hotkey))
            return HotkeyGesture.TryParse(hotkey, out var gesture) ? gesture : null;
        return null;
    }

    private IEnumerable<HotkeyActionBinding> CreateActionHotkeyBindings()
    {
        // 动作热键构建留 MainVM（设计稿契约），命令转发 Reinforcement 子 VM
        // 这三个功能不在 TrainerFeatureGroupCatalog 分组里，没有 feature item binding，
        // 只能通过 action binding 注册热键。AllowRepeat=true 支持长按连续执行。
        if (TryCreateActionHotkeyBinding(_getMeBaseFeature, Reinforcement.GetMeBaseCommand) is { } getMeBaseBinding) yield return getMeBaseBinding with { AllowRepeat = true };
        if (TryCreateActionHotkeyBinding(_weNeedBackFeature, Reinforcement.ExecuteReinforcementCommand) is { } reinforcementBinding) yield return reinforcementBinding with { AllowRepeat = true };
        if (TryCreateActionHotkeyBinding(_copyForMeFeature, Reinforcement.CopySelectedUnitCommand) is { } copyBinding) yield return copyBinding with { AllowRepeat = true };
        if (_executeReinforcementQueueHotkey is not null) yield return new HotkeyActionBinding(_executeReinforcementQueueHotkey, () => Reinforcement.ExecuteReinforcementQueueCommand.Execute(null), () => Reinforcement.ExecuteReinforcementQueueCommand.CanExecute(null));
        if (_readSelectedUnitCodeHotkey is not null) yield return new HotkeyActionBinding(_readSelectedUnitCodeHotkey, () => Reinforcement.ReadSelectedUnitCodeCommand.Execute(null), () => Reinforcement.ReadSelectedUnitCodeCommand.CanExecute(null), AllowRepeat: true);
    }

    private static HotkeyActionBinding? TryCreateActionHotkeyBinding(TrainerFeature feature, RelayCommand command)
    {
        return HotkeyGesture.TryParse(feature.Hotkey, out var gesture)
            ? new HotkeyActionBinding(gesture, () => command.Execute(null), () => command.CanExecute(null))
            : null;
    }

    private static string FormatActionButtonText(TrainerFeature feature, string label)
    {
        return string.IsNullOrWhiteSpace(feature.Hotkey) ? label : $"{label} ({feature.Hotkey})";
    }

    private IEnumerable<FeatureItemViewModel> AllFeatureItems()
    {
        return FeatureToggle.AllFeatureItems()
            .Concat(SelectedUnit.AllFeatureItems())
            .Concat(SecretProtocol.AllFeatureItems());
    }

    private void StopHotkeys() => _hotkeyCoordinator.Stop();

    /// <summary>
    /// 运行时热重载热键：用户在设置页改键后无需重启程序即可生效。
    /// 更新内存字典、刷新所有 UI 显示、若 patch 已安装则重建 dispatcher bindings，并持久化到配置文件。
    /// </summary>
    public void ReloadHotkeys(IReadOnlyDictionary<string, string> newHotkeys)
    {
        _hotkeys = new Dictionary<string, string>(newHotkeys, StringComparer.Ordinal);

        // 重新解析 feature 热键（基于 RawName），并按 RawName 把新值写回每个 FeatureItemViewModel。
        var configured = TrainerFeatureCatalog.ApplyHotkeyOverrides(_uiFeatures, _hotkeys);
        var hotkeyByRawName = configured.ToDictionary(f => f.RawName, f => f.Hotkey, StringComparer.Ordinal);
        foreach (var item in AllFeatureItems())
        {
            if (hotkeyByRawName.TryGetValue(item.Feature.RawName, out var hotkey))
            {
                item.RefreshHotkey(hotkey);
            }
            else
            {
                item.RefreshHotkey(null);
            }
        }

        // 动作热键重新解析，并刷新增援页相关按钮文本。
        _executeReinforcementQueueHotkey = ResolveConfiguredHotkey(_hotkeys, ExecuteReinforcementQueueHotkeyName);
        _readSelectedUnitCodeHotkey = ResolveConfiguredHotkey(_hotkeys, ReadSelectedUnitCodeHotkeyName);
        Reinforcement.ExecuteReinforcementQueueButtonText = _executeReinforcementQueueHotkey is null ? "执行队列" : $"执行队列 ({_executeReinforcementQueueHotkey.DisplayText})";
        Reinforcement.ReadSelectedUnitCodeButtonText = _readSelectedUnitCodeHotkey is null ? "读取选中单位" : $"读取选中单位 ({_readSelectedUnitCodeHotkey.DisplayText})";
        // 三个增援动作功能（给基地车/呼叫增援/复制单位）的按钮文本也随 feature.Hotkey 变化刷新。用重新解析后的 configured feature 取最新 Hotkey。
        var getMeBase = configured.Single(f => f.RawName == GetMeBaseRawName);
        var weNeedBack = configured.Single(f => f.RawName == WeNeedBackRawName);
        var copyForMe = configured.Single(f => f.RawName == CopyForMeRawName);
        Reinforcement.GetMeBaseButtonText = FormatActionButtonText(getMeBase, "给玩家基地车");
        Reinforcement.ExecuteReinforcementButtonText = FormatActionButtonText(weNeedBack, "呼叫战场增援");
        Reinforcement.CopySelectedUnitButtonText = FormatActionButtonText(copyForMe, "复制选中单位");

        // 仅当 patch 已安装（dispatcher 实际在跑）时才重建 bindings，避免对未启动会话误装钩子。
        if (ArePatchesInstalled)
        {
            StopHotkeys();
            StartHotkeys();
        }

        PersistSettings();
    }
}
