namespace RayaTrainer.Host.Services;

/// <summary>
/// Web 遥控开关请求与桌面端 desired-state 协调的抽象入口。
/// App 注入 <c>FeatureStateCoordinator</c>（更新 ViewModel desired 状态并触发持久化重放）；
/// 独立组件（如 WebMini）不注入，走 <c>IGameApiCommandQueue</c> 直控 fallback。
/// </summary>
public interface IFeatureToggleCoordinator
{
    /// <summary>
    /// 尝试把指定功能的 desired 状态设为 <paramref name="enabled"/>。
    /// 返回 <c>false</c> 表示协调器找不到该功能项，调用方应回退到直控路径。
    /// </summary>
    Task<bool> TrySetToggleDesiredAsync(string rawName, bool enabled);
}
