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

    /// <summary>
    /// 从功能列表徽章请求跳转到快捷键设置页（集中改键入口）。
    /// <paramref name="targetRawName"/> 为要定位的功能 RawName，设置页滚动到对应行并高亮；null 仅跳转。
    /// </summary>
    void OpenHotkeySettings(string? targetRawName);

    /// <summary>
    /// ProductIntent 行为的执行入口：经产品控制会话提交绑定的 Product Intent
    /// （自定义倍率产品附带共享输入框的整型参数）并轮询到终态。
    /// 返回是否成功与面向用户的消息；非 ProductIntent 功能返回 (false, 提示)。
    /// </summary>
    Task<(bool Success, string Message)> ExecuteProductIntentFeatureAsync(TrainerFeature feature);
}
