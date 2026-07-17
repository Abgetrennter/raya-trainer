using System.Collections.Generic;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Features;

/// <summary>
/// Result of a capability policy evaluation — the final state and reason for a feature.
/// </summary>
public readonly record struct FeatureCapabilityEvaluation(
    FeatureCapabilityState State,
    string ReasonCode,
    string? Reason = null);

/// <summary>
/// Context available to <see cref="TrainerFeatureCapabilityPolicy.Evaluate"/>.
/// Aggregates agent connection state, profile, hook/patchset registrations, and the
/// base snapshot produced by <see cref="TrainerFeatureCapabilityEvaluator"/>.
/// </summary>
public interface ITrainerFeatureCapabilityContext
{
    /// <summary>Whether the injected DLL agent is currently connected.</summary>
    bool IsAgentConnected { get; }

    /// <summary>Resolved version profile for the current target, if any.</summary>
    Ra3VersionProfile? CurrentProfile { get; }

    /// <summary>Native hook IDs successfully included in the last install.</summary>
    IReadOnlyCollection<uint> InstalledNativeHookIds { get; }

    /// <summary>PatchSet IDs registered in the last install.</summary>
    IReadOnlyCollection<uint> RegisteredPatchSetIds { get; }

    /// <summary>Whether the native address catalog was delivered to the agent.</summary>
    bool IsNativeCatalogDelivered { get; }

    /// <summary>Base capability snapshot from the existing evaluator.</summary>
    FeatureCapabilitySnapshot BaseSnapshot { get; }
}

/// <summary>
/// Unified policy that evaluates behavior-specific capability gates on top of the base
/// <see cref="TrainerFeatureCapabilityEvaluator"/> snapshot. Replaces inline RawName-based
/// special cases that were previously in <c>TrainerSessionManager.GetFeatureCapability</c>.
/// </summary>
public sealed class TrainerFeatureCapabilityPolicy
{
    /// <summary>
    /// Evaluates behavior-specific capability gates for the given feature.
    /// Start from <paramref name="context"/>.<see cref="ITrainerFeatureCapabilityContext.BaseSnapshot"/>
    /// (produced by the existing evaluator) and overlay policy rules:
    /// <list type="bullet">
    ///   <item>Composite NativeToggle requires both HookId and PatchSetId to be present in the agent.</item>
    ///   <item>CapabilityOnly features validate RequiredProfileIds and RequiresExactProfile.</item>
    ///   <item>Transitional P1 RawName special case for SelectedUnitObjectUpgrade (profile + native layout).</item>
    /// </list>
    /// </summary>
    public FeatureCapabilityEvaluation Evaluate(
        TrainerFeature feature,
        ITrainerFeatureCapabilityContext context)
    {
        var baseSnapshot = context.BaseSnapshot;

        // Pass through non-Ready states — the base evaluator already handled them,
        // and no behavior-specific gate should override a Waiting/Unavailable decision.
        if (baseSnapshot.State != FeatureCapabilityState.Ready)
        {
            return new(baseSnapshot.State, baseSnapshot.ReasonCode, baseSnapshot.Reason);
        }

        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior(feature.RawName);
        if (behavior is null)
        {
            return new(baseSnapshot.State, baseSnapshot.ReasonCode, baseSnapshot.Reason);
        }

        // ── Generic: composite NativeToggle gate ────────────────────────────
        // Any NativeToggle with both PatchSetId and HookId non-null requires those
        // to be present in the installed agent before the feature is Ready.
        if (behavior.AsNativeToggle() is { } toggle &&
            toggle.PatchSetId.HasValue &&
            toggle.HookId.HasValue)
        {
            if (!context.InstalledNativeHookIds.Contains(toggle.HookId.Value))
            {
                return new(
                    FeatureCapabilityState.Unavailable,
                    "FRAMERATE_COMPOSITE_INCOMPLETE",
                    "60fps 解锁需要完整的三重绑定依赖：Hook 41（帧率解锁游戏更新）、" +
                    "PatchSet 1（运行时字节补丁）和 State 20（帧率状态同步）。" +
                    " Hook 41 尚未安装。");
            }

            if (!context.RegisteredPatchSetIds.Contains(toggle.PatchSetId.Value))
            {
                return new(
                    FeatureCapabilityState.Unavailable,
                    "FRAMERATE_COMPOSITE_INCOMPLETE",
                    "60fps 解锁需要完整的三重绑定依赖：Hook 41（帧率解锁游戏更新）、" +
                    "PatchSet 1（运行时字节补丁）和 State 20（帧率状态同步）。" +
                    " PatchSet 1 尚未注册。");
            }
        }

        // ── Transitional P1: SelectedUnitObjectUpgrade RawName special case ──
        // Replicates the 3-condition check that was previously inline in
        // TrainerSessionManager.GetFeatureCapability (profileId + native layout).
        // Uses specific reason codes expected by existing characterization tests.
        if (string.Equals(feature.RawName, TrainerFeatureIds.SelectedUnitObjectUpgrade, System.StringComparison.Ordinal))
        {
            var profile = context.CurrentProfile;
            if (profile is null ||
                !string.Equals(profile.Id, "ra3_1.12", System.StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    FeatureCapabilityState.Unavailable,
                    "UNIT_UPGRADE_PROFILE_NOT_SUPPORTED",
                    "单位升级功能当前仅在 RA3 1.12 完成验证。请使用 1.12 版本游戏。");
            }

            if (!IsUnitUpgradeNativeLayoutReady(profile))
            {
                return new(
                    FeatureCapabilityState.Unavailable,
                    "UNIT_UPGRADE_NATIVE_LAYOUT_UNAVAILABLE",
                    "单位升级所需的引擎布局尚未在当前版本完成验证。");
            }
        }

        // ── Generic: CapabilityOnly profile requirements ────────────────────
        // For CapabilityOnly features with RequiredProfileIds, verify the current
        // profile is in the allowed set. Ran after the RawName special case so
        // SelectedUnitObjectUpgrade uses its own reason codes.
        if (behavior.AsCapabilityOnly() is { } capOnly &&
            capOnly.RequiredProfileIds.Count > 0)
        {
            var profileId = context.CurrentProfile?.Id;
            if (profileId is null ||
                !capOnly.RequiredProfileIds.Contains(profileId, System.StringComparer.OrdinalIgnoreCase))
            {
                return new(
                    FeatureCapabilityState.Unavailable,
                    "PROFILE_NOT_SUPPORTED",
                    "不可用：该功能仅支持已验证的特定游戏版本。");
            }
        }

        // Pass through — no gate downgraded the state.
        return new(baseSnapshot.State, baseSnapshot.ReasonCode, baseSnapshot.Reason);
    }

    /// <summary>
    /// Checks that the three native-agent catalog entries required for object-level
    /// upgrade grant are all Verified with a non-zero RVA.
    /// </summary>
    public static bool IsUnitUpgradeNativeLayoutReady(Ra3VersionProfile profile)
    {
        return TryHasVerifiedNonZeroRva(profile, "GameObjectAddUpgrade")
            && TryHasVerifiedNonZeroRva(profile, "ProductionModulesOffset")
            && TryHasVerifiedNonZeroRva(profile, "UpgradeTemplateTypeOffset");

        static bool TryHasVerifiedNonZeroRva(Ra3VersionProfile p, string name) =>
            p.NativeAgentRefs.TryGetValue(name, out var addr)
            && addr.Status == AddressSupportStatus.Verified
            && addr.Rva is > 0;
    }
}
