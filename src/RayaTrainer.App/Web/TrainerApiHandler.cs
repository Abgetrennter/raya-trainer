using RayaTrainer.App.Services;
using RayaTrainer.App.Web.State;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using static RayaTrainer.App.Web.TrainerPresetMapper;

namespace RayaTrainer.App.Web;

public sealed class TrainerApiHandler
{
    private const string WeNeedBackRawName = TrainerFeatureIds.Reinforcement;
    private const string SecretProtocolGrantRawName = TrainerFeatureIds.GrantSecretProtocol;
    private const string TemplateModelReplacementRawName = TrainerFeatureIds.ReplaceTemplateModel;
    private const string TemplateWeaponReplacementRawName = TrainerFeatureIds.ReplaceTemplateWeapon;
    private const string SetTargetHealthRawName = TrainerFeatureIds.SetSelectedUnitTargetHealth;

    private readonly ITrainerSessionService _session;
    private readonly IGameApiCommandQueue _commandQueue;
    private readonly IReadOnlyList<TrainerFeature> _features;
    private readonly TrainerAppSettingsStore _settingsStore;
    private readonly ITrainerPresetSource? _presetSource;
    private readonly ITrainerSavedPresetSource? _savedPresetSource;
    private readonly IGameStateBroadcaster? _broadcaster;

    public TrainerApiHandler(
        ITrainerSessionService session,
        IGameApiCommandQueue commandQueue,
        IReadOnlyList<TrainerFeature> features,
        TrainerAppSettingsStore? settingsStore = null,
        ITrainerPresetSource? presetSource = null,
        ITrainerSavedPresetSource? savedPresetSource = null,
        IGameStateBroadcaster? broadcaster = null)
    {
        _session = session;
        _commandQueue = commandQueue;
        _features = features;
        _settingsStore = settingsStore ?? new TrainerAppSettingsStore();
        _presetSource = presetSource;
        _savedPresetSource = savedPresetSource;
        _broadcaster = broadcaster;
    }

    public TrainerWebStatusResponse GetStatus()
    {
        return new TrainerWebStatusResponse(
            _session.ArePatchesInstalled,
            _session.CanUseFeatures,
            _session.FeatureController is not null,
            _session.FeatureController is IAgentFeatureController { SupportsDirectGameApi: true },
            _session.TargetProcessId,
            _session.InstalledHookCount,
            _session.RemoteSymbolSummary);
    }

    public TrainerDiagnosticSnapshot GetDiagnostics()
    {
        return _session is ITrainerDiagnosticsSource diagnostics
            ? diagnostics.GetDiagnosticSnapshot(_features, maxEvents: 50)
            : TrainerDiagnosticSnapshot.Offline;
    }

    public TrainerFeaturesResponse GetFeatures()
    {
        var controller = _session.FeatureController;
        var features = _features
            .Select(f =>
        {
            var isToggle = FeatureDispatchDefaults.IsToggle(f);
            var capability = _session.GetFeatureCapability(f);
            bool? isEnabled = null;
            if (isToggle && controller is not null)
            {
                try
                {
                    isEnabled = controller.ReadToggleState(f);
                }
                catch
                {
                    // 读取失败时保持 null
                }
            }
            return new TrainerFeatureInfo(
                f.RawName,
                f.DisplayName,
                isToggle ? TrainerFeatureType.Toggle : TrainerFeatureType.Action,
                isEnabled,
                f.Hotkey,
                f.ValueHint,
                RequiresActionParameters(f),
                capability.State.ToString(),
                capability.ReasonCode,
                capability.Reason);
        }).ToArray();

        return new TrainerFeaturesResponse(features);
    }

    public TrainerPresetsResponse GetPresets()
    {
        var settings = _settingsStore.Load();
        var reinforcementPresets = _presetSource?.GetReinforcementPresets();
        var secretProtocolPresets = _presetSource?.GetSecretProtocolPresets();
        var savedSettings = _savedPresetSource?.LoadSavedSettings() ?? Array.Empty<TrainerAppSettings>();

        return new TrainerPresetsResponse(
            MergePresets(
                    reinforcementPresets ?? Array.Empty<ReinforcementPreset>(),
                    settings.ReinforcementPresets
                        .Concat(savedSettings.SelectMany(saved => saved.ReinforcementPresets)),
                    preset => preset.Name)
                .Select(ToPresetInfo)
                .ToArray(),
            MergePresets(
                    secretProtocolPresets ?? Array.Empty<SecretProtocolQueuePreset>(),
                    settings.SecretProtocolPresets
                        .Concat(savedSettings.SelectMany(saved => saved.SecretProtocolPresets)),
                    preset => preset.Name)
                .Select(ToPresetInfo)
                .ToArray());
    }

    public Task<TrainerWebCommandResult> SetToggleAsync(
        TrainerToggleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var feature = FindFeature(request.FeatureId);
        if (feature is null)
        {
            return Task.FromResult(Publish(Failed($"未知功能：{request.FeatureId}。")));
        }

        if (!FeatureDispatchDefaults.IsToggle(feature))
        {
            return Task.FromResult(Publish(Failed($"功能不是开关项：{request.FeatureId}。")));
        }

        var capabilityFailure = RequireFeatureCapability(feature);
        if (capabilityFailure is not null)
        {
            return Task.FromResult(Publish(capabilityFailure));
        }

        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        return _commandQueue.RunAsync(_ =>
        {
            controller!.SetToggle(feature, request.Enabled);
            return Task.FromResult(Publish(Succeeded(request.Enabled ? "功能已开启。" : "功能已关闭。")));
        }, cancellationToken);
    }

    public Task<TrainerWebCommandResult> WriteResourcesAsync(
        TrainerResourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        ResourceValueSettings settings;
        try
        {
            settings = new ResourceValueSettings(
                request.MoneyAmount,
                request.PowerValue,
                request.ScPointValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Task.FromResult(Publish(Failed(
                $"资源参数无效：资金/电力必须在 {ResourceValueSettings.MinResourceValue}..{ResourceValueSettings.MaxResourceValue}，协议点必须在 {ResourceValueSettings.MinScPointValue}..{ResourceValueSettings.MaxScPointValue}。")));
        }

        return _commandQueue.RunAsync(_ =>
        {
            controller!.WriteResourceValues(settings);
            return Task.FromResult(Publish(Succeeded("资源值已写入。")));
        }, cancellationToken);
    }

    public Task<TrainerWebCommandResult> ExecuteReinforcementAsync(
        TrainerReinforcementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        var feature = RequireFeature(WeNeedBackRawName);
        ReinforcementSettings settings;
        try
        {
            settings = new ReinforcementSettings(request.UnitId, request.Count, request.Rank);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Task.FromResult(Publish(Failed(
                $"增援参数无效：单位 ID 不能为 0，数量必须在 {ReinforcementSettings.MinCount}..{ReinforcementSettings.MaxCount}，等级必须在 {ReinforcementSettings.MinRank}..{ReinforcementSettings.MaxRank}。")));
        }

        return _commandQueue.RunAsync(async token =>
        {
            var result = await controller!.TriggerActionAndWaitForConsumptionAsync(
                    feature,
                    settings,
                    FeatureDispatchDefaults.Timeout,
                    FeatureDispatchDefaults.PollInterval,
                    cancellationToken: token,
                    onWaitStatusChanged: CreateWaitStatusCallback())
                .ConfigureAwait(false);
            return Publish(ActionResult("增援", result));
        }, cancellationToken);
    }

    public Task<TrainerWebCommandResult> ExecuteReinforcementQueueAsync(
        TrainerReinforcementQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        if (request.Entries is null || request.Entries.Count == 0)
        {
            return Task.FromResult(Publish(Failed("增援队列为空。")));
        }

        var invalidReinforcementIndex = FirstInvalidReinforcementEntryIndex(request.Entries);
        if (invalidReinforcementIndex >= 0)
        {
            return Task.FromResult(Publish(Failed(
                $"增援队列参数无效：第 {invalidReinforcementIndex + 1} 项的单位 ID、数量或等级超出允许范围。")));
        }

        var feature = RequireFeature(WeNeedBackRawName);
        var entries = request.Entries
            .Select((entry, index) => new ReinforcementQueueEntry(
                $"手机增援 {index + 1}",
                $"0x{entry.UnitId:X8}",
                entry.Count.ToString(),
                entry.Rank.ToString()))
            .ToArray();
        return _commandQueue.RunAsync(async token =>
        {
            var results = await ReinforcementQueueRunner.ExecuteAsync(
                    entries,
                    controller!,
                    feature,
                    FeatureDispatchDefaults.Timeout,
                    FeatureDispatchDefaults.PollInterval,
                    token,
                    onWaitStatusChanged: CreateWaitStatusCallback())
                .ConfigureAwait(false);
            return Publish(QueueResult("增援队列", results.Count, results.Count(result => result.Status == ReinforcementQueueItemStatus.Executed)));
        }, cancellationToken);
    }

    public Task<TrainerWebCommandResult> GrantSecretProtocolAsync(
        TrainerSecretProtocolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        if (!HasSecretProtocolId(request))
        {
            return Task.FromResult(Publish(Failed("秘密协议参数无效：PlayerTech 和 Upgrade 不能同时为 0。")));
        }

        var feature = RequireFeature(SecretProtocolGrantRawName);
        var settings = new SecretProtocolGrantSettings(request.PlayerTechId, request.UpgradeId);
        return _commandQueue.RunAsync(async token =>
        {
            controller!.WriteSecretProtocolGrantSettings(settings);
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                    feature,
                    timeout: FeatureDispatchDefaults.Timeout,
                    pollInterval: FeatureDispatchDefaults.PollInterval,
                    cancellationToken: token,
                    onWaitStatusChanged: CreateWaitStatusCallback())
                .ConfigureAwait(false);
            return Publish(ActionResult("秘密协议", result));
        }, cancellationToken);
    }

    public Task<TrainerWebCommandResult> GrantSecretProtocolQueueAsync(
        TrainerSecretProtocolQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        if (request.Entries is null || request.Entries.Count == 0)
        {
            return Task.FromResult(Publish(Failed("秘密协议队列为空。")));
        }

        var invalidSecretProtocolIndex = FirstInvalidSecretProtocolEntryIndex(request.Entries);
        if (invalidSecretProtocolIndex >= 0)
        {
            return Task.FromResult(Publish(Failed(
                $"秘密协议队列参数无效：第 {invalidSecretProtocolIndex + 1} 项的 PlayerTech 和 Upgrade 不能同时为 0。")));
        }

        var feature = RequireFeature(SecretProtocolGrantRawName);
        var entries = request.Entries
            .Select((entry, index) => new SecretProtocolQueueEntry(new SecretProtocolEntry(
                "手机远程",
                string.Empty,
                $"秘密协议 {index + 1}",
                null,
                null,
                ExplicitPlayerTechId: entry.PlayerTechId,
                ExplicitUpgradeId: entry.UpgradeId)))
            .ToArray();
        return _commandQueue.RunAsync(async token =>
        {
            var results = await SecretProtocolQueueRunner.ExecuteAsync(
                    entries,
                    controller!,
                    feature,
                    FeatureDispatchDefaults.Timeout,
                    FeatureDispatchDefaults.PollInterval,
                    token,
                    onWaitStatusChanged: CreateWaitStatusCallback())
                .ConfigureAwait(false);
            return Publish(QueueResult("秘密协议队列", results.Count, results.Count(result => result.Status == SecretProtocolQueueItemStatus.Executed)));
        }, cancellationToken);
    }

    public async Task<TrainerWebCommandResult> ExecuteActionAsync(
        string featureId,
        TrainerActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var feature = FindFeature(featureId);
        if (feature is null)
        {
            return Publish(Failed($"未知功能：{featureId}。"));
        }

        if (FeatureDispatchDefaults.IsToggle(feature))
        {
            return Publish(Failed($"功能不是动作项：{featureId}。请使用 /api/toggles 端点。"));
        }

        if (RequiresActionParameters(feature))
        {
            return Publish(Failed($"功能需要专用参数：{feature.DisplayName}。请使用对应专用端点。"));
        }

        var capabilityFailure = RequireFeatureCapability(feature);
        if (capabilityFailure is not null)
        {
            return Publish(capabilityFailure);
        }

        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Publish(ready);
        }

        return await _commandQueue.RunAsync(async token =>
        {
            if (request.TargetHealth.HasValue)
            {
                controller!.WriteTargetHealthValue(request.TargetHealth.Value);
            }

            var result = await controller!.TriggerActionAndWaitForConsumptionAsync(
                feature,
                timeout: FeatureDispatchDefaults.Timeout,
                pollInterval: FeatureDispatchDefaults.PollInterval,
                cancellationToken: token,
                onWaitStatusChanged: CreateWaitStatusCallback()).ConfigureAwait(false);

            return Publish(ActionResult(feature.DisplayName, result));
        }, cancellationToken).ConfigureAwait(false);
    }

    public TrainerSelectedUnitResponse? ReadSelectedUnit()
    {
        var controller = _session.FeatureController;
        if (controller is null)
        {
            return null;
        }

        try
        {
            var unitCode = controller.ReadSelectedUnitCode();
            var gameMode = controller.ReadGameMode();
            var gameModeName = gameMode switch
            {
                9 => "主菜单",
                2 => "遭遇战",
                8 => "战役",
                _ => $"未知({gameMode})"
            };

            return new TrainerSelectedUnitResponse(
                unitCode,
                $"0x{unitCode:X8}",
                gameMode,
                gameModeName);
        }
        catch
        {
            return null;
        }
    }

    public Task<TrainerWebCommandResult> ReplaceTemplateModelAsync(
        TrainerTemplateModelReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        var feature = RequireFeature(TemplateModelReplacementRawName);
        return _commandQueue.RunAsync(async token =>
        {
            controller!.WriteTemplateModelReplacementSettings(
                TemplateModelReplacementSettings.Parse(request.TemplateName, request.NewModelPath));
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                feature,
                timeout: FeatureDispatchDefaults.Timeout,
                pollInterval: FeatureDispatchDefaults.PollInterval,
                cancellationToken: token,
                onWaitStatusChanged: CreateWaitStatusCallback()).ConfigureAwait(false);
            return Publish(ActionResult("模板模型替换", result));
        }, cancellationToken);
    }

    public Task<TrainerWebCommandResult> ReplaceTemplateWeaponAsync(
        TrainerTemplateWeaponReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(Publish(ready));
        }

        var feature = RequireFeature(TemplateWeaponReplacementRawName);
        return _commandQueue.RunAsync(async token =>
        {
            controller!.WriteTemplateWeaponReplacementSettings(
                TemplateWeaponReplacementSettings.Parse(request.TemplateName, request.NewWeaponName));
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                feature,
                timeout: FeatureDispatchDefaults.Timeout,
                pollInterval: FeatureDispatchDefaults.PollInterval,
                cancellationToken: token,
                onWaitStatusChanged: CreateWaitStatusCallback()).ConfigureAwait(false);
            return Publish(ActionResult("模板武器替换", result));
        }, cancellationToken);
    }

    public TrainerGameStateResponse? GetGameState()
    {
        var controller = _session.FeatureController;
        if (controller is null)
        {
            return null;
        }

        try
        {
            var gameMode = controller.ReadGameMode();
            var gameModeName = gameMode switch
            {
                9 => "主菜单",
                2 => "遭遇战",
                8 => "战役",
                _ => $"未知({gameMode})"
            };

            return new TrainerGameStateResponse(
                gameMode,
                gameModeName,
                gameMode != 9,
                GetStatus());
        }
        catch
        {
            return null;
        }
    }

    private TrainerWebCommandResult? RequireController(out ITrainerFeatureController? controller)
    {
        controller = _session.FeatureController;
        return _session.ArePatchesInstalled && controller is not null
            ? null
            : Failed("请先检测进程并安装 patch。");
    }

    private TrainerWebCommandResult? RequireFeatureCapability(TrainerFeature feature)
    {
        var capability = _session.GetFeatureCapability(feature);
        return capability.State == FeatureCapabilityState.Ready
            ? null
            : Failed(capability.Reason);
    }

    private TrainerFeature RequireFeature(string featureId)
    {
        return FindFeature(featureId)
            ?? throw new InvalidOperationException($"找不到功能：{featureId}。");
    }

    private TrainerFeature? FindFeature(string featureId)
    {
        return _features.FirstOrDefault(feature =>
            feature.RawName.Equals(featureId, StringComparison.OrdinalIgnoreCase) ||
            feature.DisplayName.Equals(featureId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequiresActionParameters(TrainerFeature feature)
    {
        return feature.RawName is
            WeNeedBackRawName or
            SecretProtocolGrantRawName or
            TemplateModelReplacementRawName or
            TemplateWeaponReplacementRawName or
            SetTargetHealthRawName;
    }

    private static int FirstInvalidReinforcementEntryIndex(IReadOnlyList<TrainerReinforcementRequest> entries)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            try
            {
                _ = new ReinforcementSettings(entry.UnitId, entry.Count, entry.Rank);
            }
            catch (ArgumentOutOfRangeException)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FirstInvalidSecretProtocolEntryIndex(IReadOnlyList<TrainerSecretProtocolRequest> entries)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (!HasSecretProtocolId(entries[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasSecretProtocolId(TrainerSecretProtocolRequest request)
    {
        return request.PlayerTechId != 0 || request.UpgradeId != 0;
    }

    private static TrainerWebCommandResult ActionResult(string label, ActionDispatchResult result)
    {
        return result switch
        {
            ActionDispatchResult.Consumed => Succeeded($"{label}命令已执行。"),
            ActionDispatchResult.NotRequired => Succeeded($"{label}命令已触发。"),
            ActionDispatchResult.TimedOut => Failed($"{label}命令已写入，但尚未被游戏循环消费。"),
            _ => Failed($"{label}命令返回未知状态。")
        };
    }

    private static TrainerWebCommandResult QueueResult(string label, int total, int executed)
    {
        return executed == total
            ? Succeeded($"{label}已执行：成功 {executed}/{total}。")
            : Failed($"{label}执行完成：成功 {executed}/{total}。");
    }

    private static TrainerWebCommandResult Succeeded(string message)
    {
        return new TrainerWebCommandResult(true, message);
    }

    private static TrainerWebCommandResult Failed(string message)
    {
        return new TrainerWebCommandResult(false, message);
    }

    private TrainerWebCommandResult Publish(TrainerWebCommandResult result)
    {
        _broadcaster?.Publish(TrainerWebStateMessage.Command(result));
        return result;
    }

    /// <summary>
    /// Builds an <see cref="DispatchWaitStatus"/> callback that broadcasts
    /// pause-aware wait feedback to the Web/WebSocket clients. Returns null
    /// when no broadcaster is wired so the controller skips the callback path.
    /// R2c: surfaces "waiting for resume" feedback while the trainer holds
    /// a dispatch open during a paused game.
    /// </summary>
    private Action<RayaTrainer.Core.Features.DispatchWaitStatus>? CreateWaitStatusCallback()
    {
        if (_broadcaster is null) return null;
        return status => _broadcaster.Publish(TrainerWebStateMessage.Status(status switch
        {
            RayaTrainer.Core.Features.DispatchWaitStatus.PausedWaiting => "游戏已暂停，等待恢复…",
            RayaTrainer.Core.Features.DispatchWaitStatus.Resumed => "游戏已恢复，继续执行…",
            RayaTrainer.Core.Features.DispatchWaitStatus.GraceExpired => "等待超时，已放弃当前操作。",
            _ => "执行中…"
        }));
    }
}
