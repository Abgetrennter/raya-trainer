using System.Globalization;
using System.IO;
using RayaTrainer.App.Services;
using RayaTrainer.Host.Services;
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
                SelectedPageIndex = PageIds.ToIndex(PageIds.Diagnostics);
                return;
            }

            await RefreshProcessAsync();
            if (_sessionManager.TargetProcessId is not null || HasSelectableCandidates)
            {
                return;
            }

            if (Diagnostics.Health == RayaTrainer.Core.Diagnostics.TrainerDiagnosticHealth.Error)
            {
                SelectedPageIndex = PageIds.ToIndex(PageIds.Diagnostics);
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
            await InstallPatchesAsync();
            return;
        }

        if (Diagnostics.Health is RayaTrainer.Core.Diagnostics.TrainerDiagnosticHealth.Error or
            RayaTrainer.Core.Diagnostics.TrainerDiagnosticHealth.Attention)
        {
            SelectedPageIndex = PageIds.ToIndex(PageIds.Diagnostics);
            return;
        }

        SelectedPageIndex = 0;
        StatusMessage = "准备完成。请选择需要的功能；不确定时可以把鼠标停在功能名称上查看说明。";
    }

    // Overridable in tests; defaults to a real OS process-liveness check so a manual detect can
    // tell a still-running target apart from one that has already exited.
    internal Func<int, bool> TargetLivenessProbe { get; set; } = TargetProcessHeartbeatMonitor.IsProcessRunning;

    // Manual detect entry — bound to the "立刻检测" button and the Ctrl+Alt+D hotkey. When we are
    // already attached to a live target this only refreshes status and returns; it must NOT tear
    // down the Agent-owned session, so a healthy connection is never needlessly dropped and
    // re-established. It (re)detects only when disconnected or the current target process has
    // exited. The diagnostics "重试" path uses the asynchronous forced-reconnect path.
    internal async Task DetectProcessOnDemandAsync()
    {
        if (TryKeepCurrentTarget())
        {
            await Diagnostics.RefreshNowAsync();
        }
        else
        {
            await RefreshProcessAsync();
        }
    }

    private bool TryKeepCurrentTarget()
    {
        var currentPid = _sessionManager.TargetProcessId;
        if (currentPid is null ||
            !_sessionManager.CanUseFeatures ||
            !TargetLivenessProbe(currentPid.Value))
        {
            return false;
        }

        // The session stays attached, but a one-shot product-control negotiation that never
        // settled (transient pipe failure right after attach) gets re-kicked here so a plain
        // manual detect recovers the plane without a full re-attach.
        if (_sessionManager is TrainerSessionManager sessionManager)
        {
            sessionManager.EnsureProductControlNegotiationStarted();
        }

        // Keep the header game-state badge (菜单/遭遇战/战役) in sync on a manual detect too;
        // otherwise it only refreshes on session activation.
        _gameSession.RefreshGameState();

        NotifySessionStateChanged();
        RaiseAvailabilityChangedForAllFeatures();
        RaiseCommandStates();
        StatusMessage = ArePatchesInstalled
            ? "已连接到运行中的红警 3，无需重新检测。"
            : "已连接到运行中的红警 3，请点击上方主按钮安装 Patch。";
        return true;
    }

    public async Task RefreshProcessAsync()
    {
        await DisposeSessionAsync();
        var selection = SelectDefaultTargetForAttach(updateStatus: true);
        if (selection.Target is null)
        {
            RaiseCommandStates();
            return;
        }

        await AttachTargetAsync(selection.Target, autoInstall: true, selection.Notice);
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

        await AttachTargetAsync(selection.Target, autoInstall: true, selection.Notice);
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
                .Where(candidate => candidate.CanAttemptInstallation)
                .ToArray()
            : Array.Empty<DetectedRa3Target>();

        var notice = selection.Status switch
        {
            TargetSelectionStatus.SingleSupportedAmongMany =>
                $"检测到多个 RA3 进程，已选择唯一可尝试连接目标：{FormatTarget(selection.SelectedTarget)}。",
            TargetSelectionStatus.AmbiguousRequiresUserChoice => HasSelectableCandidates
                ? "检测到多个可尝试连接的 RA3 目标，请在下方列表中选择一个再连接。"
                : $"检测到多个可安装 RA3 目标，请手动选择后再连接：{FormatCandidateSummary(selection.Candidates)}。",
            TargetSelectionStatus.NoInstallableCandidate =>
                $"检测到 RA3 进程，但没有可安装或可签名验证的版本：{FormatCandidateSummary(selection.Candidates)}。",
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

    private async Task AttachTargetAsync(TrainerTarget target, bool autoInstall, string? notice = null)
    {
        var ownsBusyState = !IsBusy;
        if (ownsBusyState)
        {
            IsBusy = true;
        }

        StatusMessage = "正在连接游戏并初始化修改器…";
        try
        {
            var result = await _sessionWorkflow.AttachAsync(_manifest, target);
            await ApplyAttachResultAsync(target, autoInstall, notice, result);
        }
        catch (Exception ex)
        {
            _autoCaptureWatcher.NotifyAttachFailed();
            StatusMessage = ex.Message;
        }
        finally
        {
            if (ownsBusyState)
            {
                IsBusy = false;
            }
            RaiseCommandStates();
        }
    }

    private async Task ApplyAttachResultAsync(
        TrainerTarget target,
        bool autoInstall,
        string? notice,
        AttachResult result)
    {
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
                    await ActivateInstalledSessionAsync();
                }
                else
                {
                    await InstallPatchesAsync();
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(notice))
        {
            StatusMessage = $"{notice} {StatusMessage}";
        }
    }

    private void SelectCandidate(DetectedRa3Target? candidate)
    {
        if (candidate is null || !candidate.CanAttemptInstallation)
        {
            return;
        }

        SelectableCandidates = Array.Empty<DetectedRa3Target>();
        _ = AttachTargetAsync(candidate.ToTrainerTarget(), autoInstall: true);
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
