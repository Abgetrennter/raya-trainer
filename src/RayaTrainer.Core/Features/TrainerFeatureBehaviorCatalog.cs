using System.Collections.Frozen;

namespace RayaTrainer.Core.Features;

public enum TrainerFeatureBehaviorKind
{
    /// <summary>Stable on/off toggle backed by a native state slot. Optionally also
    /// tied to a Runtime PatchSet and/or installed Hook (composite readiness).</summary>
    NativeToggle,

    /// <summary>Fire-and-clear trigger. C# writes value to state slot, native hook
    /// consumes next frame. UI shows transient "triggered" badge via sticky-bit readback.</summary>
    NativePulse,

    /// <summary>One-shot action dispatched via Direct GameApi (cmd 8-47).</summary>
    NativeAction,

    /// <summary>Feature exposes no native effect; capability evaluator only.
    /// E.g. detection-only features, UI placeholders.</summary>
    CapabilityOnly,
}

public readonly record struct NativeToggleBehavior(
    uint? StateId,
    uint? PatchSetId,
    uint? HookId);

public readonly record struct NativePulseBehavior(
    uint StateId,
    uint DefaultValue);

public readonly record struct NativeActionBehavior(
    uint ActionId); // Direct GameApi command number (8-47)

public readonly record struct CapabilityOnlyBehavior(
    IReadOnlyList<string> RequiredProfileIds,
    bool RequiresExactProfile);

public abstract record TrainerFeatureBehavior(string RawName, TrainerFeatureBehaviorKind Kind)
{
    public abstract NativeToggleBehavior? AsNativeToggle();
    public abstract NativePulseBehavior? AsNativePulse();
    public abstract NativeActionBehavior? AsNativeAction();
    public abstract CapabilityOnlyBehavior? AsCapabilityOnly();
}

public sealed record NativeToggleFeatureBehavior(
    string RawName,
    NativeToggleBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.NativeToggle)
{
    public override NativeToggleBehavior? AsNativeToggle() => Binding;
    public override NativePulseBehavior? AsNativePulse() => null;
    public override NativeActionBehavior? AsNativeAction() => null;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => null;
}

public sealed record NativePulseFeatureBehavior(
    string RawName,
    NativePulseBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.NativePulse)
{
    public override NativeToggleBehavior? AsNativeToggle() => null;
    public override NativePulseBehavior? AsNativePulse() => Binding;
    public override NativeActionBehavior? AsNativeAction() => null;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => null;
}

public sealed record NativeActionFeatureBehavior(
    string RawName,
    NativeActionBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.NativeAction)
{
    public override NativeToggleBehavior? AsNativeToggle() => null;
    public override NativePulseBehavior? AsNativePulse() => null;
    public override NativeActionBehavior? AsNativeAction() => Binding;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => null;
}

public sealed record CapabilityOnlyFeatureBehavior(
    string RawName,
    CapabilityOnlyBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.CapabilityOnly)
{
    public override NativeToggleBehavior? AsNativeToggle() => null;
    public override NativePulseBehavior? AsNativePulse() => null;
    public override NativeActionBehavior? AsNativeAction() => null;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => Binding;
}

public static class TrainerFeatureBehaviorCatalog
{
    private static readonly FrozenDictionary<string, TrainerFeatureBehavior> _byRawName = BuildCatalog();

    public static TrainerFeatureBehavior? TryGetBehavior(string rawName) =>
        _byRawName.TryGetValue(rawName, out var b) ? b : null;

    public static IReadOnlyCollection<TrainerFeatureBehavior> All => _byRawName.Values;

    public static IReadOnlyDictionary<string, TrainerFeatureBehavior> AllByRawName => _byRawName;

    private static FrozenDictionary<string, TrainerFeatureBehavior> BuildCatalog()
    {
        var entries = new List<TrainerFeatureBehavior>(72)
        {
            // ═══════════════════════════════════════════════════════════════
            // NativeToggle — persistent on/off state backed by native slot
            // ═══════════════════════════════════════════════════════════════
            NativeToggle(TrainerFeatureIds.Power,                       stateId: 2),
            NativeToggle(TrainerFeatureIds.SecretProtocolPoints,         stateId: 3),
            NativeToggle(TrainerFeatureIds.HaveAllSc,                    stateId: 4),
            NativeToggle(TrainerFeatureIds.FastBuild,                    stateId: 5),
            NativeToggle(TrainerFeatureIds.SuperPower,                   stateId: 6),
            NativeToggle(TrainerFeatureIds.DisableAllSecretProtocols,    stateId: 7),
            NativeToggle(TrainerFeatureIds.Zoom,                         stateId: 8),
            NativeToggle(TrainerFeatureIds.Map,                          stateId: 9),
            NativeToggle(TrainerFeatureIds.EnemyCantBuild,               stateId: 10),
            NativeToggle(TrainerFeatureIds.PlayerGodMode,                stateId: 11),
            NativeToggle(TrainerFeatureIds.PlayerOneKillMode,            stateId: 12),
            NativeToggle(TrainerFeatureIds.ChallengeTime,                stateId: 14),
            NativeToggle(TrainerFeatureIds.FreeBuild,                    stateId: 15),
            NativeToggle(TrainerFeatureIds.SecretProtocolDependencyBypass, stateId: 16),
            NativeToggle(TrainerFeatureIds.IgnorePrerequisites,          stateId: 17),
            NativeToggle(TrainerFeatureIds.IgnoreQuantityLimit,          stateId: 18),
            NativeToggle(TrainerFeatureIds.RunInBackground,              stateId: 19),
            NativeToggle(TrainerFeatureIds.LogicTimeFreeze,              stateId: 26),
            NativeToggle(TrainerFeatureIds.LogicTimeSlowMotion,          stateId: 25),

            // Frame Rate Unlock: composite binding (state + PatchSet + Hook)
            NativeToggle(TrainerFeatureIds.FrameRateUnlock60fps,
                stateId: 20, patchSetId: 1, hookId: 41),

            // ═══════════════════════════════════════════════════════════════
            // NativePulse — fire-and-clear trigger via legacy pulse dispatch
            // ═══════════════════════════════════════════════════════════════
            Pulse(TrainerFeatureIds.Money,                  stateId: 1,  defaultValue: 1),
            Pulse(TrainerFeatureIds.ChallengeMoney,         stateId: 13, defaultValue: 1),
            Pulse(TrainerFeatureIds.RestoreSelectOreMine,   stateId: 23, defaultValue: 1),

            // Danger Level: written via ExecuteLegacyPulse to DangerLevelMode state
            Pulse(TrainerFeatureIds.DangerLevelMax,         stateId: 22, defaultValue: 1),
            Pulse(TrainerFeatureIds.DangerLevelMin,         stateId: 22, defaultValue: 2),
            Pulse(TrainerFeatureIds.RestoreDangerLevelNormal, stateId: 22, defaultValue: 0),

            // ═══════════════════════════════════════════════════════════════
            // NativeAction — one-shot Direct GameApi commands
            // ═══════════════════════════════════════════════════════════════
            // Health (cmd 32: SetSelectedUnitHealth, mode encoded in RawName)
            Action(TrainerFeatureIds.SelectUnitHpMax,          actionId: 32),
            Action(TrainerFeatureIds.SelectUnitHpMin,          actionId: 32),
            Action(TrainerFeatureIds.RestoreSelectUnitNormalHp, actionId: 32),
            Action(TrainerFeatureIds.SetSelectedUnitTargetHealth, actionId: 32),

            // Speed (cmd 39: SetSelectedUnitSpeed)
            Action(TrainerFeatureIds.SelectUnitSuperSpeed,     actionId: 39),
            Action(TrainerFeatureIds.SelectUnitSlowSpeed,      actionId: 39),
            Action(TrainerFeatureIds.SelectUnitFreeze,         actionId: 39),
            Action(TrainerFeatureIds.RestoreSelectUnitSpeed,   actionId: 39),

            // Level / capture / kill (cmd 11, 40, 13)
            Action(TrainerFeatureIds.SelectUnitLevelUp,        actionId: 11),
            Action(TrainerFeatureIds.SelectUnitChangeId,       actionId: 40),
            Action(TrainerFeatureIds.DestorySelectUnit,        actionId: 13),

            // Support state (cmd 17)
            Action(TrainerFeatureIds.SetUnitSupportState,      actionId: 17),

            // Secret protocol / tech probes (cmd 20, 25, 26, 27, 28)
            Action(TrainerFeatureIds.SovietOrbitalRefuseRankOneProbe, actionId: 20),
            Action(TrainerFeatureIds.GrantSecretProtocol,             actionId: 25),
            Action(TrainerFeatureIds.GrantSelectedObjectUpgrade,       actionId: 26),
            Action(TrainerFeatureIds.ClearPlayerTechLocks,             actionId: 27),
            Action(TrainerFeatureIds.SecretProtocolBindingProbe,       actionId: 28),

            // Template replacement (cmd 29, 30)
            Action(TrainerFeatureIds.ReplaceTemplateModel,     actionId: 29),
            Action(TrainerFeatureIds.ReplaceTemplateWeapon,    actionId: 30),

            // Base / reinforcement / copy (cmd 15, 16, 14)
            Action(TrainerFeatureIds.GetBase,                  actionId: 15),
            Action(TrainerFeatureIds.Reinforcement,            actionId: 16),
            Action(TrainerFeatureIds.CopySelectedUnit,         actionId: 14),

            // Production queue (cmd 37)
            Action(TrainerFeatureIds.ExpandProductionQueue,    actionId: 37),
            Action(TrainerFeatureIds.RestoreProductionQueue,   actionId: 37),

            // Teleport (cmd 38)
            Action(TrainerFeatureIds.TeleportSelectedUnitsToMouse, actionId: 38),

            // Ammo (cmd 41)
            Action(TrainerFeatureIds.FillSelectedUnitAmmo,     actionId: 41),
            Action(TrainerFeatureIds.ResetSelectedUnitAmmo,    actionId: 41),

            // Attack speed / range toggles (cmd 42, 43)
            Action(TrainerFeatureIds.ToggleSelectedUnitAttackSpeed,  actionId: 42),
            Action(TrainerFeatureIds.ToggleSelectedUnitAttackRange,  actionId: 43),

            // Clear effects (cmd 44, 45)
            Action(TrainerFeatureIds.ClearSelectedAttackSpeedEffects,  actionId: 44),
            Action(TrainerFeatureIds.ClearSelectedAttackRangeEffects,  actionId: 45),

            // ═══════════════════════════════════════════════════════════════
            // CapabilityOnly — no native dispatch; used for UI actions /
            // capability gating only.
            // ═══════════════════════════════════════════════════════════════
            new CapabilityOnlyFeatureBehavior(
                TrainerFeatureIds.DetectProcess,
                new CapabilityOnlyBehavior([], false)),
            new CapabilityOnlyFeatureBehavior(
                TrainerFeatureIds.LaunchAndLoad,
                new CapabilityOnlyBehavior([], false)),
            new CapabilityOnlyFeatureBehavior(
                TrainerFeatureIds.ExecuteReinforcementQueue,
                new CapabilityOnlyBehavior([], false)),
            new CapabilityOnlyFeatureBehavior(
                TrainerFeatureIds.ReadSelectedUnitCode,
                new CapabilityOnlyBehavior([], false)),
            new CapabilityOnlyFeatureBehavior(
                TrainerFeatureIds.SelectedUnitObjectUpgrade,
                new CapabilityOnlyBehavior(["ra3_1.12"], true)),
        };

        var dict = new Dictionary<string, TrainerFeatureBehavior>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!dict.TryAdd(entry.RawName, entry))
            {
                throw new InvalidOperationException(
                    $"Duplicate behavior catalog entry: '{entry.RawName}'.");
            }
        }
        return dict.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // ── Builder helpers ──────────────────────────────────────────────────────

    private static TrainerFeatureBehavior NativeToggle(string rawName, uint stateId, uint? patchSetId = null, uint? hookId = null) =>
        new NativeToggleFeatureBehavior(rawName, new NativeToggleBehavior(stateId, patchSetId, hookId));

    private static TrainerFeatureBehavior Pulse(string rawName, uint stateId, uint defaultValue) =>
        new NativePulseFeatureBehavior(rawName, new NativePulseBehavior(stateId, defaultValue));

    private static TrainerFeatureBehavior Action(string rawName, uint actionId) =>
        new NativeActionFeatureBehavior(rawName, new NativeActionBehavior(actionId));
}
