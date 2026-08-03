using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows;
using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels.FeatureParameterProviders;
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
    // 主控操作热键（全局，RegisterHotKey 注册；与 in-game HotkeyCoordinator 互补）。
    private const string DetectProcessHotkeyName = TrainerFeatureIds.DetectProcess;
    private const string LaunchAndLoadHotkeyName = TrainerFeatureIds.LaunchAndLoad;
    // 游戏内面板显隐热键（默认 F10）：走 in-game HotkeyCoordinator（底层键盘钩子），
    // 发 SetOverlayState 切换面板，在对局内 DirectInput 抢占键盘时仍生效。
    private const string ToggleOverlayHotkeyName = TrainerFeatureIds.ToggleOverlay;
    // Win32 RegisterHotKey id 必须在 0x0000..0xBFFF 区间，且每个 HWND 内唯一。
    private const int DetectProcessGlobalHotkeyId = 0x9001;
    private const int LaunchAndLoadGlobalHotkeyId = 0x9002;
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
    private DurableProductPolicy _durableProductPolicy = DurableProductPolicy.Empty;
    private IReadOnlyDictionary<string, string> _hotkeys;
    // 缓存 UI feature 列表（应用 SourceTrainerOverrides 后），供 ReloadHotkeys 重新解析覆盖使用。
    private readonly IReadOnlyList<TrainerFeature> _uiFeatures;
    // 默认热键字典（基于源数据生成），供设置页「恢复默认」使用。
    private readonly IReadOnlyDictionary<string, string> _defaultHotkeys;
    // 增援动作热键：构建逻辑留 MainVM（设计稿契约），命令转发 Reinforcement 子 VM 执行。
    // 运行时热重载时这两个字段会被重新解析，故不设为 readonly。
    private HotkeyGesture? _executeReinforcementQueueHotkey;
    private HotkeyGesture? _readSelectedUnitCodeHotkey;
    // 主控操作全局热键（立刻检测 / 装载并启动）：游戏未附加时也能触发，
    // 因此走 Win32 RegisterHotKey 路径，与 in-game HotkeyCoordinator 解耦。
    private HotkeyGesture? _detectProcessHotkey;
    private HotkeyGesture? _launchAndLoadHotkey;
    // 游戏内面板显隐动作热键：运行时热重载时重新解析，故不设为 readonly。
    private HotkeyGesture? _toggleOverlayHotkey;
    private GlobalHotkeyService? _globalHotkeyService;
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
    private readonly GameProcessWatcher _autoCaptureWatcher;
    private readonly DispatcherTimer _offlineAdmissionReplayTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };
    private readonly OfflineAdmissionReplayGate _offlineAdmissionReplayGate = new();
    private bool _offlineAdmissionReplayInFlight;
    private long _offlineAdmissionReplayGeneration;
    private bool _autoCaptureEnabled;
    private readonly List<IFeatureParameterProvider> _parameterProviders = new();
    private List<FeaturePreset> _featurePresets = new();
    private long _targetHeartbeatGeneration;
    private string _statusMessage = "还没有连接游戏。点击上方主按钮开始。";
    private readonly int _attachTimeoutSeconds;
    private bool _isBusy;
    private bool _isQueueRunning;
    private bool _isGameSetupExpanded;
    private bool _hidePrimaryActionCard;
    private readonly bool _developerSurfacesVisible;
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
        _durableProductPolicy = settings.DurableProductPolicy;
        _sessionManager = sessionManager ?? new TrainerSessionManager(() => _durableProductPolicy);
        _sessionWorkflow = new SessionWorkflowViewModel(_sessionManager);
        _targetHeartbeat = new TargetProcessHeartbeatMonitor();
        _targetHeartbeat.OfflineDetected += OnTargetProcessOffline;
        _offlineAdmissionReplayTimer.Tick += OnOfflineAdmissionReplayTick;
        _autoCaptureWatcher = new GameProcessWatcher(
            selectTargets: () => _locator.SelectDefault());
        _autoCaptureWatcher.TargetFound += OnAutoCaptureTargetFound;
        _autoCaptureWatcher.AmbiguousCandidatesDetected += OnAutoCaptureAmbiguousCandidates;
        _autoCaptureWatcher.StateChanged += OnAutoCaptureStateChanged;
        _autoCaptureEnabled = settings.AutoCaptureEnabled;
        if (_autoCaptureEnabled)
        {
            _autoCaptureWatcher.Start();
        }

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
        _developerSurfacesVisible = !settings.HideDeveloperSurfaces;
        FeatureToggle = new FeatureToggleViewModel(this, mainFeatures, settings);
        SelectedUnit = new SelectedUnitViewModel(
            this,
            selectedUnitFeatures,
            () => FeatureController,
            f => GetFeatureCapability(f));
        var executeReinforcementQueueHotkey = ResolveConfiguredHotkey(hotkeys, ExecuteReinforcementQueueHotkeyName);
        var readSelectedUnitCodeHotkey = ResolveConfiguredHotkey(hotkeys, ReadSelectedUnitCodeHotkeyName);
        var detectProcessHotkey = ResolveConfiguredHotkey(hotkeys, DetectProcessHotkeyName);
        var launchAndLoadHotkey = ResolveConfiguredHotkey(hotkeys, LaunchAndLoadHotkeyName);
        var getMeBaseFeature = RequireFeature(configuredFeatures, GetMeBaseRawName);
        var weNeedBackFeature = RequireFeature(configuredFeatures, WeNeedBackRawName);
        var copyForMeFeature = RequireFeature(configuredFeatures, CopyForMeRawName);
        // 提升为字段：CreateActionHotkeyBindings 需在 InstallPatches 时构建动作热键绑定
        _executeReinforcementQueueHotkey = executeReinforcementQueueHotkey;
        _readSelectedUnitCodeHotkey = readSelectedUnitCodeHotkey;
        _detectProcessHotkey = detectProcessHotkey;
        _launchAndLoadHotkey = launchAndLoadHotkey;
        _toggleOverlayHotkey = ResolveConfiguredHotkey(hotkeys, ToggleOverlayHotkeyName);
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
            // 保存/删除预设后除持久化外，还要把完整预设快照同步到游戏内控制台投影（R3）。
            () => { PersistSettings(); PublishReinforcementProjection(); },
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
        // 启动时把设置中的预设快照交给协调器缓存；Agent 就绪后自动下发投影。
        PublishReinforcementProjection();
        SecretProtocol = new SecretProtocolViewModel(
            this,
            () => FeatureController,
            () => ArePatchesInstalled,
            () => IsBusy,
            () => IsQueueRunning,
            v => IsQueueRunning = v,
            message => StatusMessage = message,
            // 保存/删除预设后除持久化外，还要把完整预设快照同步到游戏内控制台投影（P3）。
            () => { PersistSettings(); PublishSecretProtocolProjection(); },
            secretProtocolGrantFeature,
            selectedObjectUpgradeGrantFeature,
            configuredFeatures,
            settings);
        // 启动时把设置中的预设快照交给协调器缓存；Agent 就绪后自动下发投影。
        PublishSecretProtocolProjection();
        ProductConsole = new ProductConsoleViewModel(
            _sessionManager as IProductControlSessionHost,
            message => StatusMessage = message,
            _durableProductPolicy,
            policy =>
            {
                _durableProductPolicy = policy;
                PersistSettings();
            });
        InitializePrivateOperationExplorer();
        Diagnostics = new DiagnosticsViewModel(
            _sessionManager as ITrainerDiagnosticsSource,
            AllFeatures().ToArray(),
            message => StatusMessage = message,
            retrySession: RefreshProcess,
            installPatches: InstallPatches);
        RefreshCommand = new RelayCommand(DetectProcessOnDemand);
        RefreshFeatureStatesCommand = new RelayCommand(RefreshFeatureStates);
        SaveLauncherSettingsCommand = new RelayCommand(SaveLauncherSettings);
        LaunchAndLoadCommand = new RelayCommand(() => _ = LaunchAndLoadAsync(), () => !IsBusy);
        StatusBitEditor = new StatusBitEditorPanelViewModel(
            StatusBitCatalog.All,
            ApplySelectedStatusBitAsync,
            () => CanUseStatusEditor && ArePatchesInstalled && !IsQueueRunning,
            message => StatusMessage = message);
        SelectCandidateCommand = new RelayCommand<DetectedRa3Target>(SelectCandidate);
        OpenDiagnosticsCommand = new RelayCommand(() => SelectedPageIndex = 6);
        PrimaryActionCommand = new RelayCommand(
            () => _ = ExecutePrimaryActionAsync(),
            () => !IsBusy && !HasSelectableCandidates);
        HidePrimaryActionCardCommand = new RelayCommand(HidePrimaryActionCard);
        Diagnostics.SnapshotChanged += OnDiagnosticsSnapshotChanged;
        GameLaunch.PropertyChanged += OnGameLaunchPropertyChanged;
        Theme = new ThemeViewModel(settings.IsDarkTheme, () => Persistence?.MarkDirty());
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

        Persistence = new SettingsPersistenceCoordinator(
            captureSnapshot: CurrentSettingsSnapshot,
            onError: err => StatusMessage = err is null ? "" : $"设置保存失败：{err}",
            saveAction: s => _settingsStore.Save(s));

        // 启动恢复：从已加载的 settings 恢复页面/分组折叠/期望开关。
        // Theme 已在上方用 settings.IsDarkTheme 构造，此处不再重建。
        RestoreAppPreferences(settings);
        RestoreDesiredToggles(settings);
        _parameterProviders.Add(new ResourceParameterProvider(
            capture: FeatureToggle.GetResourceValueSettings,
            writeBack: s =>
            {
                FeatureToggle.MoneyAmountText = s.MoneyAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                FeatureToggle.PowerValueText = s.PowerValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                FeatureToggle.ScPointValueText = s.ScPointValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            },
            lastValid: settings.ResourceValues));
        _parameterProviders.Add(new SelectedUnitParameterProvider(
            capture: () => (
                SelectedUnit.SelectedUnitTargetHealthText,
                SelectedUnit.SelectedUnitTargetMaxHealthText),
            writeBack: (health, max) =>
            {
                SelectedUnit.SelectedUnitTargetHealthText = health;
                SelectedUnit.SelectedUnitTargetMaxHealthText = max;
            }));
        _parameterProviders.Add(new TemplateReplacementParameterProvider(
            capture: () => (
                Reinforcement.TemplateReplacement.TargetUnitIdText,
                Reinforcement.TemplateReplacement.DonorUnitIdText),
            writeBack: (target, donor) =>
            {
                Reinforcement.TemplateReplacement.TargetUnitIdText = target;
                Reinforcement.TemplateReplacement.DonorUnitIdText = donor;
            }));
        RestoreParameterValues(settings.FeatureParameterValues, suppressRuntimeApply: true);
        FeatureState = new FeatureStateCoordinator(
            AllFeatureItems,
            () => FeatureController,
            GetFeatureCapability,
            _parameterProviders);

        // 启动恢复预设列表
        _featurePresets = settings.FeaturePresets.ToList();
        LastAppliedFeaturePresetName = settings.LastAppliedFeaturePresetName;

        FeaturePresetsPanel = new FeaturePresetViewModel(this);
        FeaturePresetsPanel.RefreshPresetNames();
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

    IReadOnlyList<FeaturePreset> ITrainerPresetSource.GetFeaturePresets() => _featurePresets.ToList();
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
    public ProductConsoleViewModel ProductConsole { get; }
    public FeatureStateCoordinator FeatureState { get; }
    public FeaturePresetViewModel FeaturePresetsPanel { get; }

    public IReadOnlyList<FeaturePreset> FeaturePresets => _featurePresets;
    public string? LastAppliedFeaturePresetName { get; private set; }

    /// <summary>单写者防抖原子保存协调器。所有偏好变更通过 MarkDirty 触发，退出时 Flush。</summary>
    public SettingsPersistenceCoordinator Persistence { get; }

    /// <summary>最近一次捕获的窗口几何。MainWindow 事件通过 UpdateWindowBounds 更新。</summary>
    public WindowBounds? LastWindowBounds { get; private set; }

    /// <summary>MainWindow 在 LocationChanged/SizeChanged/StateChanged 时调用，更新几何并标记脏。</summary>
    public void UpdateWindowBounds(WindowBounds b)
    {
        LastWindowBounds = b;
        Persistence?.MarkDirty();
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RefreshFeatureStatesCommand { get; }
    public RelayCommand SaveLauncherSettingsCommand { get; }
    public RelayCommand LaunchAndLoadCommand { get; }
    public RelayCommand<DetectedRa3Target> SelectCandidateCommand { get; }
    public RelayCommand OpenDiagnosticsCommand { get; }
    public RelayCommand PrimaryActionCommand { get; }
    public RelayCommand HidePrimaryActionCardCommand { get; }

    public string RefreshProcessHelpText => "立刻扫描 RA3 进程并刷新附加状态；不会安装或恢复 patch。";
    public string SaveLauncherSettingsHelpText => "保存 RA3 路径、启动参数、自定义 Mods 根目录、资源值、增援预设和快捷键到本地 settings。";
    public string LaunchAndLoadHelpText => "按最终参数启动并装载；参数包含 -ui 时走 RA3.exe，否则直接启动原版或选中/-modConfig 指定的 MOD .game，并在检测到可安装的 RA3 版本后自动安装 patch。";

    // 按钮文本与 helpText 不同：这两个按钮配置全局快捷键后，文本末尾会附加 (按键) 提示，
    // 与增援页"执行队列 (Ctrl+Insert)"的呈现方式一致。
    public string RefreshProcessButtonText => _detectProcessHotkey is null ? "立刻检测" : $"立刻检测 ({_detectProcessHotkey.DisplayText})";
    public string LaunchAndLoadButtonText => _launchAndLoadHotkey is null ? "装载并启动" : $"装载并启动 ({_launchAndLoadHotkey.DisplayText})";
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
            Persistence?.MarkDirty();
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

    /// <summary>
    /// 开发者/高级控制面（产品控制台、脚本操作（高级））是否可见。
    /// 启动时由设置决定，运行期不变；切换需编辑设置文件后重启。
    /// </summary>
    public bool IsDeveloperSurfaceVisible => _developerSurfacesVisible;

    public bool IsAutoCaptureEnabled
    {
        get => _autoCaptureEnabled;
        set => SetAutoCaptureEnabled(value);
    }

    public string PrimaryActionStepText
    {
        get
        {
            if (_autoCaptureEnabled && _sessionManager.TargetProcessId is null)
            {
                return _autoCaptureWatcher.CurrentState switch
                {
                    GameWatcherState.Standby => "正在等待红色警戒3启动",
                    GameWatcherState.Attaching => "已检测到游戏",
                    GameWatcherState.AwaitingAmbiguityResolution => "发现多个红色警戒3",
                    GameWatcherState.Rewinding => "正在重试",
                    _ => "正在等待红色警戒3启动",
                };
            }

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
            if (_autoCaptureEnabled && _sessionManager.TargetProcessId is null)
            {
                return _autoCaptureWatcher.CurrentState switch
                {
                    GameWatcherState.Standby => "自动等待游戏启动",
                    GameWatcherState.Attaching => "正在自动连接…",
                    GameWatcherState.AwaitingAmbiguityResolution => "请在下方选择一个",
                    GameWatcherState.Rewinding => "即将重新等待",
                    _ => "自动等待游戏启动",
                };
            }

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
            if (_autoCaptureEnabled && _sessionManager.TargetProcessId is null)
            {
                return _autoCaptureWatcher.CurrentState switch
                {
                    GameWatcherState.Standby => "已开启自动捕获。启动红色警戒3后会自动连接并装载功能，无需手动点击。",
                    GameWatcherState.Attaching => "已检测到红色警戒3，正在自动连接并装载功能。",
                    GameWatcherState.AwaitingAmbiguityResolution => "自动捕获发现多个红色警戒3，请在下方列表中选择一个再继续。",
                    GameWatcherState.Rewinding => "上一次连接中断，马上重新开始等待。",
                    _ => "已开启自动捕获。启动红色警戒3后会自动连接并装载功能，无需手动点击。",
                };
            }

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
        try { Persistence?.Flush(); StatusMessage = "设置已保存。"; }
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
        StopOfflineAdmissionReplay();
        _autoRepair.Stop();
        _gameSession.ResetGameState();
        FeatureToggle.ResetToggleStates();
        SelectedUnit.ResetToggleStates();
        _sessionWorkflow.RestoreRuntime();
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
        Persistence?.MarkDirty();
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
    void IFeatureHost.WriteTargetHealthIfNeeded(TrainerFeature feature) => SelectedUnit.WriteTargetHealthIfNeeded(feature);
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
        _offlineAdmissionReplayTimer.Tick -= OnOfflineAdmissionReplayTick;
        StopOfflineAdmissionReplay();
        _autoCaptureWatcher.TargetFound -= OnAutoCaptureTargetFound;
        _autoCaptureWatcher.AmbiguousCandidatesDetected -= OnAutoCaptureAmbiguousCandidates;
        _autoCaptureWatcher.StateChanged -= OnAutoCaptureStateChanged;
        _autoCaptureWatcher.Dispose();
        _targetHeartbeat.OfflineDetected -= OnTargetProcessOffline;
        _targetHeartbeat.Dispose();
        Diagnostics.SnapshotChanged -= OnDiagnosticsSnapshotChanged;
        GameLaunch.PropertyChanged -= OnGameLaunchPropertyChanged;
        Diagnostics.Dispose();
        DisposeSession();
        _hotkeyCoordinator.Dispose();
        _globalHotkeyService?.Dispose();
        _autoRepair.Dispose();
        Persistence?.Dispose();
    }

    private void DisposeSession()
    {
        StopOfflineAdmissionReplay();
        _targetHeartbeat.Stop();
        _autoRepair.Stop();
        _gameSession.ResetGameState();
        StopHotkeys();
        FeatureToggle.ResetToggleStates();
        SelectedUnit.ResetToggleStates();
        _sessionWorkflow.EndSession();

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

        // L4: Session recovery per plan §2:
        // 1. Initial readback from Agent (populates observed cache)
        var controller = _sessionManager.FeatureController;
        try
        {
            controller.RefreshRuntimeStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort initial readback
        }

        // 2. Replay all explicit desired states (ReplayDesiredState only touches IsToggle items;
        //    pulse features are excluded from replay per plan §2).
        // 3. null Desired states are skipped by ReplayDesiredState.
        FeatureState.ReplayDesiredState();

        // 4. Re-readback after replay to confirm Agent state
        try
        {
            controller.RefreshRuntimeStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort re-readback
        }

        // 5. Update UI from observed cache
        FeatureToggle.RefreshToggleStates();
        SelectedUnit.RefreshToggleStates();
        StartOfflineAdmissionReplay();
    }

    private void StartOfflineAdmissionReplay()
    {
        Interlocked.Increment(ref _offlineAdmissionReplayGeneration);
        _offlineAdmissionReplayGate.Reset();
        _offlineAdmissionReplayTimer.Start();
    }

    private void StopOfflineAdmissionReplay()
    {
        _offlineAdmissionReplayTimer.Stop();
        Interlocked.Increment(ref _offlineAdmissionReplayGeneration);
        _offlineAdmissionReplayGate.Reset();
    }

    private async void OnOfflineAdmissionReplayTick(object? sender, EventArgs e)
    {
        if (_offlineAdmissionReplayInFlight ||
            _sessionManager is not IProductControlSessionHost { ProductControl: { } session } ||
            !session.Negotiation.IsReady)
        {
            return;
        }

        _offlineAdmissionReplayInFlight = true;
        var generation = Volatile.Read(ref _offlineAdmissionReplayGeneration);
        try
        {
            var outcome = await session.QueryMatchContextAsync(
                ScopeMask.CurrentPlayer,
                default,
                TimeSpan.FromSeconds(1));
            if (generation != Volatile.Read(ref _offlineAdmissionReplayGeneration))
            {
                return;
            }

            var context = outcome.Value;
            if (context is null ||
                !_offlineAdmissionReplayGate.ShouldReplay(
                    context.Lifecycle,
                    context.SinglePlayerProof,
                    context.MapEpoch.Value))
            {
                return;
            }

            var controller = _sessionManager.FeatureController;
            if (controller is null)
            {
                return;
            }

            // The Agent revokes all continuing state when proof is lost. Refresh the
            // observable cache first, replay every explicit desired state for this new
            // epoch, then confirm the post-replay state for the UI.
            await controller.RefreshRuntimeStateAsync(CancellationToken.None);
            if (generation != Volatile.Read(ref _offlineAdmissionReplayGeneration) ||
                !ReferenceEquals(controller, _sessionManager.FeatureController))
            {
                return;
            }

            FeatureToggle.RefreshToggleStates();
            SelectedUnit.RefreshToggleStates();
            var replay = FeatureState.ReplayDesiredState();
            await controller.RefreshRuntimeStateAsync(CancellationToken.None);
            if (generation != Volatile.Read(ref _offlineAdmissionReplayGeneration) ||
                !ReferenceEquals(controller, _sessionManager.FeatureController))
            {
                return;
            }

            FeatureToggle.RefreshToggleStates();
            SelectedUnit.RefreshToggleStates();

            _offlineAdmissionReplayGate.MarkReplayed(context.MapEpoch.Value);

            (_sessionManager as ITrainerDiagnosticsSource)?.RecordDiagnosticEvent(
                replay.SkippedOther == 0
                    ? DiagnosticEventSeverity.Info
                    : DiagnosticEventSeverity.Warning,
                "feature_state.readmitted_replay",
                $"离线对局重新准入后已重放期望开关：成功 {replay.Applied}，能力跳过 {replay.SkippedCapability}，失败 {replay.SkippedOther}。",
                $"MapEpoch={context.MapEpoch.Value}");
        }
        catch (Exception ex)
        {
            (_sessionManager as ITrainerDiagnosticsSource)?.RecordDiagnosticEvent(
                DiagnosticEventSeverity.Warning,
                "feature_state.readmitted_replay_failed",
                "离线对局重新准入后未能重放期望开关。",
                ex.Message);
        }
        finally
        {
            _offlineAdmissionReplayInFlight = false;
        }
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
        _autoCaptureWatcher.OnSessionOffline();   // tell watcher to rewind to Standby
        DisposeSession();
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

    /// <summary>
    /// UI 入口（Task 4）— 捕获当前状态作为快照保存
    /// </summary>
    public void SaveFeaturePreset(string name) =>
        SaveFeaturePreset(name, FeatureState.CaptureSnapshot());

    /// <summary>
    /// 接口实现（Task 6）— 接收外部 snapshot（Web 传快照）
    /// </summary>
    public void SaveFeaturePreset(string name, FeatureStateSnapshot snapshot)
    {
        var existing = _featurePresets.FirstOrDefault(
            p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var idx = _featurePresets.IndexOf(existing);
            _featurePresets[idx] = existing with
            {
                Snapshot = snapshot,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
        else
        {
            _featurePresets.Add(new FeaturePreset(name, snapshot,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        Persistence?.MarkDirty();
    }

    public SnapshotApplyResult ApplyFeaturePreset(string name)
    {
        var preset = _featurePresets.FirstOrDefault(
            p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
            return new SnapshotApplyResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var result = FeatureState.ApplySnapshot(preset.Snapshot, suppressRuntimeApply: false);
        LastAppliedFeaturePresetName = name;
        Persistence?.MarkDirty();
        return result;
    }

    public bool RenameFeaturePreset(string oldName, string newName)
    {
        var preset = _featurePresets.FirstOrDefault(
            p => p.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (preset is null) return false;
        if (_featurePresets.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            return false; // 新名冲突
        var idx = _featurePresets.IndexOf(preset);
        _featurePresets[idx] = preset with { Name = newName, UpdatedAtUtc = DateTimeOffset.UtcNow };
        if (LastAppliedFeaturePresetName?.Equals(oldName, StringComparison.OrdinalIgnoreCase) == true)
            LastAppliedFeaturePresetName = newName;
        Persistence?.MarkDirty();
        return true;
    }

    public bool DeleteFeaturePreset(string name)
    {
        var removed = _featurePresets.RemoveAll(
            p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            if (LastAppliedFeaturePresetName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                LastAppliedFeaturePresetName = null;
            Persistence?.MarkDirty();
        }
        return removed > 0;
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
            _hidePrimaryActionCard,
            AutoCaptureEnabled: _autoCaptureEnabled);
    }

    /// <summary>
    /// 供 SettingsPersistenceCoordinator 捕获的完整快照：在 CurrentSettings 基础上叠加
    /// 主题/页面/分组折叠/期望开关/窗口几何/参数值等 v2 偏好字段。
    /// </summary>
    private TrainerAppSettings CurrentSettingsSnapshot()
    {
        var current = CurrentSettings();
        return current with
        {
            IsDarkTheme = Theme.IsDarkTheme,
            WindowBounds = LastWindowBounds,
            SelectedPageId = MapPageIdFromIndex(_selectedPageIndex),
            GroupExpandedStates = CaptureGroupExpandedStates(),
            DesiredToggleStates = CaptureDesiredToggles(),
            FeatureParameterValues = CaptureParameterValues(),
            FeaturePresets = _featurePresets,
            LastAppliedFeaturePresetName = LastAppliedFeaturePresetName,
            DurableProductPolicy = _durableProductPolicy,
            HideDeveloperSurfaces = !_developerSurfacesVisible
        };
    }

    private IReadOnlyDictionary<string, bool> CaptureGroupExpandedStates()
    {
        var dict = new Dictionary<string, bool>();
        foreach (var g in FeatureToggle.Groups.Concat(SelectedUnit.Groups))
            dict[g.GroupId] = g.IsExpanded;
        return dict;
    }

    private IReadOnlyDictionary<string, bool> CaptureDesiredToggles()
    {
        var dict = new Dictionary<string, bool>();
        foreach (var item in AllFeatureItems())
            if (item.DesiredEnabled is bool d)
                dict[item.Feature.RawName] = d;
        return dict;
    }

    private IReadOnlyDictionary<string, string> CaptureParameterValues()
    {
        var dict = new Dictionary<string, string>();
        foreach (var provider in _parameterProviders)
        {
            foreach (var kv in provider.CaptureValidated())
            {
                dict[kv.Key] = kv.Value;
            }
        }
        return dict;
    }

    private void RestoreParameterValues(IReadOnlyDictionary<string, string> values, bool suppressRuntimeApply)
    {
        foreach (var provider in _parameterProviders)
        {
            provider.RestoreValidated(values, suppressRuntimeApply);
        }
    }

    private void RestoreAppPreferences(TrainerAppSettings s)
    {
        // Theme 已在构造函数用 s.IsDarkTheme + MarkDirty 回调构造，此处不再重建。
        var index = MapPageIndexFromId(s.SelectedPageId);
        // 开发者面隐藏时，把指向其页面的持久化索引钳制回首屏，避免落到无入口的页面。
        ClampHiddenDeveloperSurfacePageIndex(ref index);          // 私有实现处理 operation-explorer(9)
        if (!_developerSurfacesVisible && index == PageIds.ToIndex(PageIds.ProductConsole))
            index = 0;                                            // 公共处理 product-console(8)
        SelectedPageIndex = index;
        foreach (var g in FeatureToggle.Groups.Concat(SelectedUnit.Groups))
            if (s.GroupExpandedStates.TryGetValue(g.GroupId, out var exp))
                g.IsExpanded = exp;
    }

    private void RestoreDesiredToggles(TrainerAppSettings s)
    {
        foreach (var item in AllFeatureItems())
            if (s.DesiredToggleStates.TryGetValue(item.Feature.RawName, out var d))
                item.SetDesired(d, suppressApply: true); // 仅记 desired，等 Agent Ready 重放
    }

    private void PersistSettings() => Persistence?.MarkDirty();

    // R3: 把当前完整预设快照推给会话层投影协调器（Agent 未就绪时缓存，就绪后自动同步）。
    private void PublishReinforcementProjection() =>
        (_sessionManager as IReinforcementProjectionPublisher)
            ?.PublishReinforcementPresets(Reinforcement.GetReinforcementPresetsSnapshot());

    // P3: 秘密协议预设快照同样推给独立的第二个投影协调器。
    private void PublishSecretProtocolProjection() =>
        (_sessionManager as ISecretProtocolProjectionPublisher)
            ?.PublishSecretProtocolPresets(SecretProtocol.GetSecretProtocolPresetsSnapshot());

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

    /// <summary>
    /// 初始化全局热键服务。需在主窗口 HWND 创建后（OnSourceInitialized）调用一次。
    /// 在此之前 ReloadHotkeys 仅更新内存状态，不调用 Win32 RegisterHotKey。
    /// </summary>
    public void InitializeGlobalHotkeys(IntPtr hwnd)
    {
        if (_globalHotkeyService is not null)
        {
            return; // 已初始化（例如窗口重建场景），保持首次实例避免重复 hook。
        }
        _globalHotkeyService = new GlobalHotkeyService(hwnd);
        ApplyGlobalHotkeys();
    }

    /// <summary>
    /// 按 _detectProcessHotkey / _launchAndLoadHotkey 当前值重新注册全局热键。
    /// 在 InitializeGlobalHotkeys 和 ReloadHotkeys 中调用。null 表示该热键未分配，跳过注册。
    /// </summary>
    private void ApplyGlobalHotkeys()
    {
        if (_globalHotkeyService is null)
        {
            return;
        }
        _globalHotkeyService.UnregisterAll();

        if (_detectProcessHotkey is not null &&
            !_globalHotkeyService.Register(DetectProcessGlobalHotkeyId, _detectProcessHotkey, OnDetectProcessGlobalHotkey))
        {
            StatusMessage = $"全局热键 {_detectProcessHotkey.DisplayText} 注册失败，可能被其他程序占用；可在设置页改用其他组合。";
        }
        if (_launchAndLoadHotkey is not null &&
            !_globalHotkeyService.Register(LaunchAndLoadGlobalHotkeyId, _launchAndLoadHotkey, OnLaunchAndLoadGlobalHotkey))
        {
            StatusMessage = $"全局热键 {_launchAndLoadHotkey.DisplayText} 注册失败，可能被其他程序占用；可在设置页改用其他组合。";
        }
    }

    private void OnDetectProcessGlobalHotkey()
    {
        if (RefreshCommand.CanExecute(null))
        {
            RefreshCommand.Execute(null);
        }
    }

    private void OnLaunchAndLoadGlobalHotkey()
    {
        if (LaunchAndLoadCommand.CanExecute(null))
        {
            LaunchAndLoadCommand.Execute(null);
        }
    }

    private static IReadOnlyDictionary<string, string> CreateDefaultHotkeys(IReadOnlyList<TrainerFeature> features)
    {
        // 全局热键默认组合选 Ctrl+Alt：避免与 Ctrl+Shift（系统键盘布局切换）、Win+*（系统级）、
        // Alt+letter（菜单助记符）等高频冲突。用户可在设置页改成任何组合。
        var hotkeys = new Dictionary<string, string>(TrainerFeatureCatalog.CreateDefaultHotkeys(features), StringComparer.Ordinal)
        {
            [ReadSelectedUnitCodeHotkeyName] = "Home",
            [ExecuteReinforcementQueueHotkeyName] = "Insert",
            [DetectProcessHotkeyName] = "Ctrl+Alt+D",
            [LaunchAndLoadHotkeyName] = "Ctrl+Alt+L",
            [ToggleOverlayHotkeyName] = "F10",
        };
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
        // 游戏内面板显隐：走 App 底层键盘钩子，绕过对局内 DirectInput；命中即消费该键，
        // 使原生 WM_KEYDOWN F10 处理器收不到，避免主菜单里双重切换互相抵消。
        if (_toggleOverlayHotkey is not null) yield return new HotkeyActionBinding(_toggleOverlayHotkey, () => _sessionManager.ToggleOverlayVisibility(), () => _sessionManager.CanToggleOverlay);
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

        // 主控操作全局热键重新解析、刷新按钮文本、并重新注册到 Win32。
        _detectProcessHotkey = ResolveConfiguredHotkey(_hotkeys, DetectProcessHotkeyName);
        _launchAndLoadHotkey = ResolveConfiguredHotkey(_hotkeys, LaunchAndLoadHotkeyName);
        _toggleOverlayHotkey = ResolveConfiguredHotkey(_hotkeys, ToggleOverlayHotkeyName);
        OnPropertyChanged(nameof(RefreshProcessButtonText));
        OnPropertyChanged(nameof(LaunchAndLoadButtonText));
        ApplyGlobalHotkeys();

        // 仅当 patch 已安装（dispatcher 实际在跑）时才重建 bindings，避免对未启动会话误装钩子。
        if (ArePatchesInstalled)
        {
            StopHotkeys();
            StartHotkeys();
        }

        PersistSettings();
    }

    // 私有构建钩子：Operation Explorer 页（索引 9）只存在于私有构建，
    // 由 Private/ViewModels/MainViewModel.OperationExplorer.cs 实现；公共投影下为空操作。
    partial void InitializePrivateOperationExplorer();
    partial void TryMapPrivatePageIndexFromId(string? pageId, ref int index, ref bool handled);
    partial void TryMapPrivatePageIdFromIndex(int index, ref string pageId, ref bool handled);
    // 隐藏开发者面时，把指向私有页（索引 9）的持久化页索引钳制回首屏。
    partial void ClampHiddenDeveloperSurfacePageIndex(ref int index);

    private int MapPageIndexFromId(string? pageId)
    {
        var index = 0;
        var handled = false;
        TryMapPrivatePageIndexFromId(pageId, ref index, ref handled);
        return handled ? index : PageIds.ToIndex(pageId);
    }

    private string MapPageIdFromIndex(int index)
    {
        var pageId = PageIds.Features;
        var handled = false;
        TryMapPrivatePageIdFromIndex(index, ref pageId, ref handled);
        return handled ? pageId : PageIds.FromIndex(index);
    }
}
