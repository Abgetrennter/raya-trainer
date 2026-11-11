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
                ? "启动后没有找到红警 3。"
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
                $"检测到多个红警 3 进程，已自动选择唯一能连接的那个：{FormatTarget(selection.SelectedTarget)}。",
            TargetSelectionStatus.AmbiguousRequiresUserChoice => HasSelectableCandidates
                ? "检测到多个红警 3 进程，请在下方列表里选择一个再连接。"
                : $"检测到多个能连接的红警 3 目标，请手动选择后再连接：{FormatCandidateSummary(selection.Candidates)}。",
            TargetSelectionStatus.NoInstallableCandidate => DescribeUnusableTargets(selection.Candidates),
            TargetSelectionStatus.NoCandidate => "没有找到正在运行的红警 3。",
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

    // Core 已判定每个候选为什么不能用，这里只负责把原因讲成用户能看懂的话：
    // 先说这一步该做什么，再说这次为什么连不上。
    private static string DescribeUnusableTargets(IReadOnlyList<DetectedRa3Target> candidates)
    {
        if (candidates.Count == 1)
        {
            var (advice, reason) = DescribeRejection(candidates[0]);
            return $"找到了红警 3，但没法连接。{advice}原因：{reason}。";
        }

        var reasons = string.Join("；", candidates.Select(candidate => DescribeRejection(candidate).Reason));
        return $"找到了 {candidates.Count} 个红警 3 进程，但都没法连接：{reasons}。请分别处理后点「立刻检测」。";
    }

    private static (string Advice, string Reason) DescribeRejection(DetectedRa3Target candidate)
    {
        var name = candidate.ModuleName;
        var version = candidate.FileVersion;
        return candidate.RejectionReason switch
        {
            TargetRejectionReason.VersionUnsupported => (
                "请把游戏更新到受支持的版本（1.12、1.13 或起义时刻 1.0／1.1），再点「立刻检测」。",
                $"{name} 的版本是 {version}，不在支持范围内"),
            TargetRejectionReason.NameNotOriginal => (
                "请换回游戏目录里没改过名的原版主程序，再点「立刻检测」。",
                $"现在运行的是 {name}，不是原版主程序名，改了名或打过补丁的副本无法确认版本"),
            TargetRejectionReason.MixedBuild => (
                "请修复或重装游戏，再点「立刻检测」。",
                $"{name} 的版本号是 {version}，和文件名对不上，可能混装了不同版本的文件"),
            TargetRejectionReason.VersionUnreadable => (
                "请换回游戏目录里的原版主程序，再点「立刻检测」。",
                $"读不到 {name} 的版本号，文件可能被修改过"),
            TargetRejectionReason.Not32Bit => (
                "请确认启动的是游戏目录里的原版主程序。",
                $"{name} 不是 32 位程序"),
            _ => (
                "请换回游戏目录里的原版主程序，再点「立刻检测」。",
                $"{name} 的版本无法确认")
        };
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
