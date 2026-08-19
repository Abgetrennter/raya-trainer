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

    /// <summary>One-shot Product Intent submission over the product control session.
    /// The App builds the intent from the bound ProductId (plus typed parameters for
    /// custom-multiplier products) and polls the layered result until settled.</summary>
    ProductIntent,
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

public readonly record struct ProductIntentBehavior(
    string ProductId);

public abstract record TrainerFeatureBehavior(string RawName, TrainerFeatureBehaviorKind Kind)
{
    public abstract NativeToggleBehavior? AsNativeToggle();
    public abstract NativePulseBehavior? AsNativePulse();
    public abstract NativeActionBehavior? AsNativeAction();
    public abstract CapabilityOnlyBehavior? AsCapabilityOnly();
    public abstract ProductIntentBehavior? AsProductIntent();
}

public sealed record NativeToggleFeatureBehavior(
    string RawName,
    NativeToggleBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.NativeToggle)
{
    public override NativeToggleBehavior? AsNativeToggle() => Binding;
    public override NativePulseBehavior? AsNativePulse() => null;
    public override NativeActionBehavior? AsNativeAction() => null;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => null;
    public override ProductIntentBehavior? AsProductIntent() => null;
}

public sealed record NativePulseFeatureBehavior(
    string RawName,
    NativePulseBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.NativePulse)
{
    public override NativeToggleBehavior? AsNativeToggle() => null;
    public override NativePulseBehavior? AsNativePulse() => Binding;
    public override NativeActionBehavior? AsNativeAction() => null;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => null;
    public override ProductIntentBehavior? AsProductIntent() => null;
}

public sealed record NativeActionFeatureBehavior(
    string RawName,
    NativeActionBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.NativeAction)
{
    public override NativeToggleBehavior? AsNativeToggle() => null;
    public override NativePulseBehavior? AsNativePulse() => null;
    public override NativeActionBehavior? AsNativeAction() => Binding;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => null;
    public override ProductIntentBehavior? AsProductIntent() => null;
}

public sealed record CapabilityOnlyFeatureBehavior(
    string RawName,
    CapabilityOnlyBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.CapabilityOnly)
{
    public override NativeToggleBehavior? AsNativeToggle() => null;
    public override NativePulseBehavior? AsNativePulse() => null;
    public override NativeActionBehavior? AsNativeAction() => null;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => Binding;
    public override ProductIntentBehavior? AsProductIntent() => null;
}

public sealed record ProductIntentFeatureBehavior(
    string RawName,
    ProductIntentBehavior Binding) : TrainerFeatureBehavior(RawName, TrainerFeatureBehaviorKind.ProductIntent)
{
    public override NativeToggleBehavior? AsNativeToggle() => null;
    public override NativePulseBehavior? AsNativePulse() => null;
    public override NativeActionBehavior? AsNativeAction() => null;
    public override CapabilityOnlyBehavior? AsCapabilityOnly() => null;
    public override ProductIntentBehavior? AsProductIntent() => Binding;
}

public static class TrainerFeatureBehaviorCatalog
{
    private static readonly FrozenDictionary<string, TrainerFeatureBehavior> _byRawName = BuildCatalog();

    public static TrainerFeatureBehavior? TryGetBehavior(string rawName) =>
        _byRawName.TryGetValue(rawName, out var b) ? b : null;

    public static IReadOnlyCollection<TrainerFeatureBehavior> All => _byRawName.Values;

    public static IReadOnlyDictionary<string, TrainerFeatureBehavior> AllByRawName => _byRawName;

    /// <summary>
    /// Distinct native Hook IDs declared as dependencies by the App's own feature grid. The capability
    /// policy uses these to gate hook-backed features once the Agent reports its core hooks installed:
    /// the Agent installs its full resolved plan transactionally, so the App trusts its own declared
    /// dependencies rather than App-side signature scanning or profile Native refs (plan §6).
    /// </summary>
    public static IReadOnlyCollection<uint> DeclaredHookIds { get; } =
        _byRawName.Values
            .Select(b => b.AsNativeToggle())
            .Where(t => t is { HookId: not null })
            .Select(t => t!.Value.HookId!.Value)
            .Distinct()
            .ToArray();

    /// <summary>Distinct runtime PatchSet IDs declared as dependencies by the App's feature grid.</summary>
    public static IReadOnlyCollection<uint> DeclaredPatchSetIds { get; } =
        _byRawName.Values
            .Select(b => b.AsNativeToggle())
            .Where(t => t is { PatchSetId: not null })
            .Select(t => t!.Value.PatchSetId!.Value)
            .Distinct()
            .ToArray();

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
            NativeToggle(TrainerFeatureIds.FastBuild,                    stateId: 5, hookId: 30),
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

            // Speed (cmd 39: SetSelectedUnitSpeed) 已从协议移除：四模式改由 selected-unit.speed.*
            // 产品经 AttributeModifier 路由执行，旧 feature 入口已隐藏。

            // Level (cmd 11) / capture / destroy: capture and destroy now route through the
            // selected-unit.capture / selected-unit.destroy Product Intents (Captured bindings).
            Action(TrainerFeatureIds.SelectUnitLevelUp,        actionId: 11),
            ProductIntent(TrainerFeatureIds.SelectUnitChangeId, "selected-unit.capture"),
            ProductIntent(TrainerFeatureIds.DestorySelectUnit,  "selected-unit.destroy"),

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

            // Attack speed/range toggles and clears (cmd 42-45) 已从协议移除；
            // 正式入口为 AttributeModifier 产品。

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

            // ═════════════════════════════════════════════════════════════════
            // ProductIntent — unified attribute-modification entries. The App
            // submits the bound ProductId over the product control session; the
            // custom-multiplier entries additionally carry the shared integer
            // multiplier input as the single typed parameter.
            // ═════════════════════════════════════════════════════════════════
            ProductIntent(TrainerFeatureIds.ProductSpeedFast,           "selected-unit.speed.fast"),
            ProductIntent(TrainerFeatureIds.ProductSpeedSlow,           "selected-unit.speed.slow"),
            ProductIntent(TrainerFeatureIds.ProductSpeedFreeze,         "selected-unit.speed.freeze"),
            ProductIntent(TrainerFeatureIds.ProductSpeedRestore,        "selected-unit.speed.restore"),
            ProductIntent(TrainerFeatureIds.ProductAttackSpeedEnable,   "selected-unit.attack-speed.enable"),
            ProductIntent(TrainerFeatureIds.ProductAttackSpeedDisable,  "selected-unit.attack-speed.disable"),
            ProductIntent(TrainerFeatureIds.ProductAttackSpeedCustom,   "selected-unit.attack-speed.custom"),
            ProductIntent(TrainerFeatureIds.ProductAttackRangeEnable,   "selected-unit.attack-range.enable"),
            ProductIntent(TrainerFeatureIds.ProductAttackRangeDisable,  "selected-unit.attack-range.disable"),
            ProductIntent(TrainerFeatureIds.ProductAttackRangeCustom,   "selected-unit.attack-range.custom"),
            ProductIntent(TrainerFeatureIds.ProductAttackDamageCustom,  "selected-unit.attack-damage.custom"),
            ProductIntent(TrainerFeatureIds.ProductMaxHealthCustom,     "selected-unit.max-health.custom"),
            ProductIntent(TrainerFeatureIds.ProductMoveSpeedCustom,     "selected-unit.move-speed.custom"),
            ProductIntent(TrainerFeatureIds.ProductClearAttackSpeed,    "effects.clear-attack-speed"),
            ProductIntent(TrainerFeatureIds.ProductClearAttackRange,    "effects.clear-attack-range"),
            // 战场支援：精兵学院（生产升星）/ 车库（治疗光环）/ 将军刽子手生成 / 矿脉生成。
            ProductIntent(TrainerFeatureIds.ProductVeterancyGrant,     "player.production-veterancy.grant"),
            ProductIntent(TrainerFeatureIds.ProductHealingAuraEnable,  "player.healing-aura.enable"),
            ProductIntent(TrainerFeatureIds.ProductHealingAuraDisable, "player.healing-aura.disable"),
            ProductIntent(TrainerFeatureIds.ProductSpawnMechaKing,     "spawn.mecha-king"),
            ProductIntent(TrainerFeatureIds.ProductSpawnOreNode,       "spawn.ore-node"),
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

    private static TrainerFeatureBehavior ProductIntent(string rawName, string productId) =>
        new ProductIntentFeatureBehavior(rawName, new ProductIntentBehavior(productId));
}
