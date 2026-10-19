using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 启动器配置域：RA3.exe 路径、启动参数、窗口/音频开关、Mods 根目录与 MOD 列表、参数生成。
/// 不含进程附加逻辑（LaunchAndLoad/SaveLauncherSettings 留 MainViewModel 协调者）。
/// </summary>
public sealed class GameLaunchViewModel : ViewModelBase
{
    private static readonly Ra3ModEntry NoModLaunchEntry = new("无 MOD", string.Empty, string.Empty, null);

    private readonly Func<bool> _isBusy;
    private readonly Action<string> _setStatus;
    private readonly Action _saveSettings;

    private string _launcherPath;
    private string _launcherArguments = string.Empty;
    private bool _launchUseRa3LauncherUi;
    private bool _launchWindowed;
    private bool _launchFullscreen;
    private string _launchResolutionXText = string.Empty;
    private string _launchResolutionYText = string.Empty;
    private string _launchWindowPositionXText = string.Empty;
    private string _launchWindowPositionYText = string.Empty;
    private bool _launchNoAudio;
    private bool _launchNoAudioMusic;
    private string _modsRootPath;
    private Ra3ModEntry? _selectedModLaunchEntry;

    public GameLaunchViewModel(
        TrainerAppSettings settings,
        Func<bool> isBusy,
        Action<string> setStatus,
        Action saveSettings)
    {
        _isBusy = isBusy;
        _setStatus = setStatus;
        _saveSettings = saveSettings;

        _launcherPath = settings.LauncherPath;
        ApplyLauncherArguments(settings.LauncherArguments, notify: false);
        _modsRootPath = settings.ModsRootPath;
        ModLaunchEntries = new ObservableCollection<Ra3ModEntry>();

        BrowseLauncherCommand = new RelayCommand(BrowseLauncherPath, () => !_isBusy());
        BrowseModsRootCommand = new RelayCommand(BrowseModsRootPath, () => !_isBusy());
        GenerateLauncherArgumentsCommand = new RelayCommand(GenerateLauncherArguments, () => !_isBusy());
        RefreshModsCommand = new RelayCommand(() => RefreshModLaunchEntries(null, updateStatus: true), () => !_isBusy());

        RefreshModLaunchEntries(settings.SelectedModSkudefPath, updateStatus: false);
    }

    public ObservableCollection<Ra3ModEntry> ModLaunchEntries { get; }

    public RelayCommand BrowseLauncherCommand { get; }
    public RelayCommand BrowseModsRootCommand { get; }
    public RelayCommand GenerateLauncherArgumentsCommand { get; }
    public RelayCommand RefreshModsCommand { get; }

    public string LauncherPath { get => _launcherPath; set { _launcherPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(GamePathSummaryText)); } }

    public string GamePathSummaryText => string.IsNullOrWhiteSpace(LauncherPath)
        ? "点击配置游戏路径"
        : (LauncherPath.Length > 50 ? "..." + LauncherPath[^47..] : LauncherPath);

    public string LauncherArguments
    {
        get => _launcherArguments;
        set
        {
            var normalized = value ?? string.Empty;
            if (_launcherArguments == normalized)
            {
                return;
            }

            _launcherArguments = normalized;
            OnPropertyChanged();
        }
    }

    public bool LaunchUseRa3LauncherUi
    {
        get => _launchUseRa3LauncherUi;
        set
        {
            if (_launchUseRa3LauncherUi == value)
            {
                return;
            }

            _launchUseRa3LauncherUi = value;
            OnPropertyChanged();
        }
    }

    public bool LaunchWindowed
    {
        get => _launchWindowed;
        set
        {
            if (_launchWindowed == value)
            {
                return;
            }

            _launchWindowed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LaunchBorderlessFullscreen));
        }
    }

    public bool LaunchFullscreen
    {
        get => _launchFullscreen;
        set
        {
            if (_launchFullscreen == value)
            {
                return;
            }

            _launchFullscreen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LaunchBorderlessFullscreen));
        }
    }

    public bool LaunchBorderlessFullscreen
    {
        get => LaunchWindowed && LaunchFullscreen;
        set
        {
            if (LaunchBorderlessFullscreen == value)
            {
                return;
            }

            _launchWindowed = value;
            _launchFullscreen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LaunchWindowed));
            OnPropertyChanged(nameof(LaunchFullscreen));
        }
    }

    public string LaunchResolutionXText { get => _launchResolutionXText; set { _launchResolutionXText = value; OnPropertyChanged(); } }
    public string LaunchResolutionYText { get => _launchResolutionYText; set { _launchResolutionYText = value; OnPropertyChanged(); } }
    public string LaunchWindowPositionXText { get => _launchWindowPositionXText; set { _launchWindowPositionXText = value; OnPropertyChanged(); } }
    public string LaunchWindowPositionYText { get => _launchWindowPositionYText; set { _launchWindowPositionYText = value; OnPropertyChanged(); } }
    public bool LaunchNoAudio { get => _launchNoAudio; set { _launchNoAudio = value; OnPropertyChanged(); } }
    public bool LaunchNoAudioMusic { get => _launchNoAudioMusic; set { _launchNoAudioMusic = value; OnPropertyChanged(); } }
    public string ModsRootPath { get => _modsRootPath; set { _modsRootPath = value; OnPropertyChanged(); } }

    public Ra3ModEntry? SelectedModLaunchEntry { get => _selectedModLaunchEntry; set { _selectedModLaunchEntry = value; OnPropertyChanged(); RaiseCommandStates(); } }

    public string BrowseLauncherHelpText => "选择游戏程序路径（RA3.exe 或起义时刻 RA3EP1.exe），供一键启动使用。";
    public string GenerateLauncherArgumentsHelpText => "按上方配置和选中 MOD 生成最终参数；选中 MOD 时会写入 -modConfig，无 MOD 不写入，生成后仍可手动追加或修改少见参数，例如 -silentLogin、-file。";
    public string BrowseModsRootHelpText => "选择自定义 Mods 根目录；通常是 文档\\Red Alert 3\\Mods，也可以选择其他 MOD 根目录。";
    public string RefreshModsHelpText => "扫描 MOD 根目录下的 .skudef 文件，刷新可直接启动的 MOD 列表。";
    public string LaunchUseRa3LauncherUiHelpText => "生成参数时加入 -ui；最终参数带 -ui 会走选中的游戏程序（RA3.exe / RA3EP1.exe），不带则直接启动 .game。";
    public string LaunchWindowedHelpText => "启动参数加入 -win，以窗口模式运行游戏。";
    public string LaunchFullscreenHelpText => "启动参数加入 -fullscreen；和窗口模式一起启用时就是全屏窗口化。";
    public string LaunchBorderlessFullscreenHelpText => "同时加入 -win 和 -fullscreen，即全屏窗口化/无边框全屏。";
    public string LaunchResolutionHelpText => "窗口模式下填写后启动参数会加入 -xres 和 -yres；留空则不指定分辨率。";
    public string LaunchWindowPositionHelpText => "填写后启动参数会加入 -xpos 和 -ypos，用于窗口模式下指定窗口位置。";
    public string LaunchNoAudioHelpText => "生成参数时加入 -noaudio，关闭游戏音频。";
    public string LaunchNoAudioMusicHelpText => "生成参数时加入 -noAudioMusic，只关闭游戏音乐。";
    public string LauncherGuideText => "原版游戏可以选择无 MOD 后使用装载并启动，并不带 -ui 直接启动原版 .game，也可以在最终参数加入 -ui 走选中的游戏程序（RA3.exe / RA3EP1.exe）；上方启动器界面等配置只负责生成最终参数，不包含 -ui 时会跳过游戏程序，直接启动原版或从自定义 Mods 根目录读取 .skudef 的 MOD .game；日冕等自带启动器的 MOD 仍可进入游戏后点击立刻检测进程自动装载。";

    /// <summary>
    /// 刷新本 VM 的命令 CanExecute 状态。MainViewModel.RaiseCommandStates() 会在共享状态变化时遍历调用。
    /// </summary>
    public void RaiseCommandStates()
    {
        BrowseLauncherCommand.RaiseCanExecuteChanged();
        BrowseModsRootCommand.RaiseCanExecuteChanged();
        GenerateLauncherArgumentsCommand.RaiseCanExecuteChanged();
        RefreshModsCommand.RaiseCanExecuteChanged();
    }

    /// <summary>供 MainViewModel.LaunchAndLoadAsync 生成最终启动参数。</summary>
    public string GenerateArguments() => CurrentLaunchArgumentOptions().ToCommandLine();

    /// <summary>供 MainViewModel.LaunchAndLoadAsync 定位游戏根目录。</summary>
    public string ResolveGameRootPath()
    {
        if (string.IsNullOrWhiteSpace(LauncherPath))
        {
            throw new InvalidOperationException("请先选择游戏程序路径（RA3.exe 或 RA3EP1.exe），用于定位游戏根目录。");
        }

        return Path.GetDirectoryName(LauncherPath)
            ?? throw new InvalidOperationException("无法从游戏程序路径定位游戏根目录。");
    }

    /// <summary>供 MainViewModel.CurrentSettings() 收集启动器片段。</summary>
    public (string LauncherPath, string LauncherArguments, string ModsRootPath, string? SelectedModSkudefPath) GetSettingsSnapshot()
        => ((LauncherPath ?? string.Empty).Trim(),
            (LauncherArguments ?? string.Empty).Trim(),
            (ModsRootPath ?? string.Empty).Trim(),
            SelectedModLaunchEntry?.SkudefPath);

    private void GenerateLauncherArguments()
    {
        LauncherArguments = CurrentLaunchArgumentOptions().ToCommandLine();
        _setStatus("已从上方配置生成最终参数。");
    }

    private void BrowseLauncherPath()
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "选择游戏程序（RA3.exe 或 RA3EP1.exe）", Filter = "红警3游戏程序|RA3.exe;RA3EP1.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog() == true)
            {
                LauncherPath = dialog.FileName;
                _saveSettings();
            }
        }
        catch (Exception ex) { _setStatus($"选择启动器路径失败：{ex.Message}"); }
    }

    private void BrowseModsRootPath()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择 Mods 根目录",
                InitialDirectory = Directory.Exists(ModsRootPath) ? ModsRootPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog() == true)
            {
                ModsRootPath = dialog.FolderName;
                RefreshModLaunchEntries(null, updateStatus: true);
                _saveSettings();
            }
        }
        catch (Exception ex) { _setStatus($"选择 Mods 根目录失败：{ex.Message}"); }
    }

    private void RefreshModLaunchEntries(string? preferredSkudefPath, bool updateStatus)
    {
        var selectedPath = preferredSkudefPath ?? SelectedModLaunchEntry?.SkudefPath;
        ModLaunchEntries.Clear();
        ModLaunchEntries.Add(NoModLaunchEntry);
        SelectedModLaunchEntry = NoModLaunchEntry;

        if (string.IsNullOrWhiteSpace(ModsRootPath))
        {
            if (updateStatus) _setStatus("请先选择自定义 Mods 根目录。");
            return;
        }

        try
        {
            var scannedMods = Ra3ModCatalog.Load(ModsRootPath);
            foreach (var mod in scannedMods)
            {
                ModLaunchEntries.Add(mod);
            }

            SelectedModLaunchEntry =
                ModLaunchEntries.FirstOrDefault(mod => string.Equals(mod.SkudefPath, selectedPath, StringComparison.OrdinalIgnoreCase)) ??
                ModLaunchEntries.FirstOrDefault();

            if (updateStatus)
            {
                _setStatus(scannedMods.Count == 0
                    ? "未在 Mods 根目录下找到 .skudef。"
                    : $"已扫描 MOD：{scannedMods.Count} 个。");
            }
        }
        catch (Exception ex)
        {
            if (updateStatus)
            {
                _setStatus($"扫描 MOD 失败：{ex.Message}");
            }
        }
    }

    private Ra3LaunchArgumentOptions CurrentLaunchArgumentOptions()
    {
        return new Ra3LaunchArgumentOptions(
            LaunchUseRa3LauncherUi,
            LaunchWindowed,
            LaunchFullscreen,
            LaunchResolutionXText,
            LaunchResolutionYText,
            LaunchWindowPositionXText,
            LaunchWindowPositionYText,
            LaunchNoAudio,
            LaunchNoAudioMusic,
            SelectedModLaunchEntry?.SkudefPath ?? string.Empty,
            string.Empty,
            string.Empty);
    }

    private void ApplyLauncherArguments(string? launcherArguments, bool notify)
    {
        _launcherArguments = (launcherArguments ?? string.Empty).Trim();
        var options = Ra3LaunchArgumentOptions.Parse(launcherArguments);
        _launchUseRa3LauncherUi = options.UseLauncherUi;
        _launchWindowed = options.Windowed;
        _launchFullscreen = options.Fullscreen;
        _launchResolutionXText = options.ResolutionX;
        _launchResolutionYText = options.ResolutionY;
        _launchWindowPositionXText = options.WindowPositionX;
        _launchWindowPositionYText = options.WindowPositionY;
        _launchNoAudio = options.NoAudio;
        _launchNoAudioMusic = options.NoAudioMusic;

        if (!notify)
        {
            return;
        }

        OnPropertyChanged(nameof(LaunchUseRa3LauncherUi));
        OnPropertyChanged(nameof(LaunchWindowed));
        OnPropertyChanged(nameof(LaunchFullscreen));
        OnPropertyChanged(nameof(LaunchBorderlessFullscreen));
        OnPropertyChanged(nameof(LaunchResolutionXText));
        OnPropertyChanged(nameof(LaunchResolutionYText));
        OnPropertyChanged(nameof(LaunchWindowPositionXText));
        OnPropertyChanged(nameof(LaunchWindowPositionYText));
        OnPropertyChanged(nameof(LaunchNoAudio));
        OnPropertyChanged(nameof(LaunchNoAudioMusic));
        OnPropertyChanged(nameof(LauncherArguments));
        RaiseCommandStates();
    }
}
