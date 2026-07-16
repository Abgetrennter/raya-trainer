using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Agent;

namespace RayaTrainer.Core.Features;

public interface ITrainerFeatureController
{
    void SetToggle(TrainerFeature feature, bool enabled);

    void TriggerAction(TrainerFeature feature);

    void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings);

    Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
        TrainerFeature feature,
        ReinforcementSettings? reinforcementSettings = null,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        Action? onDispatched = null,
        CancellationToken cancellationToken = default,
        TimeSpan? pausedGracePeriod = null,
        Action<DispatchWaitStatus>? onWaitStatusChanged = null);

    void WriteReinforcementSettings(ReinforcementSettings settings);

    void WriteResourceValues(ResourceValueSettings settings);

    void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings);

    void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings);

    void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings);

    SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult();

    void PulseAutoRepair();

    void ClearAutoRepairPulse();

    void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f);

    uint ReadSelectedUnitCode();

    /// <summary>
    /// Reads the available OBJECT-type upgrade hashes from the first selected
    /// unit's modules through the game-thread dispatcher. Returns up to 20
    /// unique hashes plus the unit type id. Empty when no unit is selected.
    /// </summary>
    SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades();

    /// <summary>
    /// Grants an OBJECT-type upgrade to selected units of the same type as the
    /// first selected unit (ThingTemplate* comparison on GameObject+0x4).
    /// Returns the dispatch status reported by the game thread.
    /// </summary>
    GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null);

    /// <summary>
    /// Reads the DLL game-thread dispatcher heartbeat counter.
    /// Monotonic while the game runs; frozen when paused (alt-tabbed).
    /// Used by the pause-aware dispatch waiter to distinguish a paused game
    /// from an action that genuinely failed to be consumed.
    /// </summary>
    uint ReadGameThreadTick();

    /// <summary>
    /// Reads the current GameMode field from the game's GameClient singleton.
    /// Returns the raw int32 value. Use <see cref="GameRuntimeConstants.GameModeShell"/>
    /// to check for menu state.
    /// </summary>
    int ReadGameMode();

    void Reset(TrainerFeature feature);

    bool ReadToggleState(TrainerFeature feature);
}
