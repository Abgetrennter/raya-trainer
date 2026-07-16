using System.Globalization;
using System.IO;
using RayaTrainer.App.Services;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ExecutePrimaryActionAsync()
    {
        if (IsBusy || HasSelectableCandidates)
        {
            return;
        }

        if (_sessionManager.TargetProcessId is null)
        {
            if (Diagnostics.Health == RayaTrainer.Core.Diagnostics.TrainerDiagnosticHealth.Error)
            {
                SelectedPageIndex = 6;
                return;
            }

            RefreshProcess();
            if (_sessionManager.TargetProcessId is not null || HasSelectableCandidates)
            {
                return;
            }

            if (Diagnostics.Health == RayaTrainer.Core.Diagnostics.TrainerDiagnosticHealth.Error)
            {
                SelectedPageIndex = 6;
                return;
            }

            if (HasConfiguredGamePath)
            {
                await LaunchAndLoadAsync();
                return;
            }

            IsGameSetupExpanded = true;
            StatusMessage = "没有找到正在运行的红警 3。请在“游戏位置”中选择游戏程序（RA3.exe 或起义时刻 RA3EP1.exe），然后再次点击上方主按钮。";
            return;
        }

        if (!ArePatchesInstalled)
        {
            InstallPatches();
            return;
        }

        if (Diagnostics.Health is RayaTrainer.Core.Diagnostics.TrainerDiagnosticHealth.Error or
            RayaTrainer.Core.Diagnostics.TrainerDiagnosticHealth.Attention)
        {
            SelectedPageIndex = 6;
            return;
        }

        SelectedPageIndex = 0;
        StatusMessage = "准备完成。请选择需要的功能；不确定时可以把鼠标停在功能名称上查看说明。";
    }

    public void RefreshProcess()
    {
        DisposeSession();
        var selection = SelectDefaultTargetForAttach(updateStatus: true);
        if (selection.Target is null)
        {
            RaiseCommandStates();
            return;
        }

        AttachTarget(selection.Target, autoInstall: true, selection.Notice);
    }

    public async Task LaunchAndLoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            SaveLauncherSettings();
            var options = Ra3LaunchArgumentOptions.Parse(GameLaunch.LauncherArguments);
            if (options.UseLauncherUi)
            {
                StatusMessage = "正在通过游戏程序启动。";
                _launcher.Start(GameLaunch.LauncherPath, GameLaunch.LauncherArguments);
                await WaitForLaunchedGameAndAttachAsync("已启动游戏程序，等待可安装的 RA3 游戏进程。");
                return;
            }

            var modSkudefPath = string.IsNullOrWhiteSpace(options.ModConfigPath)
                ? GameLaunch.SelectedModLaunchEntry?.SkudefPath
                : options.ModConfigPath;
            var isModLaunch = !string.IsNullOrWhiteSpace(modSkudefPath);
            var plan = Ra3DirectLaunchPlanner.Create(
                GameLaunch.ResolveGameRootPath(),
                modSkudefPath ?? string.Empty,
                options.ToDirectGameArguments());
            StatusMessage = isModLaunch
                ? $"正在直接启动 MOD：{Path.GetFileNameWithoutExtension(modSkudefPath)}。"
                : "正在直接启动原版游戏。";
            _launcher.StartCommandLine(plan.CommandLine, plan.WorkingDirectory);
            await WaitForLaunchedGameAndAttachAsync(isModLaunch
                ? "已直接启动 MOD，等待可安装的 RA3 游戏进程。"
                : "已直接启动原版游戏，等待可安装的 RA3 游戏进程。");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task WaitForLaunchedGameAndAttachAsync(string waitingMessage)
    {
        StatusMessage = waitingMessage;
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(CurrentSettings().AttachTimeoutSeconds + 5));
        var selection = await WaitForDefaultTargetAsync(
            TimeSpan.FromSeconds(CurrentSettings().AttachTimeoutSeconds),
            cancellation.Token);
        if (selection.Target is null)
        {
            StatusMessage = selection.Status == TargetSelectionStatus.NoCandidate
                ? "启动后未找到 RA3。"
                : selection.Notice ?? StatusMessage;
            return;
        }

        AttachTarget(selection.Target, autoInstall: true, selection.Notice);
    }

    private async Task<(TrainerTarget? Target, string? Notice, TargetSelectionStatus Status)> WaitForDefaultTargetAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var selection = await Ra3TargetSelectionWaiter.WaitForDefaultAsync(
            _locator.SelectDefault,
            timeout,
            cancellationToken: cancellationToken);
        return ToAttachSelection(selection, updateStatus: false);
    }

    private (TrainerTarget? Target, string? Notice, TargetSelectionStatus Status) SelectDefaultTargetForAttach(
        bool updateStatus)
    {
        return ToAttachSelection(_locator.SelectDefault(), updateStatus);
    }

    private (TrainerTarget? Target, string? Notice, TargetSelectionStatus Status) ToAttachSelection(
        TargetSelectionResult selection,
        bool updateStatus)
    {
        SelectableCandidates = selection.Status == TargetSelectionStatus.AmbiguousRequiresUserChoice
            ? selection.Candidates
                .Where(candidate => candidate.SupportStatus == TargetSupportStatus.Installable)
                .ToArray()
            : Array.Empty<DetectedRa3Target>();

        var notice = selection.Status switch
        {
            TargetSelectionStatus.SingleSupportedAmongMany =>
                $"检测到多个 RA3 进程，已选择唯一可安装目标：{FormatTarget(selection.SelectedTarget)}。",
            TargetSelectionStatus.AmbiguousRequiresUserChoice => HasSelectableCandidates
                ? "检测到多个可安装 RA3 目标，请在下方列表中选择一个再连接。"
                : $"检测到多个可安装 RA3 目标，请手动选择后再连接：{FormatCandidateSummary(selection.Candidates)}。",
            TargetSelectionStatus.NoInstallableCandidate =>
                $"检测到 RA3 进程，但没有可安装的已验证版本：{FormatCandidateSummary(selection.Candidates)}。",
            TargetSelectionStatus.NoCandidate => "未找到 RA3 进程。",
            _ => null
        };
        if (updateStatus && notice is not null)
        {
            StatusMessage = notice;
        }

        return (selection.SelectedTarget?.ToTrainerTarget(), notice, selection.Status);
    }

    private static string FormatCandidateSummary(IReadOnlyList<DetectedRa3Target> candidates)
    {
        return string.Join("；", candidates.Select(FormatTarget));
    }

    private static string FormatTarget(DetectedRa3Target? target)
    {
        if (target is null)
        {
            return "未知目标";
        }

        var version = target.Profile?.DisplayName ?? target.FileVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = "未知版本";
        }

        return $"{version} PID={target.ProcessId} {target.ModulePath}";
    }

    private void AttachTarget(TrainerTarget target, bool autoInstall, string? notice = null)
    {
        try
        {
            var result = _sessionWorkflow.Attach(_manifest, target);
            StatusMessage = result.Message;
            if (result.Success)
            {
                _targetHeartbeatGeneration = _targetHeartbeat.Start(target.ProcessId!.Value);
                CurrentTargetInfo = FormatCurrentTargetInfo(target);
                SelectableCandidates = Array.Empty<DetectedRa3Target>();
                NotifySessionStateChanged();
                _autoCaptureWatcher.NotifyAttached();
                if (autoInstall)
                {
                    if (ArePatchesInstalled)
                    {
                        RaiseAvailabilityChangedForAllFeatures();
                        ActivateInstalledSession();
                    }
                    else
                    {
                        InstallPatches();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(notice))
            {
                StatusMessage = $"{notice} {StatusMessage}";
            }
        }
        catch (Exception ex)
        {
            _autoCaptureWatcher.NotifyAttachFailed();
            StatusMessage = ex.Message;
        }
        finally
        {
            RaiseCommandStates();
        }
    }

    private void SelectCandidate(DetectedRa3Target? candidate)
    {
        if (candidate is null || candidate.SupportStatus != TargetSupportStatus.Installable)
        {
            return;
        }

        SelectableCandidates = Array.Empty<DetectedRa3Target>();
        AttachTarget(candidate.ToTrainerTarget(), autoInstall: true);
    }

    private static string FormatCurrentTargetInfo(TrainerTarget target)
    {
        var profileId = string.IsNullOrWhiteSpace(target.VersionProfileId)
            ? "ra3_1.12"
            : target.VersionProfileId;
        var version = string.IsNullOrWhiteSpace(target.FileVersion)
            ? profileId
            : target.FileVersion;
        var pid = target.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "?";
        var path = string.IsNullOrWhiteSpace(target.ModulePath) ? string.Empty : $"  {target.ModulePath}";
        return $"{version}  PID={pid}  {profileId}  [DLL Agent]{path}";
    }
}
