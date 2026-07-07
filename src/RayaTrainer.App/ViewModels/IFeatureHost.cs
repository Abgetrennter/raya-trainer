using RayaTrainer.Core.Features;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// FeatureItemViewModel 与协调者之间的契约，解耦对 MainViewModel 的硬依赖。
/// 由 MainViewModel 实现，方法体按职责委托给对应子 ViewModel
/// （资源值写入委托 FeatureToggleViewModel，增援参数委托 ReinforcementViewModel）。
/// </summary>
public interface IFeatureHost
{
    bool ArePatchesInstalled { get; }

    ITrainerFeatureController? FeatureController { get; }

    string StatusMessage { set; }

    FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature);

    void WriteResourceValuesIfNeeded(TrainerFeature feature);

    void WriteTargetHealthIfNeeded(TrainerFeature feature);

    void OnFeatureToggleChanged(TrainerFeature feature, bool enabled);

    void CompleteActionIfNeeded(TrainerFeature feature, ActionDispatchResult result);

    ReinforcementSettings GetReinforcementSettings();

    /// <summary>从功能列表徽章请求跳转到快捷键设置页（集中改键入口）。</summary>
    void OpenHotkeySettings();

    /// <summary>就地清除某个功能的快捷键（置空，立即生效并持久化）。</summary>
    void ClearHotkey(TrainerFeature feature);
}
