using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.Services;

/// <summary>
/// 功能状态统一协调器。
/// WPF click/hotkey 已走 FeatureItemViewModel.SetDesired（不变）。
/// Web SetToggleAsync 改走本协调器。
/// Agent Ready（ActivateInstalledSession）后调 ReplayDesiredState 把所有 desired=true 的 toggle 真正下发。
/// </summary>
public sealed class FeatureStateCoordinator
{
    private readonly Func<IEnumerable<FeatureItemViewModel>> _allItems;
    private readonly Func<ITrainerFeatureController?> _getController;
    private readonly Func<TrainerFeature, FeatureCapabilitySnapshot> _getCapability;
    private readonly IReadOnlyList<IFeatureParameterProvider> _providers;

    public FeatureStateCoordinator(
        Func<IEnumerable<FeatureItemViewModel>> allItems,
        Func<ITrainerFeatureController?> getController,
        Func<TrainerFeature, FeatureCapabilitySnapshot> getCapability,
        IReadOnlyList<IFeatureParameterProvider> providers)
    {
        _allItems = allItems;
        _getController = getController;
        _getCapability = getCapability;
        _providers = providers;
    }

    public FeatureItemViewModel? FindItem(string rawName) =>
        _allItems().FirstOrDefault(i => i.Feature.RawName == rawName);

    /// <summary>
    /// Agent Ready 后重放：把所有 DesiredEnabled==true 且 capability Ready 的 toggle 真正下发。
    /// desired=false 或 null 的不动（Agent 默认关闭，除非 readback 显示已开）。
    /// </summary>
    public ReplayResult ReplayDesiredState()
    {
        var controller = _getController();
        if (controller is null)
        {
            return new ReplayResult(0, 0, 0);
        }

        int applied = 0, skippedCapability = 0, skippedOther = 0;
        foreach (var item in _allItems())
        {
            if (!item.IsToggle) continue;
            if (item.DesiredEnabled != true) continue;

            var cap = _getCapability(item.Feature);
            if (cap.State != FeatureCapabilityState.Ready)
            {
                skippedCapability++;
                continue;
            }

            try
            {
                item.SetDesired(true, suppressApply: false);
                applied++;
            }
            catch
            {
                skippedOther++;
            }
        }
        return new ReplayResult(applied, skippedCapability, skippedOther);
    }

    public FeatureStateSnapshot CaptureSnapshot()
    {
        var toggles = new Dictionary<string, bool>();
        foreach (var item in _allItems())
        {
            if (item.IsToggle && item.DesiredEnabled is bool d)
                toggles[item.Feature.RawName] = d;
        }

        var parameters = new Dictionary<string, string>();
        foreach (var provider in _providers)
            foreach (var kv in provider.CaptureValidated())
                parameters[kv.Key] = kv.Value;

        return new FeatureStateSnapshot(toggles, parameters);
    }

    public SnapshotApplyResult ApplySnapshot(FeatureStateSnapshot snapshot, bool suppressRuntimeApply)
    {
        var applied = new List<string>();
        var skippedCap = new List<string>();
        var skippedOther = new List<string>();

        // 1. 恢复参数（suppressRuntimeApply 控制 provider 是否 writeBack）
        foreach (var provider in _providers)
        {
            provider.RestoreValidated(snapshot.ParameterValues, suppressRuntimeApply);
        }

        // 2. 恢复 toggle desired（suppressRuntimeApply 控制 controller 是否下发）
        var controller = suppressRuntimeApply ? null : _getController();
        foreach (var kv in snapshot.ToggleStates)
        {
            var item = FindItem(kv.Key);
            if (item is null || !item.IsToggle) continue;

            var cap = _getCapability(item.Feature);
            if (!suppressRuntimeApply && cap.State != FeatureCapabilityState.Ready)
            {
                // 记 desired 但不下发（capability 不足）
                item.SetDesired(kv.Value, suppressApply: true);
                skippedCap.Add(kv.Key);
                continue;
            }

            try
            {
                item.SetDesired(kv.Value, suppressApply: suppressRuntimeApply || controller is null);
                applied.Add(kv.Key);
            }
            catch
            {
                skippedOther.Add(kv.Key);
            }
        }

        return new SnapshotApplyResult(applied, skippedCap, skippedOther);
    }

    public sealed record ReplayResult(int Applied, int SkippedCapability, int SkippedOther);
}

public sealed record SnapshotApplyResult(
    IReadOnlyList<string> AppliedToggles,
    IReadOnlyList<string> SkippedToggles,
    IReadOnlyList<string> SkippedOther);
