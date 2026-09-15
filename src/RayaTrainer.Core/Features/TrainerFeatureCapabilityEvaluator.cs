using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Features;

public sealed record TrainerFeatureCapabilityContext(
    bool HasTarget,
    bool SessionReady,
    bool PatchesInstalled,
    bool BackendSupportsDirectGameApi,
    bool DirectGameApiReady,
    string? UnavailableReason = null,
    string UnavailableReasonCode = "PROFILE_OR_HOOK_UNAVAILABLE");

public static class TrainerFeatureCapabilityEvaluator
{
    public static FeatureCapabilitySnapshot Evaluate(
        TrainerFeature feature,
        TrainerFeatureCapabilityContext context)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(context);
        var groupName = TrainerFeatureGroupCatalog.GetGroupName(feature);

        if (!string.IsNullOrWhiteSpace(context.UnavailableReason))
        {
            return Create(
                feature,
                groupName,
                FeatureCapabilityState.Unavailable,
                context.UnavailableReasonCode,
                context.UnavailableReason);
        }

        if (!context.HasTarget)
        {
            return Create(
                feature,
                groupName,
                FeatureCapabilityState.Waiting,
                "NO_TARGET",
                "待连接：请先检测并连接受支持的游戏进程。");
        }

        if (feature.RequiresDirectGameApi && !context.BackendSupportsDirectGameApi)
        {
            return Create(
                feature,
                groupName,
                FeatureCapabilityState.Unavailable,
                "DIRECT_GAME_API_REQUIRED",
                "不可用：该功能需要已启用 Direct GameApi 的 DLL Agent 后端。");
        }

        if (!context.SessionReady)
        {
            return Create(
                feature,
                groupName,
                FeatureCapabilityState.Waiting,
                "SESSION_NOT_READY",
                "待连接：当前会话尚未准备完成。");
        }

        if (!context.PatchesInstalled)
        {
            return Create(
                feature,
                groupName,
                FeatureCapabilityState.Waiting,
                "PATCH_NOT_INSTALLED",
                "待安装：连接已建立，请安装 Patch 后使用。");
        }

        if (feature.RequiresDirectGameApi && !context.DirectGameApiReady)
        {
            return Create(
                feature,
                groupName,
                FeatureCapabilityState.Unavailable,
                "DIRECT_GAME_API_NOT_READY",
                "不可用：Agent 已连接，但 Direct GameApi controller 未就绪。");
        }

        return Create(
            feature,
            groupName,
            FeatureCapabilityState.Ready,
            "READY",
            "就绪：当前会话满足该功能的执行条件。");
    }

    private static FeatureCapabilitySnapshot Create(
        TrainerFeature feature,
        string groupName,
        FeatureCapabilityState state,
        string reasonCode,
        string reason) =>
        new(feature.RawName, feature.DisplayName, groupName, state, reasonCode, reason);
}
