using RayaTrainer.App.Services;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// 工具域：版本更新检查 + 手机遥控（MobileRemote）。
/// ToolsPage 全部内容归本 VM（手机遥控直接绑定，无跨页）。
/// </summary>
public sealed class ToolsViewModel : ViewModelBase
{
    private readonly IUpdateChecker _updateChecker;
    private readonly string _currentVersion;
    private readonly Action<string> _setStatus;
    private bool _isCheckingForUpdates;

    public ToolsViewModel(
        IUpdateChecker? updateChecker,
        IApplicationVersionProvider? versionProvider,
        IMobileRemoteLinkProvider? mobileRemoteLinkProvider,
        IQrCodeImageFactory? qrCodeImageFactory,
        IMobileRemoteAvailability? mobileRemoteAvailability,
        Action<string> setStatus)
    {
        _updateChecker = updateChecker ?? new GitHubReleaseUpdateChecker();
        _currentVersion = versionProvider?.CurrentVersion ?? new ApplicationVersionProvider().CurrentVersion;
        _setStatus = setStatus;
        MobileRemote = new MobileRemoteAccessViewModel(
            mobileRemoteLinkProvider ?? new LanMobileRemoteLinkProvider(),
            qrCodeImageFactory ?? new QrCodeImageFactory(),
            mobileRemoteAvailability ?? new MobileRemoteAvailability(),
            setStatus);
        CheckForUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(), () => !IsCheckingForUpdates);
    }

    public MobileRemoteAccessViewModel MobileRemote { get; }

    public RelayCommand CheckForUpdatesCommand { get; }

    public string CurrentVersionText => $"当前版本：{_currentVersion}";

    public string CheckForUpdatesHelpText => "连接 GitHub 检查最新正式 Release；只提示版本和下载页，不会自动下载或替换程序。";

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            _isCheckingForUpdates = value;
            OnPropertyChanged();
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        _setStatus("正在检查 GitHub 最新正式版本。");
        try
        {
            var result = await _updateChecker.CheckLatestStableReleaseAsync(_currentVersion);
            _setStatus(CreateUpdateStatusMessage(result));
        }
        catch (Exception ex)
        {
            _setStatus($"检查更新失败：{ex.Message}");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>刷新本 VM 命令 CanExecute。MainViewModel.RaiseCommandStates() 遍历调用。</summary>
    public void RaiseCommandStates()
    {
        CheckForUpdatesCommand.RaiseCanExecuteChanged();
        MobileRemote.GenerateQrCodeCommand.RaiseCanExecuteChanged();
    }

    private static string CreateUpdateStatusMessage(UpdateCheckResult result)
    {
        if (!result.IsSuccessful)
        {
            return result.Message;
        }

        if (!result.IsUpdateAvailable)
        {
            return $"当前已是最新版本 {result.CurrentVersion}。";
        }

        var assetText = result.Assets.Count == 0
            ? "发布页暂无可下载资产"
            : $"发布页包含 {result.Assets.Count} 个下载资产";
        return $"发现新版 {result.LatestVersion}（当前 {result.CurrentVersion}），{assetText}，请到 GitHub Release 下载：{result.ReleaseUrl}";
    }
}
