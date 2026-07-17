using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Core.Features;

public sealed partial class AgentFeatureController : IAgentFeatureController
{
    private const uint HealthModeExplicit = 1;
    private const uint HealthModeMax = 2;
    private const uint HealthModeMin = 3;
    private const uint HealthModeRestore = 4;
    private const uint ExpandedProductionQueueLimit = 999;
    private const uint DefaultProductionQueueLimit = 1;

    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GameApiCommandTimeout = TimeSpan.FromSeconds(8);

    private readonly IAgentClient _client;
    private readonly int _processId;
    private readonly bool _supportsDirectGameApi;
    private readonly AddressResolver _moduleAddressResolver;
    private readonly Dictionary<NativeFeatureStateId, uint> _nativeFeatureStates = [];
    private FeatureStatesResponse? _lastObserved;
    private static readonly HashSet<NativeFeatureStateId> PulseStateIds = new()
    {
        NativeFeatureStateId.MoneyPulse,           // 1
        NativeFeatureStateId.ChallengeMoneyPulse,  // 13
        NativeFeatureStateId.AutoRepairPulse,      // 21
        NativeFeatureStateId.RestoreOrePulse       // 23
    };
    private ReinforcementSettings _reinforcementSettings = ReinforcementSettings.Default;
    private ResourceValueSettings _resourceValueSettings = ResourceValueSettings.Default;
    private SecretProtocolGrantSettings _secretProtocolGrantSettings = SecretProtocolGrantSettings.Empty;
    private TemplateModelReplacementSettings? _templateModelReplacementSettings;
    private TemplateWeaponReplacementSettings? _templateWeaponReplacementSettings;
    private float _targetHealth;
    private float _targetMaxHealth;
    private SecretProtocolBindingProbeStatus _lastProbeStatus;

    public AgentFeatureController(
        IAgentClient client,
        int processId,
        AgentStatusPayload status,
        bool supportsDirectGameApi = true)
    {
        _client = client;
        _processId = processId;
        _supportsDirectGameApi = supportsDirectGameApi;
        _moduleAddressResolver = new AddressResolver(
            unchecked((nint)status.ModuleBase),
            new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase));
    }

    public bool SupportsDirectGameApi => _supportsDirectGameApi;

    public void SetToggle(TrainerFeature feature, bool enabled)
    {
        var stateId = GetNativeFeatureStateId(feature);
        WriteNativeFeatureState(stateId, enabled ? 1u : 0u);
        ApplyRuntimePatchSetForToggle(feature, enabled);
    }

    public void TriggerAction(TrainerFeature feature)
    {
        TriggerAction(feature, reinforcementSettings: null);
    }

    public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings)
    {
        ExecuteNativeAction(feature, reinforcementSettings, GameApiCommandTimeout);
    }

    public async Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
        TrainerFeature feature,
        ReinforcementSettings? reinforcementSettings = null,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        Action? onDispatched = null,
        CancellationToken cancellationToken = default,
        TimeSpan? pausedGracePeriod = null,
        Action<DispatchWaitStatus>? onWaitStatusChanged = null)
    {
        var dispatchStatus = await Task.Run(
                () => ExecuteNativeAction(feature, reinforcementSettings, timeout ?? GameApiCommandTimeout),
                cancellationToken)
            .ConfigureAwait(false);
        onDispatched?.Invoke();
        return ToActionDispatchResult(dispatchStatus);
    }

    public void WriteReinforcementSettings(ReinforcementSettings settings)
    {
        _reinforcementSettings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void WriteResourceValues(ResourceValueSettings settings)
    {
        _resourceValueSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        WriteNativeFeatureStates(
        [
            (NativeFeatureStateId.MoneyAmount, unchecked((uint)settings.MoneyAmount)),
            (NativeFeatureStateId.PowerValue, unchecked((uint)settings.PowerValue)),
            (NativeFeatureStateId.SecretProtocolPointValue, unchecked((uint)settings.ScPointValue)),
            (NativeFeatureStateId.SelectedUnitMaxHealthBits, BitConverter.SingleToUInt32Bits(9999999f))
        ]);
    }

    public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings)
    {
        _secretProtocolGrantSettings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings)
    {
        _templateModelReplacementSettings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings)
    {
        _templateWeaponReplacementSettings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult()
    {
        return new SecretProtocolBindingProbeResult(
            0, 0, 0, SecretProtocolBindingItemStatus.NotRun,
            0, SecretProtocolBindingItemStatus.NotRun, _lastProbeStatus);
    }

    public void PulseAutoRepair()
    {
        WriteNativeFeatureStates(
        [
            (NativeFeatureStateId.AutoRepair, 1),
            (NativeFeatureStateId.AutoRepairPulse, 1)
        ]);
    }

    public void ClearAutoRepairPulse()
    {
        WriteNativeFeatureStates(
        [
            (NativeFeatureStateId.AutoRepairPulse, 0),
            (NativeFeatureStateId.AutoRepair, 0)
        ]);
    }

    public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f)
    {
        _targetHealth = targetHealth;
        _targetMaxHealth = targetMaxHealth;
    }

    public uint ReadSelectedUnitCode()
    {
        EnsureDirectGameApiSupported();
        var result = _client.ReadSelectedUnitSnapshotViaGameApiAsync(
                _processId,
                new AgentGameApiReadSelectedUnitCodeRequest(
                    TimeoutMilliseconds: 5000,
                    EnableDirectGameApi: true),
                GameApiCommandTimeout)
            .GetAwaiter()
            .GetResult();
        if (result.StatusCode != AgentStatusCode.Ok ||
            result.DispatchStatus != GameApiDispatchStatus.Completed ||
            result.UnitTypeId == 0 ||
            result.ThingClassAddress == 0)
        {
            throw new InvalidDataException(
                $"Agent ReadSelectedUnitCode returned status={result.StatusCode}, dispatch={result.DispatchStatus}, " +
                $"request={result.RequestId}, tick={result.GameThreadTickBefore}->{result.GameThreadTickAfter}, " +
                $"unit=0x{result.UnitTypeId:X8}, thingClass=0x{result.ThingClassAddress:X8}.");
        }

        return result.UnitTypeId;
    }

    public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades()
    {
        EnsureDirectGameApiSupported();
        var result = _client.GetSelectedUnitUpgradesAsync(
                _processId,
                new AgentGameApiGetSelectedUnitUpgradesRequest(
                    TimeoutMilliseconds: 5000,
                    EnableDirectGameApi: true),
                GameApiCommandTimeout)
            .GetAwaiter()
            .GetResult();
        if (result.StatusCode != AgentStatusCode.Ok ||
            result.DispatchStatus != GameApiDispatchStatus.Completed)
        {
            throw new InvalidDataException(
                $"Agent GetSelectedUnitUpgrades returned status={result.StatusCode}, dispatch={result.DispatchStatus}, " +
                $"request={result.RequestId}, tick={result.GameThreadTickBefore}->{result.GameThreadTickAfter}.");
        }

        // Empty (no selection or no upgrade-triggered modules) is a successful
        // empty snapshot, not an exception. Callers branch on Count == 0.
        // Defense-in-depth: the native handler caps Count at 20, but a corrupted or
        // forged payload must not drive an unbounded allocation here.
        if (result.Count > 20)
        {
            throw new InvalidDataException(
                $"Agent GetSelectedUnitUpgrades returned Count={result.Count}, exceeds the 20-entry maximum. " +
                $"request={result.RequestId}, tick={result.GameThreadTickBefore}->{result.GameThreadTickAfter}.");
        }

        var hashes = new uint[result.Count];
        if (result.Count > 0)
        {
            hashes[0] = result.UpgradeHash0;
            if (result.Count > 1) hashes[1] = result.UpgradeHash1;
            if (result.Count > 2) hashes[2] = result.UpgradeHash2;
            if (result.Count > 3) hashes[3] = result.UpgradeHash3;
            if (result.Count > 4) hashes[4] = result.UpgradeHash4;
            if (result.Count > 5) hashes[5] = result.UpgradeHash5;
            if (result.Count > 6) hashes[6] = result.UpgradeHash6;
            if (result.Count > 7) hashes[7] = result.UpgradeHash7;
            if (result.Count > 8) hashes[8] = result.UpgradeHash8;
            if (result.Count > 9) hashes[9] = result.UpgradeHash9;
            if (result.Count > 10) hashes[10] = result.UpgradeHash10;
            if (result.Count > 11) hashes[11] = result.UpgradeHash11;
            if (result.Count > 12) hashes[12] = result.UpgradeHash12;
            if (result.Count > 13) hashes[13] = result.UpgradeHash13;
            if (result.Count > 14) hashes[14] = result.UpgradeHash14;
            if (result.Count > 15) hashes[15] = result.UpgradeHash15;
            if (result.Count > 16) hashes[16] = result.UpgradeHash16;
            if (result.Count > 17) hashes[17] = result.UpgradeHash17;
            if (result.Count > 18) hashes[18] = result.UpgradeHash18;
            if (result.Count > 19) hashes[19] = result.UpgradeHash19;
        }
        return new SelectedUnitUpgradesSnapshot(
            result.UnitTypeId,
            result.ThingTemplateAddress,
            result.Count,
            hashes);
    }

    public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null)
    {
        EnsureDirectGameApiSupported();
        var effectiveTimeout = timeout ?? GameApiCommandTimeout;
        var gameApiTimeoutMilliseconds = Math.Clamp((uint)effectiveTimeout.TotalMilliseconds, 1u, 5000u);
        var request = new AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest(
            UpgradeHash: upgradeHash,
            TimeoutMilliseconds: gameApiTimeoutMilliseconds,
            EnableDirectGameApi: true);
        var result = _client.GrantObjectUpgradeOnSelectedSameTypeAsync(_processId, request, effectiveTimeout)
            .GetAwaiter().GetResult();
        if (result.StatusCode != AgentStatusCode.Ok &&
            result.StatusCode != AgentStatusCode.TimedOut)
        {
            throw new InvalidOperationException(
                $"Agent GrantObjectUpgradeOnSelectedSameType failed: status={result.StatusCode}, dispatch={result.DispatchStatus}.");
        }

        return result.DispatchStatus;
    }

    private void EnsureDirectGameApiSupported()
    {
        if (!_supportsDirectGameApi)
        {
            throw new NotSupportedException("当前版本尚未启用 Direct GameApi。");
        }
    }

    public uint ReadGameThreadTick()
    {
        var status = _client.GetStatusAsync(_processId, DefaultCommandTimeout, CancellationToken.None)
            .GetAwaiter().GetResult();
        EnsureOk(status.StatusCode, AgentCommand.GetStatus);
        return status.GameThreadTick;
    }

    public int ReadGameMode()
    {
        var result = _client.GetGameModeAsync(_processId, DefaultCommandTimeout, CancellationToken.None)
            .GetAwaiter().GetResult();
        EnsureOk(result.StatusCode, AgentCommand.GetGameMode);
        return result.GameMode;
    }

    public void Reset(TrainerFeature feature)
    {
        if (TryGetNativeFeatureStateId(feature, out var stateId))
        {
            WriteNativeFeatureState(stateId, 0);
            ApplyRuntimePatchSetForToggle(feature, enabled: false);
        }
    }

    private static bool TryGetSelectedUnitHealthMode(TrainerFeature feature, out uint mode)
    {
        mode = feature.RawName switch
        {
            "Select Unit HP MAX" => HealthModeMax,
            "Select Unit HP MIN" => HealthModeMin,
            "Restore Select Unit Normal HP" => HealthModeRestore,
            _ => 0
        };

        return mode != 0;
    }

    private static bool TryGetProductionQueueLimit(TrainerFeature feature, out uint maxQueueEntries)
    {
        maxQueueEntries = feature.RawName switch
        {
            TrainerFeatureCatalog.ExpandProductionQueueRawName => ExpandedProductionQueueLimit,
            TrainerFeatureCatalog.RestoreProductionQueueRawName => DefaultProductionQueueLimit,
            _ => 0
        };

        return maxQueueEntries != 0;
    }

    private static bool IsTeleportSelectedUnitsToMouse(TrainerFeature feature) =>
        feature.RawName.Equals(
            TrainerFeatureCatalog.TeleportSelectedUnitsToMouseRawName,
            StringComparison.Ordinal);

    private GameApiDispatchStatus ExecuteNativeAction(
        TrainerFeature feature,
        ReinforcementSettings? reinforcementSettings,
        TimeSpan timeout)
    {
        if (TryGetProductionQueueLimit(feature, out var productionQueueLimit))
        {
            return ExpandProductionQueue(productionQueueLimit, timeout);
        }

        if (IsTeleportSelectedUnitsToMouse(feature))
        {
            return TeleportSelectedUnitsToMouse(timeout);
        }

        if (TryGetSelectedUnitHealthMode(feature, out var healthMode))
        {
            return SetSelectedUnitHealth(healthMode, 0f, 0f, timeout);
        }

        var settings = reinforcementSettings ?? _reinforcementSettings;

        // Catalog-based dispatch: NativeAction features route by ActionId
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior(feature.RawName);
        if (behavior is NativeActionFeatureBehavior action)
        {
            return DispatchNativeAction(feature, action.Binding.ActionId, settings, timeout);
        }

        // Legacy fallthrough: pulse features, FreeBuild, EnableFlags-only toggles,
        // and unknown features. Preserves exception message distinction:
        // - EnableFlags-only features with no pulse mapping → "Native pulse 路由"
        // - Truly unknown features → "Native action 路由"
        return ExecuteLegacyPulse(feature, reinforcementSettings);
    }

    private GameApiDispatchStatus DispatchNativeAction(
        TrainerFeature feature,
        uint actionId,
        ReinforcementSettings settings,
        TimeSpan timeout)
    {
        switch (actionId)
        {
            case 11: return TriggerLevelUp(timeout: timeout);
            case 13: return KillUnit(timeout);
            case 14: return CopyForMe(timeout) != 0
                    ? GameApiDispatchStatus.Completed
                    : GameApiDispatchStatus.Failed;
            case 15: return GetMeBase(timeout);
            case 16: return WeNeedBack(
                    settings.UnitId,
                    unchecked((uint)settings.Count),
                    unchecked((uint)settings.Rank),
                    timeout);
            case 17: return SetUnitState(0, timeout);
            case 20: return GrantPlayerTech(0x3A7E2F69, timeout);
            case 25: return GrantSecretProtocol(
                    _secretProtocolGrantSettings.PlayerTechId,
                    _secretProtocolGrantSettings.UpgradeId,
                    timeout);
            case 26: return GrantSelectedUpgrade(RequireUpgradeId(), timeout);
            case 27: return ClearPlayerTechLocks(timeout);
            case 28: return ExecuteSecretProtocolProbe(timeout);
            case 29: return ReplaceTemplateModel(
                    RequireModelReplacement().TargetUnitId,
                    RequireModelReplacement().DonorUnitId,
                    timeout);
            case 30: return ReplaceTemplateWeapon(
                    RequireWeaponReplacement().TargetUnitId,
                    RequireWeaponReplacement().DonorUnitId,
                    timeout);
            case 32: // SetSelectedUnitTargetHealth
                if (_targetHealth > 0)
                    return SetSelectedUnitHealth(HealthModeExplicit, _targetHealth, _targetMaxHealth, timeout);
                throw new InvalidOperationException("请先输入有效的目标生命值。");
            case 39: return DispatchNativeSpeedAction(feature, timeout);
            case 40: return CaptureSelectedUnits(timeout);
            case 41: return DispatchNativeAmmoAction(feature, timeout);
            case 42: return ToggleSelectedAttackSpeed(timeout);
            case 43: return ToggleSelectedAttackRange(timeout);
            case 44: return ClearSelectedAttackSpeedEffects(timeout);
            case 45: return ClearSelectedAttackRangeEffects(timeout);
            default:
                throw new InvalidOperationException($"未知的 NativeAction ID: {actionId}。");
        }
    }

    private GameApiDispatchStatus DispatchNativeSpeedAction(TrainerFeature feature, TimeSpan timeout)
    {
        var mode = feature.RawName switch
        {
            "Select Unit Super Speed" => 1u,
            "Select Unit Slow Speed" => 2u,
            "Select Unit Freeze" => 3u,
            "Restore Select Unit Speed" => 4u,
            _ => throw new InvalidOperationException($"未知的速度模式: {feature.RawName}。")
        };
        return SetSelectedUnitSpeed(mode, timeout);
    }

    private GameApiDispatchStatus DispatchNativeAmmoAction(TrainerFeature feature, TimeSpan timeout)
    {
        var ammo = feature.RawName switch
        {
            "Fill Selected Unit Ammo" => 0x7FFFFFFFu,
            "Reset Selected Unit Ammo" => 1u,
            _ => throw new InvalidOperationException($"未知的弹药动作: {feature.RawName}。")
        };
        return SetSelectedUnitAmmo(ammo, timeout);
    }

    private GameApiDispatchStatus ExecuteLegacyPulse(
        TrainerFeature feature,
        ReinforcementSettings? reinforcementSettings)
    {
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior(feature.RawName);

        // Pulse features: write state from catalog binding
        if (behavior is NativePulseFeatureBehavior pulse)
        {
            WriteNativeFeatureState((NativeFeatureStateId)pulse.Binding.StateId, pulse.Binding.DefaultValue);
            return GameApiDispatchStatus.Completed;
        }

        // Free Build: NativeToggle that acts as pulse when TriggerAction is called
        if (behavior is NativeToggleFeatureBehavior toggle && feature.RawName == TrainerFeatureIds.FreeBuild)
        {
            WriteNativeFeatureState((NativeFeatureStateId)toggle.Binding.StateId!.Value, 1);
            return GameApiDispatchStatus.Completed;
        }

        // Preserve: EnableFlags-only features with no pulse mapping → throw "pulse 路由"
        if (feature.DispatchTarget is null && feature.EnableFlags.Count > 0)
        {
            throw new InvalidOperationException($"{feature.DisplayName} 尚未配置 Native pulse 路由。");
        }

        // Preserve: truly unknown features → throw "action 路由"
        throw new InvalidOperationException($"{feature.DisplayName} 尚未配置 Native action 路由。");
    }

    private static NativeFeatureStateId GetNativeFeatureStateId(TrainerFeature feature)
    {
        if (TryGetNativeFeatureStateId(feature, out var stateId))
        {
            return stateId;
        }

        throw new InvalidOperationException($"{feature.DisplayName} 尚未配置 Native toggle 状态。 ");
    }

    private static bool TryGetNativeFeatureStateId(
        TrainerFeature feature,
        out NativeFeatureStateId stateId)
    {
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior(feature.RawName);
        if (behavior?.AsNativeToggle() is { } toggle && toggle.StateId.HasValue)
        {
            stateId = (NativeFeatureStateId)toggle.StateId.Value;
            return true;
        }

        if (behavior?.AsNativePulse() is { } pulse)
        {
            stateId = (NativeFeatureStateId)pulse.StateId;
            return true;
        }

        stateId = default;
        return false;
    }

    private static bool IsPulseFeature(NativeFeatureStateId stateId) =>
        PulseStateIds.Contains(stateId);

    private void WriteNativeFeatureState(NativeFeatureStateId stateId, uint value)
    {
        WriteNativeFeatureStates([(stateId, value)]);
    }

    // L4: wire to cmd 5 SetFeatureStates
    private void WriteNativeFeatureStates(
        IReadOnlyList<(NativeFeatureStateId StateId, uint Value)> states)
    {
        var wireStates = states
            .Select(s => ((uint)s.StateId, s.Value))
            .ToArray();
        var request = new SetFeatureStatesRequest(wireStates);
        var result = _client.SetFeatureStatesAsync(
                _processId,
                request,
                DefaultCommandTimeout,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (result.StatusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException(
                $"Agent SetFeatureStates failed: {result.StatusCode}.");
        }

        // Update local cache on success
        foreach (var (stateId, value) in states)
        {
            _nativeFeatureStates[stateId] = value;
        }
    }

    private void ApplyRuntimePatchSetForToggle(TrainerFeature feature, bool enabled)
    {
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior(feature.RawName);
        if (behavior?.AsNativeToggle() is { } toggle && toggle.PatchSetId.HasValue)
        {
            var patchSetId = toggle.PatchSetId.Value;
            var result = _client.SetRuntimePatchSetAsync(
                    _processId,
                    patchSetId,
                    enabled,
                    DefaultCommandTimeout,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (result.StatusCode != AgentStatusCode.Ok && result.StatusCode != AgentStatusCode.Pending)
            {
                throw new InvalidOperationException(
                    $"SetRuntimePatchSet({patchSetId}, {enabled}) failed: {result.StatusCode}.");
            }
        }
        // Features without PatchSetId binding: no-op (toggle is just state-write, no runtime patch)
    }

    // L4: one-shot batch refresh from DLL
    // Note: this is sync in practice (blocking on GetAwaiter) to match existing pattern.
    public async Task<FeatureStatesResponse> RefreshRuntimeStateAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetFeatureStatesAsync(
                _processId,
                DefaultCommandTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        _lastObserved = response;

        // Update local cache from response
        foreach (var entry in response.Entries)
        {
            _nativeFeatureStates[(NativeFeatureStateId)entry.StateId] = entry.Value;
        }

        return response;
    }

    // L4: nullable toggle state — null if no value has ever been written or refreshed
    public bool? ReadToggleState(TrainerFeature feature)
    {
        // Pulse features must use ReadPulseFired regardless of toggle mapping
        if (IsPulseFeature(feature))
        {
            throw new InvalidOperationException(
                $"Pulse features must use ReadPulseFired: {feature.DisplayName}.");
        }

        if (!TryGetNativeFeatureStateId(feature, out var stateId))
        {
            return null;
        }

        // Return null if we have never written or refreshed this state
        if (!_nativeFeatureStates.TryGetValue(stateId, out var value))
        {
            return null;
        }

        return value != 0;
    }

    // L4: read pulse sticky bit — null if no value has ever been written or refreshed
    public bool? ReadPulseFired(TrainerFeature feature)
    {
        if (!TryGetNativeFeatureStateId(feature, out var stateId))
        {
            return null;
        }

        if (!IsPulseFeature(stateId))
        {
            return null;
        }

        if (!_nativeFeatureStates.TryGetValue(stateId, out var value))
        {
            return null;
        }

        return value != 0;
    }

    public bool IsPulseFeature(TrainerFeature feature)
    {
        return TryGetNativeFeatureStateId(feature, out var stateId) && PulseStateIds.Contains(stateId);
    }

    private uint RequireUpgradeId()
    {
        if (_secretProtocolGrantSettings.UpgradeId == 0)
        {
            throw new InvalidOperationException("尚未选择要授予的 Upgrade。 ");
        }

        return _secretProtocolGrantSettings.UpgradeId;
    }

    private TemplateModelReplacementSettings RequireModelReplacement() =>
        _templateModelReplacementSettings ??
        throw new InvalidOperationException("尚未配置模型替换的目标与来源单位。 ");

    private TemplateWeaponReplacementSettings RequireWeaponReplacement() =>
        _templateWeaponReplacementSettings ??
        throw new InvalidOperationException("尚未配置武器替换的目标与来源单位。 ");

    private static GameApiDispatchStatus ToDispatchStatus(uint status) =>
        status == (uint)SecretProtocolBindingProbeStatus.Completed
            ? GameApiDispatchStatus.Completed
            : GameApiDispatchStatus.Failed;

    private GameApiDispatchStatus ExecuteSecretProtocolProbe(TimeSpan timeout)
    {
        _lastProbeStatus = (SecretProtocolBindingProbeStatus)SecretProtocolBindingProbe(timeout);
        return ToDispatchStatus((uint)_lastProbeStatus);
    }

    private static ActionDispatchResult ToActionDispatchResult(GameApiDispatchStatus dispatchStatus)
    {
        return dispatchStatus == GameApiDispatchStatus.Completed
            ? ActionDispatchResult.Consumed
            : ActionDispatchResult.TimedOut;
    }

    private static void EnsureOk(AgentStatusCode statusCode, AgentCommand command)
    {
        if (statusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException($"Agent {command} failed: {statusCode}.");
        }
    }

}
