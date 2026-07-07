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
        ApplyToggleBytePatches(feature, enabled);
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
            ApplyToggleBytePatches(feature, enabled: false);
        }
    }

    public bool ReadToggleState(TrainerFeature feature)
    {
        return TryGetNativeFeatureStateId(feature, out var stateId) &&
            _nativeFeatureStates.TryGetValue(stateId, out var value) &&
            value != 0;
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
        return feature.RawName switch
        {
            "Select Unit Level UP" => TriggerLevelUp(timeout: timeout),
            "Select Unit Super Speed" => SetSelectedUnitSpeed(1, timeout),
            "Select Unit Slow Speed" => SetSelectedUnitSpeed(2, timeout),
            "Select Unit Freeze" => SetSelectedUnitSpeed(3, timeout),
            "Restore Select Unit Speed" => SetSelectedUnitSpeed(4, timeout),
            "Select Unit Change ID" => CaptureSelectedUnits(timeout),
            "Destory Select Unit" => KillUnit(timeout),
            TrainerFeatureIds.GetBase => GetMeBase(timeout),
            TrainerFeatureIds.Reinforcement => WeNeedBack(
                settings.UnitId,
                unchecked((uint)settings.Count),
                unchecked((uint)settings.Rank),
                timeout),
            TrainerFeatureIds.CopySelectedUnit => CopyForMe(timeout) != 0
                ? GameApiDispatchStatus.Completed
                : GameApiDispatchStatus.Failed,
            "Set Unit Support State" => SetUnitState(0, timeout),
            TrainerFeatureIds.SecretProtocolBindingProbe => ExecuteSecretProtocolProbe(timeout),
            "Soviet Orbital Refuse Rank 1 Probe" => GrantPlayerTech(0x3A7E2F69, timeout),
            TrainerFeatureIds.GrantSecretProtocol => GrantSecretProtocol(
                _secretProtocolGrantSettings.PlayerTechId,
                _secretProtocolGrantSettings.UpgradeId,
                timeout),
            TrainerFeatureIds.GrantSelectedObjectUpgrade => GrantSelectedUpgrade(
                RequireUpgradeId(),
                timeout),
            "Clear Player Tech Locks" => ClearPlayerTechLocks(timeout),
            TrainerFeatureIds.ReplaceTemplateModel => ReplaceTemplateModel(
                RequireModelReplacement().TargetUnitId,
                RequireModelReplacement().DonorUnitId,
                timeout),
            TrainerFeatureIds.ReplaceTemplateWeapon => ReplaceTemplateWeapon(
                RequireWeaponReplacement().TargetUnitId,
                RequireWeaponReplacement().DonorUnitId,
                timeout),
            TrainerFeatureIds.SetSelectedUnitTargetHealth when _targetHealth > 0 =>
                SetSelectedUnitHealth(HealthModeExplicit, _targetHealth, _targetMaxHealth, timeout),
            TrainerFeatureIds.SetSelectedUnitTargetHealth => throw new InvalidOperationException(
                "请先输入有效的目标生命值。"),
            "Fill Selected Unit Ammo" => SetSelectedUnitAmmo(0x7FFFFFFF, timeout),
            "Reset Selected Unit Ammo" => SetSelectedUnitAmmo(1, timeout),
            "Toggle Selected Unit Attack Speed" => ToggleSelectedAttackSpeed(timeout),
            TrainerFeatureIds.Money or
            "Challenge Money" or
            "Danger Level MAX" or
            "Danger Level MIN" or
            "Restore Danger Level Normal" or
            "Restore Select Ore Mine" or
            "Free Build" => ExecuteLegacyPulse(feature, reinforcementSettings),
            _ when feature.DispatchTarget is null && feature.EnableFlags.Count > 0 =>
                ExecuteLegacyPulse(feature, reinforcementSettings),
            _ => throw new InvalidOperationException($"{feature.DisplayName} 尚未配置 Native action 路由。")
        };
    }

    private GameApiDispatchStatus ExecuteLegacyPulse(
        TrainerFeature feature,
        ReinforcementSettings? reinforcementSettings)
    {
        var (stateId, value) = feature.RawName switch
        {
            TrainerFeatureIds.Money => (NativeFeatureStateId.MoneyPulse, 1u),
            "Challenge Money" => (NativeFeatureStateId.ChallengeMoneyPulse, 1u),
            "Danger Level MAX" => (NativeFeatureStateId.DangerLevelMode, 1u),
            "Danger Level MIN" => (NativeFeatureStateId.DangerLevelMode, 2u),
            "Restore Danger Level Normal" => (NativeFeatureStateId.DangerLevelMode, 0u),
            "Restore Select Ore Mine" => (NativeFeatureStateId.RestoreOrePulse, 1u),
            "Free Build" => (NativeFeatureStateId.FreeBuild, 1u),
            _ => throw new InvalidOperationException($"{feature.DisplayName} 尚未配置 Native pulse 路由。")
        };
        WriteNativeFeatureState(stateId, value);
        return GameApiDispatchStatus.Completed;
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
        stateId = feature.RawName switch
        {
            TrainerFeatureIds.Power => NativeFeatureStateId.Power,
            TrainerFeatureIds.SecretProtocolPoints => NativeFeatureStateId.SecretProtocolPoints,
            "HAVE ALL SC" => NativeFeatureStateId.AllSecretProtocols,
            "FAST BUILD" => NativeFeatureStateId.FastBuild,
            TrainerFeatureIds.SuperPower => NativeFeatureStateId.SuperPower,
            TrainerFeatureIds.DisableAllSecretProtocols => NativeFeatureStateId.DisableAllSuperPowers,
            "Zoom" => NativeFeatureStateId.Zoom,
            "MAP" => NativeFeatureStateId.RevealMap,
            "Enemy Can't Build" => NativeFeatureStateId.EnemyCannotBuild,
            "Player God Mode" => NativeFeatureStateId.GodMode,
            "Player One Kill Mode" => NativeFeatureStateId.OneHitKill,
            "Challenge Time" => NativeFeatureStateId.ChallengeTime,
            "Free Build" => NativeFeatureStateId.FreeBuild,
            TrainerFeatureIds.SecretProtocolDependencyBypass => NativeFeatureStateId.SecretProtocolDependencyBypass,
            "Ignore Prerequisites" => NativeFeatureStateId.IgnorePrerequisites,
            "Ignore Quantity Limit" => NativeFeatureStateId.IgnoreQuantityLimit,
            "Run In Background" => NativeFeatureStateId.RunInBackground,
            "Frame Rate Unlock 60fps" => NativeFeatureStateId.FrameRateUnlock,
            _ => 0
        };
        return stateId != 0;
    }

    private void WriteNativeFeatureState(NativeFeatureStateId stateId, uint value)
    {
        WriteNativeFeatureStates([(stateId, value)]);
    }

    private void WriteNativeFeatureStates(
        IReadOnlyList<(NativeFeatureStateId StateId, uint Value)> states)
    {
        var request = new AgentMemoryWriteRequest(
            states.Select(state => new AgentMemoryWriteOperation(
                (uint)state.StateId,
                AgentMemoryAddressMode.Direct,
                BitConverter.GetBytes(state.Value))));
        var result = _client.SetToggleAsync(
                _processId,
                request,
                DefaultCommandTimeout,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        EnsureOk(result.StatusCode, AgentCommand.SetToggle);

        foreach (var state in states)
        {
            _nativeFeatureStates[state.StateId] = state.Value;
        }
    }

    private void ApplyToggleBytePatches(TrainerFeature feature, bool enabled)
    {
        if (feature.ToggleBytePatches is not { Count: > 0 })
        {
            return;
        }

        var writes = feature.ToggleBytePatches.Select(patch =>
        {
            var expression = patch.Address.Trim();
            var dereference = expression.StartsWith("[", StringComparison.Ordinal) &&
                expression.EndsWith("]", StringComparison.Ordinal);
            if (dereference)
            {
                expression = expression[1..^1].Trim();
            }

            var address = _moduleAddressResolver.Resolve(expression).ToInt64();
            if (address is < 0 or > uint.MaxValue)
            {
                throw new InvalidOperationException($"Patch 地址 0x{address:X} 超出 x86 地址空间。 ");
            }

            return new AgentMemoryWriteOperation(
                unchecked((uint)address),
                dereference ? AgentMemoryAddressMode.DereferenceUInt32 : AgentMemoryAddressMode.Direct,
                (enabled ? patch.EnabledBytes : patch.DisabledBytes).ToArray());
        });

        var result = _client.TriggerActionAsync(
                _processId,
                new AgentMemoryWriteRequest(writes),
                DefaultCommandTimeout,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        EnsureOk(result.StatusCode, AgentCommand.TriggerAction);
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
