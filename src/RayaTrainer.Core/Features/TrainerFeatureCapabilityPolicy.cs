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

        // ── Generic: NativeToggle runtime dependency gates ──────────────────
        // A declared HookId or PatchSetId must be present in the installed agent before
        // the feature is Ready. Most state-only toggles declare neither dependency.
        if (behavior.AsNativeToggle() is { } toggle)
        {
            if (toggle.HookId.HasValue &&
                !context.InstalledNativeHookIds.Contains(toggle.HookId.Value))
            {
                if (!toggle.PatchSetId.HasValue)
                {
                    return new(
                        FeatureCapabilityState.Unavailable,
                        "NATIVE_HOOK_MISSING",
                        $"{feature.DisplayName}需要的 Hook {toggle.HookId.Value} 尚未安装，请重新连接游戏。");
                }

                return new(
                    FeatureCapabilityState.Unavailable,
                    "FRAMERATE_COMPOSITE_INCOMPLETE",
                    "60fps 解锁需要完整的三重绑定依赖：Hook 41（帧率解锁游戏更新）、" +
                    "PatchSet 1（运行时字节补丁）和 State 20（帧率状态同步）。" +
                    " Hook 41 尚未安装。");
            }

            if (toggle.PatchSetId.HasValue &&
                !context.RegisteredPatchSetIds.Contains(toggle.PatchSetId.Value))
            {
                return new(
                    FeatureCapabilityState.Unavailable,
                    "FRAMERATE_COMPOSITE_INCOMPLETE",
                    "60fps 解锁需要完整的三重绑定依赖：Hook 41（帧率解锁游戏更新）、" +
                    "PatchSet 1（运行时字节补丁）和 State 20（帧率状态同步）。" +
                    " PatchSet 1 尚未注册。");
            }
        }

        // ── Generic: CapabilityOnly profile requirements ────────────────────
        // For CapabilityOnly features with RequiredProfileIds, verify the current
        // profile is in the allowed set. SelectedUnitObjectUpgrade is registered as a
        // CapabilityOnlyFeatureBehavior(["ra3_1.12"], RequiresExactProfile=true), so it
        // is handled uniformly by this gate — no RawName special case needed.
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
}
