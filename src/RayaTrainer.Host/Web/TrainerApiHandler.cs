using RayaTrainer.Host.Services;
using RayaTrainer.Host.Web.State;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using static RayaTrainer.Host.Web.TrainerPresetMapper;

namespace RayaTrainer.Host.Web;

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
    private readonly UpgradeNameResolver _nameResolver;
    private readonly IFeatureToggleCoordinator? _featureToggleCoordinator;

    public TrainerApiHandler(
        ITrainerSessionService session,
        IGameApiCommandQueue commandQueue,
        IReadOnlyList<TrainerFeature> features,
        TrainerAppSettingsStore? settingsStore = null,
        ITrainerPresetSource? presetSource = null,
        ITrainerSavedPresetSource? savedPresetSource = null,
        IGameStateBroadcaster? broadcaster = null,
        IFeatureToggleCoordinator? featureToggleCoordinator = null)
    {
        _session = session;
        _commandQueue = commandQueue;
        _features = features;
        _settingsStore = settingsStore ?? new TrainerAppSettingsStore();
        _presetSource = presetSource;
        _savedPresetSource = savedPresetSource;
        _broadcaster = broadcaster;
        _nameResolver = new UpgradeNameResolver();
        _featureToggleCoordinator = featureToggleCoordinator;
    }

    public TrainerWebStatusResponse GetStatus()
    {
        return new TrainerWebStatusResponse(
            _session.ArePatchesInstalled,
            _session.FeatureController is IAgentFeatureController { SupportsDirectGameApi: true },
            _session.TargetProcessId,
            _session.InstalledHookCount);
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

    public ReinforcementCatalogResponse GetReinforcementCatalog()
    {
        var entries = ReinforcementUnitCatalog.LoadWithCustomFile()
            .Select(u => new ReinforcementCatalogEntry(
                u.Mod,
                u.Faction,
                u.CodeText,
                u.Code,
                u.Name,
                u.SourceId))
            .ToArray();
        return new ReinforcementCatalogResponse(entries);
    }

    public SecretProtocolCatalogResponse GetSecretProtocolCatalog()
    {
        var entries = SecretProtocolCatalog.LoadWithCustomFile()
            .Select(p => new SecretProtocolCatalogEntry(
                p.Mod,
                p.Faction,
                p.Name,
                p.PlayerTechIdText,
                p.UpgradeText,
                p.PlayerTechId,
                p.UpgradeId,
                p.CanGrant))
            .ToArray();
        return new SecretProtocolCatalogResponse(entries);
    }

    public async Task<TrainerWebCommandResult> SetToggleAsync(
        TrainerToggleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var feature = FindFeature(request.FeatureId);
        if (feature is null)
        {
            return Publish(Failed($"未知功能：{request.FeatureId}。"));
        }

        if (!FeatureDispatchDefaults.IsToggle(feature))
        {
            return Publish(Failed($"功能不是开关项：{request.FeatureId}。"));
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

        // 走协调器（若注入）：更新 desired/observed + 触发持久化。
        // UI 线程调度由协调器实现自行负责（App 侧经 WPF Dispatcher）。
        if (_featureToggleCoordinator is not null
            && await _featureToggleCoordinator.TrySetToggleDesiredAsync(feature.RawName, request.Enabled)
                .ConfigureAwait(false))
        {
            return Publish(Succeeded(request.Enabled ? "功能已开启。" : "功能已关闭。"));
        }

        // Fallback：旧路径（无协调器或找不到 item 时）
        return await _commandQueue.RunAsync(_ =>
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

    public Task<TrainerWebQueueResult> ExecuteReinforcementQueueAsync(
        TrainerReinforcementQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(new TrainerWebQueueResult(false, ready.Message, Array.Empty<TrainerQueueItemResult>()));
        }

        if (request.Entries is null || request.Entries.Count == 0)
        {
            return Task.FromResult(new TrainerWebQueueResult(false, "增援队列为空。", Array.Empty<TrainerQueueItemResult>()));
        }

        var invalidReinforcementIndex = FirstInvalidReinforcementEntryIndex(request.Entries);
        if (invalidReinforcementIndex >= 0)
        {
            return Task.FromResult(new TrainerWebQueueResult(false, $"增援队列参数无效：第 {invalidReinforcementIndex + 1} 项的单位 ID、数量或等级超出允许范围。", Array.Empty<TrainerQueueItemResult>()));
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
            return BuildReinforcementQueueResult(results);
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

    public Task<TrainerWebQueueResult> GrantSecretProtocolQueueAsync(
        TrainerSecretProtocolQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ready = RequireController(out var controller);
        if (ready is not null)
        {
            return Task.FromResult(new TrainerWebQueueResult(false, ready.Message, Array.Empty<TrainerQueueItemResult>()));
        }

        if (request.Entries is null || request.Entries.Count == 0)
        {
            return Task.FromResult(new TrainerWebQueueResult(false, "秘密协议队列为空。", Array.Empty<TrainerQueueItemResult>()));
        }

        var invalidSecretProtocolIndex = FirstInvalidSecretProtocolEntryIndex(request.Entries);
        if (invalidSecretProtocolIndex >= 0)
        {
            return Task.FromResult(new TrainerWebQueueResult(false, $"秘密协议队列参数无效：第 {invalidSecretProtocolIndex + 1} 项的 PlayerTech 和 Upgrade 不能同时为 0。", Array.Empty<TrainerQueueItemResult>()));
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
            return BuildSecretProtocolQueueResult(results);
        }, cancellationToken);
    }

    public Task<FeaturePresetsResponse> GetFeaturePresets(CancellationToken ct = default)
    {
        var presets = _presetSource?.GetFeaturePresets() ?? Array.Empty<FeaturePreset>();
        return Task.FromResult(new FeaturePresetsResponse(presets));
    }

    public Task<TrainerWebCommandResult> SaveFeaturePreset(FeaturePresetSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Task.FromResult(Publish(Failed("预设名不能为空")));
        _presetSource?.SaveFeaturePreset(request.Name.Trim(), request.Snapshot);
        return Task.FromResult(Publish(Succeeded("预设已保存。")));
    }

    public Task<TrainerWebCommandResult> DeleteFeaturePreset(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(Publish(Failed("预设名不能为空")));
        _presetSource?.DeleteFeaturePreset(name.Trim());
        return Task.FromResult(Publish(Succeeded("预设已删除。")));
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

    public FeatureCapabilitySnapshot GetObjectUpgradeCapability() =>
        _session.GetFeatureCapability(TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature);

    public TrainerUnitUpgradesResponse? ReadSelectedUnitUpgrades()
    {
        var controller = _session.FeatureController;
        if (controller is null) return null;

        try
        {
            var snapshot = controller.ReadSelectedUnitUpgrades();
            var upgrades = new List<TrainerUnitUpgradeItem>();
            for (var i = 0u; i < snapshot.Count; i++)
            {
                var hash = snapshot.Hashes[(int)i];
                upgrades.Add(new TrainerUnitUpgradeItem(
                    hash,
                    _nameResolver.ResolveDisplayNameOrFallback(hash),
                    _nameResolver.TryResolveName(hash)?.Description ?? ""));
            }

            var message = snapshot.ThingTemplateAddress == 0
                ? "请先在游戏中选中一个单位"
                : snapshot.Count == 0
                    ? "当前单位没有可授予的对象级升级"
                    : "";

            return new TrainerUnitUpgradesResponse(
                snapshot.UnitTypeId,
                $"0x{snapshot.UnitTypeId:X8}",
                message,
                upgrades);
        }
        catch
        {
            return null;
        }
    }

    public Task<TrainerWebCommandResult> GrantObjectUpgradeOnSelectedSameTypeAsync(
        uint upgradeHash,
        CancellationToken cancellationToken = default)
    {
        var capabilityFailure = RequireFeatureCapability(TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature);
        if (capabilityFailure is not null)
        {
            return Task.FromResult(Publish(capabilityFailure));
        }

        var ready = RequireController(out var controller);
        if (ready is not null) return Task.FromResult(Publish(ready));
        if (upgradeHash == 0) return Task.FromResult(Publish(Failed("升级参数无效：hash 不能为 0。", "INVALID_UPGRADE_HASH")));

        return _commandQueue.RunAsync(token =>
        {
            var result = controller!.GrantObjectUpgradeOnSelectedSameType(upgradeHash);
            var (success, reasonCode, message) = result switch
            {
                GameApiDispatchStatus.Completed => (true, null as string, "升级已授予。"),
                GameApiDispatchStatus.Disabled => (false, "GRANT_DISABLED", "授予升级失败：当前状态不可授予（可能已被授予或非对象级升级）。"),
                GameApiDispatchStatus.TimedOut => (false, "GRANT_TIMEOUT", "授予升级命令已超时，请重试。"),
                GameApiDispatchStatus.Failed => (false, "GRANT_FAILED", "授予升级失败。"),
                _ => (false, "GRANT_UNKNOWN", $"授予升级返回未知状态：{result}。")
            };
            return Task.FromResult(Publish(new TrainerWebCommandResult(success, message, reasonCode)));
        }, cancellationToken);
    }

    // --- Product Control Plane (U4) ---
    // Web is a remote/backup surface over the same I4 IProductControlSession as WPF, reached
    // through the internal IProductControlSessionHost seam (never a self-constructed session,
    // never a legacy feature handler). Native Overlay consumes Agent-owned projections and
    // mirrors the aligned result-state subset. Agent offline / stale revision /
    // expired result are structured DTO fields, not thrown exceptions or reassembled strings.

    private static readonly TimeSpan ProductControlTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProductResultPollInterval = TimeSpan.FromMilliseconds(50);
    private const int ProductResultPollAttempts = 5;

    private static readonly ProductControlStatusInfo OfflineProductControlStatus = StatusInfo(
        ProductControlResultClassification.Offline, "尚未连接目标进程。");

    private IProductControlSession? ProductControlSession =>
        (_session as IProductControlSessionHost)?.ProductControl;

    /// <summary>
    /// Lists the generated product catalog projected onto public JSON records: id, display name,
    /// kind, and the DECLARED scope/binding/reapply plus typed parameter descriptors. The submit
    /// route derives its <see cref="ContextBinding"/> from the same projection by id.
    /// </summary>
    public ProductControlCatalogResponse GetProductControlCatalog()
    {
        var products = ProductCatalogProjection.PublicEntries
            .Select(entry => new ProductControlCatalogProduct(
                entry.ProductId.Value,
                entry.DisplayName,
                entry.Kind.ToString(),
                entry.Scope.ToString(),
                entry.Binding.ToString(),
                entry.Reapply.ToString(),
                entry.Parameters
                    .Select(parameter => new ProductControlCatalogParameter(
                        parameter.Name, parameter.Kind.ToString()))
                    .ToArray()))
            .ToArray();
        return new ProductControlCatalogResponse(products);
    }

    public async Task<ProductControlContextResponse> GetProductControlContextAsync(
        CancellationToken cancellationToken = default)
    {
        var session = ProductControlSession;
        if (session is null)
        {
            return OfflineContext(OfflineProductControlStatus);
        }

        var outcome = await session
            .QueryMatchContextAsync(
                ScopeMask.CurrentPlayer | ScopeMask.AllOtherPlayers | ScopeMask.AllUnits | ScopeMask.SelectionSummary,
                default,
                ProductControlTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        var status = StatusInfo(ProductControlResultClassifier.FromStatus(outcome.Status), outcome.Detail);
        if (outcome.Value is not { } context)
        {
            return OfflineContext(status);
        }

        return new ProductControlContextResponse(
            status,
            true,
            context.Lifecycle.ToString(),
            context.RuntimeFlags.ToString(),
            context.ScopeAvailabilityMask.ToString(),
            context.ActivePlayerCount > 0,
            context.ActivePlayerCount,
            context.SnapshotGeneration.Value);
    }

    public async Task<ProductControlSubmitResponse> SubmitProductIntentAsync(
        ProductControlSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = ProductControlSession;
        if (session is null)
        {
            return new ProductControlSubmitResponse(
                OfflineProductControlStatus, false, 0,
                ProductAcceptance.Rejected.ToString(), ProductErrorCode.None.ToString());
        }

        ProductId productId;
        try
        {
            productId = new ProductId(request.ProductId?.Trim() ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            return new ProductControlSubmitResponse(
                StatusInfo(ProductControlResultClassification.Rejected, $"产品 ID 无效：{exception.Message}"),
                false, 0, ProductAcceptance.Rejected.ToString(), ProductErrorCode.InvalidRequest.ToString());
        }

        if (!ProductCatalogProjection.TryGetPublic(productId.Value, out var catalogEntry))
        {
            return new ProductControlSubmitResponse(
                StatusInfo(
                    ProductControlResultClassification.Rejected,
                    $"产品不在可用目录中：{productId.Value}。"),
                false,
                0,
                ProductAcceptance.Rejected.ToString(),
                ProductErrorCode.ProductUnavailable.ToString());
        }

        if (!TryBuildProductParameters(catalogEntry, request.Amount, out var parameters, out var parameterError))
        {
            return new ProductControlSubmitResponse(
                StatusInfo(ProductControlResultClassification.Rejected, parameterError),
                false,
                0,
                ProductAcceptance.Rejected.ToString(),
                ProductErrorCode.InvalidRequest.ToString());
        }

        // Derive the ContextBinding and typed parameter shape from the same public catalog.
        var submitRequest = new SubmitIntentRequest(
            productId,
            catalogEntry.ToContextBinding(),
            parameters);

        var submitOutcome = await session
            .SubmitProductIntentAsync(submitRequest, ProductControlTimeout, cancellationToken)
            .ConfigureAwait(false);

        // Fetch the layered result for an accepted intent so the classification matches the
        // Overlay/WPF (Ok / EffectUnknown / Superseded rather than a bare "pending").
        ProductControlOutcome<ProductResult>? resultOutcome = null;
        if (submitOutcome.Value is { Acceptance: ProductAcceptance.Accepted, IntentId.IsInvalid: false } accepted)
        {
            resultOutcome = await QueryProductResultUntilSettledAsync(
                    session,
                    accepted.IntentId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var classification = ProductControlResultClassifier.Classify(submitOutcome, resultOutcome);
        var response = submitOutcome.Value;
        return new ProductControlSubmitResponse(
            StatusInfo(classification, submitOutcome.Detail),
            response?.Acceptance == ProductAcceptance.Accepted,
            response?.IntentId.Value ?? 0,
            (response?.Acceptance ?? ProductAcceptance.Rejected).ToString(),
            (response?.ErrorCode ?? ProductErrorCode.None).ToString());
    }

    public async Task<ProductControlResultResponse> GetProductResultAsync(
        ulong intentId,
        CancellationToken cancellationToken = default)
    {
        var session = ProductControlSession;
        if (session is null)
        {
            return OfflineResult(OfflineProductControlStatus, intentId, "");
        }

        var outcome = await session
            .GetProductResultAsync(new IntentId(intentId), ProductControlTimeout, cancellationToken)
            .ConfigureAwait(false);

        var classification = ClassifyResult(outcome);
        var status = StatusInfo(classification, outcome.Detail);
        if (outcome.Value is not { } result)
        {
            return OfflineResult(status, intentId, outcome.Detail);
        }

        return new ProductControlResultResponse(
            status,
            result.IntentId.Value,
            result.Availability.ToString(),
            result.Admission.ToString(),
            result.Execution.ToString(),
            result.Effect.ToString(),
            result.Compensation.ToString(),
            result.ErrorCode.ToString(),
            result.ProductId?.Value,
            result.Detail);
    }

    public async Task<ProductControlDesiredResponse> GetProductDesiredAsync(
        uint offset,
        uint limit,
        CancellationToken cancellationToken = default)
    {
        var effectiveLimit = limit == 0
            ? (uint)ProductControlWireCodec.MaxGetDesiredLimit
            : Math.Min(limit, (uint)ProductControlWireCodec.MaxGetDesiredLimit);

        var session = ProductControlSession;
        if (session is null)
        {
            return new ProductControlDesiredResponse(
                OfflineProductControlStatus, false, 0, 0, offset, effectiveLimit,
                Array.Empty<ProductControlDesiredItem>());
        }

        var outcome = await session
            .GetDesiredIntentsAsync(offset, effectiveLimit, ProductControlTimeout, cancellationToken)
            .ConfigureAwait(false);

        var status = StatusInfo(ProductControlResultClassifier.FromStatus(outcome.Status), outcome.Detail);
        if (outcome.Value is not { } desired)
        {
            return new ProductControlDesiredResponse(
                status, false, 0, 0, offset, effectiveLimit,
                Array.Empty<ProductControlDesiredItem>());
        }

        var items = desired.Items
            .Select(item => new ProductControlDesiredItem(
                item.IntentId.Value,
                item.ProductId.Value,
                item.BindingKind.ToString(),
                item.ScopeKind.ToString(),
                item.ReapplyPolicy.ToString(),
                item.DesiredState.ToString()))
            .ToArray();

        return new ProductControlDesiredResponse(
            status,
            true,
            desired.PolicyRevision.Value,
            desired.TotalCount,
            offset,
            effectiveLimit,
            items);
    }

    private static ProductControlContextResponse OfflineContext(ProductControlStatusInfo status) =>
        new(status, false, MatchLifecycle.Unavailable.ToString(),
            RuntimeFlags.None.ToString(), ScopeMask.None.ToString(), false, 0, 0);

    private static ProductControlResultResponse OfflineResult(
        ProductControlStatusInfo status, ulong intentId, string detail) =>
        new(status, intentId,
            ResultAvailability.UnknownIntent.ToString(),
            AdmissionState.Pending.ToString(),
            ExecutionState.NotStarted.ToString(),
            EffectState.NotApplicable.ToString(),
            CompensationState.NotRequired.ToString(),
            ProductErrorCode.None.ToString(),
            null,
            detail);

    private static ProductControlStatusInfo StatusInfo(
        ProductControlResultClassification classification, string detail) =>
        new(classification.ToString(),
            ProductControlResultClassifier.ToLabel(classification),
            detail);

    // Classify a standalone layered result exactly as the shared classifier would if it had
    // followed an accepted submit: a synthetic Accepted submit forces the classifier down its
    // layered-result path, so the Web surface reports the identical classification vocabulary.
    private static ProductControlResultClassification ClassifyResult(
        ProductControlOutcome<ProductResult> resultOutcome)
    {
        var syntheticSubmit = ProductControlOutcome<SubmitIntentResponse>.Ok(
            new SubmitIntentResponse(
                0, ProductAcceptance.Accepted, ProductErrorCode.None,
                resultOutcome.Value?.IntentId ?? default));
        return ProductControlResultClassifier.Classify(syntheticSubmit, resultOutcome);
    }

    private static bool TryBuildProductParameters(
        ProductCatalogEntry entry,
        long? amount,
        out IReadOnlyList<ScriptValue> parameters,
        out string error)
    {
        parameters = [];
        error = string.Empty;
        if (entry.Parameters.Count == 0)
        {
            if (amount.HasValue)
            {
                error = $"产品“{entry.DisplayName}”不接受参数。";
                return false;
            }
            return true;
        }

        if (entry.Parameters.Count != 1 ||
            entry.Parameters[0].Kind != ScriptValueKind.Integer)
        {
            error = $"产品“{entry.DisplayName}”的参数形状暂不受 Web 控制面支持。";
            return false;
        }

        if (!amount.HasValue)
        {
            error = $"缺少整数参数“{entry.Parameters[0].Name}”。";
            return false;
        }

        parameters = [ScriptValue.Integer(amount.Value)];
        return true;
    }

    private static async Task<ProductControlOutcome<ProductResult>?> QueryProductResultUntilSettledAsync(
        IProductControlSession session,
        IntentId intentId,
        CancellationToken cancellationToken)
    {
        var accepted = ProductControlOutcome<SubmitIntentResponse>.Ok(
            new SubmitIntentResponse(
                ProductControlWireCodec.AgentStatusOk,
                ProductAcceptance.Accepted,
                ProductErrorCode.None,
                intentId));
        ProductControlOutcome<ProductResult>? latest = null;
        for (var attempt = 0; attempt < ProductResultPollAttempts; attempt++)
        {
            latest = await session
                .GetProductResultAsync(intentId, ProductControlTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (ProductControlResultClassifier.Classify(accepted, latest) !=
                ProductControlResultClassification.Pending)
            {
                break;
            }

            if (attempt + 1 < ProductResultPollAttempts)
            {
                await Task.Delay(ProductResultPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        return latest;
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
            : Failed(capability.Reason, capability.ReasonCode);
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

    private static TrainerWebQueueResult BuildReinforcementQueueResult(
        IReadOnlyList<ReinforcementQueueResult> results)
    {
        var items = results
            .Select((r, i) => new TrainerQueueItemResult(i, r.Status.ToString(), r.Message))
            .ToArray();
        var executed = results.Count(r => r.Status == ReinforcementQueueItemStatus.Executed);
        var success = executed == results.Count;
        return new TrainerWebQueueResult(
            success,
            $"成功 {executed}/{results.Count}。",
            items);
    }

    private static TrainerWebQueueResult BuildSecretProtocolQueueResult(
        IReadOnlyList<SecretProtocolQueueResult> results)
    {
        var items = results
            .Select((r, i) => new TrainerQueueItemResult(i, r.Status.ToString(), r.Message))
            .ToArray();
        var executed = results.Count(r => r.Status == SecretProtocolQueueItemStatus.Executed);
        var success = executed == results.Count;
        return new TrainerWebQueueResult(
            success,
            $"成功 {executed}/{results.Count}。",
            items);
    }

    private static TrainerWebCommandResult Succeeded(string message)
    {
        return new TrainerWebCommandResult(true, message);
    }

    private static TrainerWebCommandResult Failed(string message, string? reasonCode = null)
    {
        return new TrainerWebCommandResult(false, message, reasonCode);
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
