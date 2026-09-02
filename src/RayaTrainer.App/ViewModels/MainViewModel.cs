using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows;
using RayaTrainer.App.Services;
using RayaTrainer.Host.Services;
using RayaTrainer.App.ViewModels.FeatureParameterProviders;
using RayaTrainer.Host.Web;
using RayaTrainer.App.Views;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Errors;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Hashing;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Hotkeys;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.RuntimeAssets.AttributeModifiers;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IFeatureHost, IDisposable
{
    // 动作热键的配置 key 使用稳定 RawName；具体动作在 _actionHotkeyDefinitions 声明表登记。
    // Win32 RegisterHotKey id 必须在 0x0000..0xBFFF 区间，且每个 HWND 内唯一；
    // 全局动作按声明表顺序从该基址分配。
    private const int GlobalHotkeyIdBase = 0x9001;
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
    // 动作热键声明表：新增主要按键只需在此加一条记录，默认值、设置页行、冲突检测、
    // 绑定、热重载与全局注册自动生效。委托经 lambda 延迟解引用子 VM（构造顺序在本表之后），
    // 实际触发时机均在会话建立后。
    private readonly IReadOnlyList<ActionHotkeyDefinition> _actionHotkeyDefinitions;
    // feature 命令覆盖表：不在分组目录、无功能卡片的动作（给基地车/呼叫增援/复制单位），
    // 设置页行与默认键走 feature 管线，这里只登记命令绑定与按钮文本刷新。
    private readonly IReadOnlyList<FeatureCommandOverride> _featureCommandOverrides;
    private GlobalHotkeyService? _globalHotkeyService;
    private readonly TrainerProcessLocator _locator;
    private readonly GameLauncher _launcher = new();
    private readonly HotkeyCoordinator _hotkeyCoordinator;
    private readonly AutoRepairPulseService _autoRepair;
    private readonly GameSessionViewModel _gameSession;
    private readonly ITrainerSessionService _sessionManager;
    private readonly SessionWorkflowViewModel _sessionWorkflow;
    private readonly TargetProcessHeartbeatMonitor _targetHeartbeat;
    private readonly GameProcessWatcher _autoCaptureWatcher;
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
    // 兼容字段：App 已不消费 EnableWebControl（Web 拆到可选组件 WebMini），
    // 但保存设置时原样写回，保证老设置文件零迁移、不丢用户选择。
    private readonly bool _enableWebControl;
    private string _currentTargetInfo = string.Empty;
    private IReadOnlyList<DetectedRa3Target> _selectableCandidates = Array.Empty<DetectedRa3Target>();

    private MainViewModel(
        TrainerManifest manifest,
        TrainerAppSettingsStore settingsStore,
        IUpdateChecker? updateChecker = null,
        IApplicationVersionProvider? versionProvider = null,
        ITrainerSessionService? sessionManager = null,
        TrainerProcessLocator? locator = null)
    {
        _manifest = manifest;
        _settingsStore = settingsStore;
        _locator = locator ?? new TrainerProcessLocator();
        var uiFeatures = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features);
        _uiFeatures = uiFeatures;
        var panelActions = TrainerFeatureCatalog.CreatePanelActions();
        var secretProtocolGrantFeature = RequirePanelAction(panelActions, SecretProtocolGrantRawName);
        var selectedObjectUpgradeGrantFeature = RequirePanelAction(panelActions, SelectedObjectUpgradeGrantRawName);
        _actionHotkeyDefinitions = CreateActionHotkeyDefinitions();
        _featureCommandOverrides = CreateFeatureCommandOverrides();
        var defaultHotkeys = CreateDefaultHotkeys(uiFeatures);
        _defaultHotkeys = defaultHotkeys;
        var settings = settingsStore.Load(defaultHotkeys);
        _durableProductPolicy = settings.DurableProductPolicy;
        _enableWebControl = settings.EnableWebControl;
        Tools = new ToolsViewModel(
            updateChecker,
            versionProvider,
            message => StatusMessage = message);
        _sessionManager = sessionManager ?? new TrainerSessionManager(() => _durableProductPolicy);
        _sessionWorkflow = new SessionWorkflowViewModel(_sessionManager);
        _targetHeartbeat = new TargetProcessHeartbeatMonitor();
        _targetHeartbeat.OfflineDetected += OnTargetProcessOffline;
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
        var getMeBaseFeature = RequireFeature(configuredFeatures, GetMeBaseRawName);
        var weNeedBackFeature = RequireFeature(configuredFeatures, WeNeedBackRawName);
        var copyForMeFeature = RequireFeature(configuredFeatures, CopyForMeRawName);
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
            FormatActionHotkeyText(TrainerFeatureIds.ExecuteReinforcementQueue, "执行队列"),
            FormatActionHotkeyText(TrainerFeatureIds.ReadSelectedUnitCode, "读取选中单位"),
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
        Ascension = new AscensionViewModel(
            message => StatusMessage = message,
            new AscensionSubmitGateway(
                () => (_sessionManager as IProductControlSessionHost)?.ProductControl,
                () => _sessionManager.TargetProcessId),
            settings.AscensionPresets,
            PersistSettings);
        // 把声明表动作热键的当前组合同步到各按钮文本（热重载外的初始呈现）。
        // 需在 Reinforcement/Ascension 就绪后执行。
        RefreshActionHotkeyTexts();
        Diagnostics = new DiagnosticsViewModel(
            _sessionManager as ITrainerDiagnosticsSource,
            AllFeatures().ToArray(),
            message => StatusMessage = message,
            retrySession: () => _ = RefreshProcessAsync(),
            installPatches: InstallPatches);
        RefreshCommand = new RelayCommand(() => _ = DetectProcessOnDemandAsync(), () => !IsBusy);
        RefreshFeatureStatesCommand = new RelayCommand(RefreshFeatureStates);
        SaveLauncherSettingsCommand = new RelayCommand(SaveLauncherSettings);
        LaunchAndLoadCommand = new RelayCommand(() => _ = LaunchAndLoadAsync(), () => !IsBusy);
        StatusBitEditor = new StatusBitEditorPanelViewModel(
            StatusBitCatalog.All,
            ApplySelectedStatusBitAsync,
            () => CanUseStatusEditor && ArePatchesInstalled && !IsQueueRunning,
            message => StatusMessage = message);
        SelectCandidateCommand = new RelayCommand<DetectedRa3Target>(SelectCandidate);
        OpenDiagnosticsCommand = new RelayCommand(
            () => SelectedPageIndex = PageIds.ToIndex(PageIds.Diagnostics));
        PrimaryActionCommand = new RelayCommand(
            () => _ = ExecutePrimaryActionAsync(),
            () => !IsBusy && !HasSelectableCandidates);
        HidePrimaryActionCardCommand = new RelayCommand(HidePrimaryActionCard);
        Diagnostics.SnapshotChanged += OnDiagnosticsSnapshotChanged;
        GameLaunch.PropertyChanged += OnGameLaunchPropertyChanged;
        // 子 VM 的队列计数变更转发到本层（侧边栏徽章/战术条绑定的是 MainViewModel 属性）。
        Reinforcement.PropertyChanged += OnReinforcementPropertyChanged;
        SecretProtocol.PropertyChanged += OnSecretProtocolPropertyChanged;
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
            _actionHotkeyDefinitions,
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
        _parameterProviders.Add(new SelectedUnitMultiplierParameterProvider(
            capture: () => SelectedUnit.CaptureMultiplierTexts(),
            writeBack: values => SelectedUnit.RestoreMultiplierTexts(values)));
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
        TrainerProcessLocator? locator = null) => new(
            manifest,
            settingsStore,
            updateChecker,
            versionProvider,
            sessionManager,
            locator);

    public IReadOnlyList<ReinforcementPreset> GetReinforcementPresets() => Reinforcement.GetReinforcementPresetsSnapshot();

    public IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets() => SecretProtocol.GetSecretProtocolPresetsSnapshot();

    public IReadOnlyList<FeaturePreset> GetFeaturePresets() => _featurePresets.ToList();
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
    public AscensionViewModel Ascension { get; }
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
    public string RefreshProcessButtonText => FormatActionHotkeyText(TrainerFeatureIds.DetectProcess, "立刻检测");
    public string LaunchAndLoadButtonText => FormatActionHotkeyText(TrainerFeatureIds.LaunchAndLoad, "装载并启动");
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
            Diagnostics.SetActive(value == PageIds.ToIndex(PageIds.Diagnostics));
            if (value == PageIds.ToIndex(PageIds.Ascension))
            {
                // Re-entering the matrix reconciles its displayed active state against the
                // Agent (a match change clears the committed table; the badge must follow).
                Ascension.NotifyPageShown();
            }
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

    public bool HasReinforcementQueue => Reinforcement.ReinforcementQueueCount > 0;
    public bool HasSecretProtocolQueue => SecretProtocol.SecretProtocolQueueCount > 0;

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
    void IFeatureHost.OpenHotkeySettings(string? targetRawName)
    {
        // 页面导航一律经 PageIds 解析索引，不硬编码数字（侧边栏插页时硬编码会静默跳错页）。
        SelectedPageIndex = PageIds.ToIndex(PageIds.HotkeySettings);
        HotkeySettings.RequestReveal(targetRawName);
    }

    // ProductIntent 行为执行：经产品控制会话提交绑定的 Product Intent，与 Overlay/Web
    // 同一执行路由（统一属性修改体系阶段 D）。自定义倍率产品附带共享输入框的
    // 整型参数，先用 AttributeBitWeightCodec 预校验，与 Native 路由同一合同。
    async Task<(bool Success, string Message)> IFeatureHost.ExecuteProductIntentFeatureAsync(TrainerFeature feature)
    {
        var binding = TrainerFeatureBehaviorCatalog.TryGetBehavior(feature.RawName)?.AsProductIntent();
        if (binding is null)
        {
            return (false, "该功能不是产品意图入口。");
        }

        if (_sessionManager is not IProductControlSessionHost { ProductControl: { } session })
        {
            return (false, "尚未连接游戏，请先检测进程并安装 patch。");
        }

        IReadOnlyList<ScriptValue> parameters = [];
        if (AttributeBitWeightCodec.TryGetCustomMultiplierKind(feature.RawName, out var multiplierKind))
        {
            var text = SelectedUnit.GetMultiplierText(feature.RawName).Trim();
            if (!int.TryParse(
                    text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var multiplier))
            {
                return (false, "请先在该功能的倍率输入框填写整数倍率。");
            }
            if (!AttributeBitWeightCodec.TryCompose(multiplierKind, multiplier, out _, out var error))
            {
                return (false, error);
            }
            parameters = [ScriptValue.Integer(multiplier)];
        }

        // 生产升星授予（精兵学院）带 level 参数：1=老兵/2=精英/3=英雄，
        // 预校验与 Native 侧 InvalidRequest 规则一致（GameObjectSpawnProductIntent.cpp）。
        if (feature.RawName == TrainerFeatureIds.ProductVeterancyGrant)
        {
            var text = FeatureToggle.VeterancyLevelText.Trim();
            if (!int.TryParse(
                    text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var level) ||
                level < 1 || level > 3)
            {
                return (false, "请先在生产升星授予的等级输入框填写 1（老兵）、2（精英）或 3（英雄）。");
            }

            parameters = [ScriptValue.Integer(level)];
        }

        // 生成矿脉参数是开口方向预设（1/3/5/7 = 东北/西北/西南/东南，见 Native 的
        // kOreNodePlacementPresets）；预校验与 Native 的 InvalidRequest 条件一致
        // （GameObjectSpawnProductIntent.cpp）。UI 只暴露官方角度集合（45° 奇数倍）；
        // 停靠几何共享烘焙缓存的已知限制见 docs/project-status.md 悬挂工作。
        if (feature.RawName == TrainerFeatureIds.ProductSpawnOreNode)
        {
            var preset = FeatureToggle.OreNodeAnglePreset;
            if (preset is not (1 or 3 or 5 or 7))
            {
                return (false, "生成矿脉的开口方向必须是 1（东北）、3（西北）、5（西南）或 7（东南）。");
            }

            parameters = [ScriptValue.Integer(preset)];
        }

        // Captured-bound products (all selected-unit attribute modifiers) need the submit-time
        // selection as engine ObjectIDs; read it from the Agent's published Match Context
        // snapshot via command 70 before composing the intent.
        IReadOnlyList<uint>? capturedObjectIds = null;
        if (ProductCatalogProjection.TryGetPublic(binding.Value.ProductId, out var catalogEntry) &&
            catalogEntry.Binding == BindingKind.Captured)
        {
            if (_sessionManager.TargetProcessId is not int targetProcessId)
            {
                return (false, "尚未连接游戏进程。");
            }

            try
            {
                var idsPayload = await new AgentNamedPipeClient()
                    .GetSelectedObjectIdsAsync(targetProcessId, TimeSpan.FromSeconds(2))
                    .ConfigureAwait(true);
                capturedObjectIds = idsPayload.ObjectIds;
            }
            catch (Exception exception)
            {
                return (false, $"读取游戏内选中对象失败：{exception.Message}");
            }
        }

        var submission = await ProductFeatureSubmitter
            .SubmitAsync(session, binding.Value.ProductId, parameters, capturedObjectIds)
            .ConfigureAwait(true);
        return (submission.Success, submission.Message);
    }

    private void OnReinforcementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Reinforcement.ReinforcementQueueCount))
        {
            OnPropertyChanged(nameof(ReinforcementQueueCount));
            OnPropertyChanged(nameof(HasReinforcementQueue));
        }
    }

    private void OnSecretProtocolPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecretProtocol.SecretProtocolQueueCount))
        {
            OnPropertyChanged(nameof(SecretProtocolQueueCount));
            OnPropertyChanged(nameof(HasSecretProtocolQueue));
        }
    }

    public void Dispose()
    {
        _autoCaptureWatcher.TargetFound -= OnAutoCaptureTargetFound;
        _autoCaptureWatcher.AmbiguousCandidatesDetected -= OnAutoCaptureAmbiguousCandidates;
        _autoCaptureWatcher.StateChanged -= OnAutoCaptureStateChanged;
        _autoCaptureWatcher.Dispose();
        _targetHeartbeat.OfflineDetected -= OnTargetProcessOffline;
        _targetHeartbeat.Dispose();
        Diagnostics.SnapshotChanged -= OnDiagnosticsSnapshotChanged;
        GameLaunch.PropertyChanged -= OnGameLaunchPropertyChanged;
        Reinforcement.PropertyChanged -= OnReinforcementPropertyChanged;
        SecretProtocol.PropertyChanged -= OnSecretProtocolPropertyChanged;
        Diagnostics.Dispose();
        DisposeSession();
        _hotkeyCoordinator.Dispose();
        _globalHotkeyService?.Dispose();
        _autoRepair.Dispose();
        Persistence?.Dispose();
    }

    private void DisposeSession()
    {
        _targetHeartbeat.Stop();
        _autoRepair.Stop();
        _gameSession.ResetGameState();
        StopHotkeys();
        FeatureToggle.ResetToggleStates();
        SelectedUnit.ResetToggleStates();
        Ascension.ClearActiveState();
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
            DiagnosticEventCodes.TargetOffline,
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
        var now = DateTimeOffset.UtcNow;
        var existingIndex = NamedPresetList.FindIndex(_featurePresets, name);
        var preset = existingIndex >= 0
            ? _featurePresets[existingIndex] with { Snapshot = snapshot, UpdatedAtUtc = now }
            : new FeaturePreset(name, snapshot, now, now);
        NamedPresetList.Upsert(_featurePresets, preset);
        Persistence?.MarkDirty();
    }

    public SnapshotApplyResult ApplyFeaturePreset(string name)
    {
        var presetIndex = NamedPresetList.FindIndex(_featurePresets, name);
        if (presetIndex < 0)
            return new SnapshotApplyResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var preset = _featurePresets[presetIndex];
        var result = FeatureState.ApplySnapshot(preset.Snapshot, suppressRuntimeApply: false);
        LastAppliedFeaturePresetName = name;
        Persistence?.MarkDirty();
        return result;
    }

    public bool RenameFeaturePreset(string oldName, string newName)
    {
        var idx = NamedPresetList.FindIndex(_featurePresets, oldName);
        if (idx < 0) return false;
        if (NamedPresetList.ContainsName(_featurePresets, newName, idx))
            return false; // 新名冲突
        var preset = _featurePresets[idx];
        _featurePresets[idx] = preset with { Name = newName, UpdatedAtUtc = DateTimeOffset.UtcNow };
        if (LastAppliedFeaturePresetName?.Equals(oldName, StringComparison.OrdinalIgnoreCase) == true)
            LastAppliedFeaturePresetName = newName;
        Persistence?.MarkDirty();
        return true;
    }

    public bool DeleteFeaturePreset(string name)
    {
        var removed = NamedPresetList.RemoveByName(_featurePresets, name);
        if (removed)
        {
            if (LastAppliedFeaturePresetName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                LastAppliedFeaturePresetName = null;
            Persistence?.MarkDirty();
        }
        return removed;
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
            AscensionPresets = Ascension.GetPresetsSnapshot(),
            LastAppliedFeaturePresetName = LastAppliedFeaturePresetName,
            DurableProductPolicy = _durableProductPolicy,
            HideDeveloperSurfaces = !_developerSurfacesVisible,
            EnableWebControl = _enableWebControl
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
        ClampHiddenDeveloperSurfacePageIndex(ref index);          // 私有实现处理 operation-explorer(10)
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
        _hotkeyCoordinator.Start(AllFeatureItems(), CreateActionHotkeyBindings(_hotkeys));
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
    /// 按声明表中的全局动作当前配置重新注册全局热键。
    /// 在 InitializeGlobalHotkeys 和 ReloadHotkeys 中调用；未分配（解析为 null）的动作跳过注册。
    /// </summary>
    private void ApplyGlobalHotkeys()
    {
        if (_globalHotkeyService is null)
        {
            return;
        }
        _globalHotkeyService.UnregisterAll();

        // 全局动作按声明表顺序从基址分配 id；注册失败逐条提示（组合键被占用）。
        var globalIndex = 0;
        foreach (var definition in _actionHotkeyDefinitions)
        {
            if (definition.Scope != ActionHotkeyScope.GlobalRegister ||
                ResolveConfiguredHotkey(_hotkeys, definition.RawName) is not { } gesture)
            {
                continue;
            }

            if (!_globalHotkeyService.Register(GlobalHotkeyIdBase + globalIndex++, gesture, definition.Execute))
            {
                StatusMessage = $"全局热键 {gesture.DisplayText} 注册失败，可能被其他程序占用；可在设置页改用其他组合。";
            }
        }
    }

    /// <summary>
    /// 动作热键声明表：新增主要按键只需在此加一条记录。默认组合选 Ctrl+Alt 是为避开
    /// Ctrl+Shift（系统键盘布局切换）、Win+*（系统级）、Alt+letter（菜单助记符）等高频冲突，
    /// 用户可在设置页改成任何组合。
    /// </summary>
    private IReadOnlyList<ActionHotkeyDefinition> CreateActionHotkeyDefinitions()
    {
        return
        [
            new(TrainerFeatureIds.ExecuteReinforcementQueue, "执行队列（增援）", "Insert",
                () => Reinforcement.ExecuteReinforcementQueueCommand.Execute(null),
                () => Reinforcement.ExecuteReinforcementQueueCommand.CanExecute(null),
                GestureChanged: text => Reinforcement.ExecuteReinforcementQueueButtonText = FormatLabelWithHotkey("执行队列", text)),
            new(TrainerFeatureIds.ReadSelectedUnitCode, "读取选中单位代码", "Home",
                () => Reinforcement.ReadSelectedUnitCodeCommand.Execute(null),
                () => Reinforcement.ReadSelectedUnitCodeCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.ReadSelectedUnitCodeButtonText = FormatLabelWithHotkey("读取选中单位", text)),
            new(TrainerFeatureIds.AscensionApply, "属性修改：应用", "Ctrl+Alt+A",
                () => Ascension.ApplyCommand.Execute(null),
                () => Ascension.ApplyCommand.CanExecute(null),
                GestureChanged: text => Ascension.ApplyButtonText = FormatLabelWithHotkey("应用", text)),
            new(TrainerFeatureIds.AscensionRestore, "属性修改：全部还原", "Ctrl+Alt+R",
                () => Ascension.RestoreCommand.Execute(null),
                () => Ascension.RestoreCommand.CanExecute(null),
                GestureChanged: text => Ascension.RestoreButtonText = FormatLabelWithHotkey("全部还原", text)),
            // 游戏内面板显隐：命中即消费该键，使原生 WM_KEYDOWN F10 处理器收不到，
            // 避免主菜单里双重切换互相抵消。
            new(TrainerFeatureIds.ToggleOverlay, "游戏内面板 显示/隐藏", "F10",
                () => _sessionManager.ToggleOverlayVisibility(),
                () => _sessionManager.CanToggleOverlay),
            // 主控操作（全局）：修改器最小化或游戏未启动时也能触发，走 Win32 RegisterHotKey。
            new(TrainerFeatureIds.DetectProcess, "立刻检测（全局）", "Ctrl+Alt+D",
                () => { if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null); },
                CanExecute: null,
                Scope: ActionHotkeyScope.GlobalRegister),
            new(TrainerFeatureIds.LaunchAndLoad, "装载并启动（全局）", "Ctrl+Alt+L",
                () => { if (LaunchAndLoadCommand.CanExecute(null)) LaunchAndLoadCommand.Execute(null); },
                CanExecute: null,
                Scope: ActionHotkeyScope.GlobalRegister),
        ];
    }

    private IReadOnlyList<FeatureCommandOverride> CreateFeatureCommandOverrides()
    {
        return
        [
            new(TrainerFeatureIds.GetBase,
                () => Reinforcement.GetMeBaseCommand.Execute(null),
                () => Reinforcement.GetMeBaseCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.GetMeBaseButtonText = FormatLabelWithHotkey("给玩家基地车", text)),
            new(TrainerFeatureIds.Reinforcement,
                () => Reinforcement.ExecuteReinforcementCommand.Execute(null),
                () => Reinforcement.ExecuteReinforcementCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.ExecuteReinforcementButtonText = FormatLabelWithHotkey("呼叫战场增援", text)),
            new(TrainerFeatureIds.CopySelectedUnit,
                () => Reinforcement.CopySelectedUnitCommand.Execute(null),
                () => Reinforcement.CopySelectedUnitCommand.CanExecute(null),
                AllowRepeat: true,
                GestureChanged: text => Reinforcement.CopySelectedUnitButtonText = FormatLabelWithHotkey("复制选中单位", text)),
        ];
    }

    private IReadOnlyDictionary<string, string> CreateDefaultHotkeys(IReadOnlyList<TrainerFeature> features)
    {
        // 目录功能默认键来自 feature.Hotkey（经 manifest/catalog），动作热键默认键来自声明表。
        var hotkeys = new Dictionary<string, string>(TrainerFeatureCatalog.CreateDefaultHotkeys(features), StringComparer.Ordinal);
        foreach (var definition in _actionHotkeyDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(definition.DefaultGesture))
            {
                hotkeys[definition.RawName] = definition.DefaultGesture;
            }
        }

        return hotkeys;
    }

    private static HotkeyGesture? ResolveConfiguredHotkey(IReadOnlyDictionary<string, string> hotkeys, string name)
    {
        if (hotkeys.TryGetValue(name, out var hotkey))
            return HotkeyGesture.TryParse(hotkey, out var gesture) ? gesture : null;
        return null;
    }

    /// <summary>
    /// 构建动作热键绑定：声明表动作按 <paramref name="hotkeys"/> 现场解析（无缓存手势字段，
    /// 热重载后重建即生效）；feature 命令覆盖的手势经覆盖应用后的 feature 解析，
    /// 无用户覆盖时回落 feature 默认键。internal 供测试断言绑定新鲜度。
    /// </summary>
    internal IEnumerable<HotkeyActionBinding> CreateActionHotkeyBindings(IReadOnlyDictionary<string, string> hotkeys)
    {
        foreach (var definition in _actionHotkeyDefinitions)
        {
            if (ResolveConfiguredHotkey(hotkeys, definition.RawName) is not { } gesture)
            {
                continue;
            }

            yield return new HotkeyActionBinding(gesture, definition.Execute, definition.CanExecute, definition.AllowRepeat);
        }

        var configuredFeatures = TrainerFeatureCatalog.ApplyHotkeyOverrides(_uiFeatures, hotkeys);
        var featureByRawName = configuredFeatures.ToDictionary(f => f.RawName, StringComparer.Ordinal);
        foreach (var overrideEntry in _featureCommandOverrides)
        {
            if (!featureByRawName.TryGetValue(overrideEntry.RawName, out var feature) ||
                !HotkeyGesture.TryParse(feature.Hotkey, out var featureGesture))
            {
                continue;
            }

            yield return new HotkeyActionBinding(featureGesture, overrideEntry.Execute, overrideEntry.CanExecute, overrideEntry.AllowRepeat);
        }
    }

    private static string FormatActionButtonText(TrainerFeature feature, string label)
    {
        return FormatLabelWithHotkey(label, feature.Hotkey);
    }

    private static string FormatLabelWithHotkey(string label, string? hotkey)
    {
        return string.IsNullOrWhiteSpace(hotkey) ? label : $"{label} ({hotkey})";
    }

    private string FormatActionHotkeyText(string rawName, string label)
    {
        return FormatLabelWithHotkey(label, ResolveConfiguredHotkey(_hotkeys, rawName)?.DisplayText);
    }

    /// <summary>把声明表动作热键的当前组合经 GestureChanged 同步到各按钮文本（构造初始呈现与热重载共用）。</summary>
    private void RefreshActionHotkeyTexts()
    {
        foreach (var definition in _actionHotkeyDefinitions)
        {
            definition.GestureChanged?.Invoke(ResolveConfiguredHotkey(_hotkeys, definition.RawName)?.DisplayText);
        }
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

        // feature 命令覆盖的按钮文本随覆盖应用后的 feature.Hotkey 刷新。
        foreach (var overrideEntry in _featureCommandOverrides)
        {
            if (hotkeyByRawName.TryGetValue(overrideEntry.RawName, out var overrideHotkey))
            {
                overrideEntry.GestureChanged?.Invoke(overrideHotkey);
            }
        }

        // 声明表动作热键重解析，经 GestureChanged 刷新各自呈现；随后统一通知主控按钮文本并重注册全局键。
        RefreshActionHotkeyTexts();

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
