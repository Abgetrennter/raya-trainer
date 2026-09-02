namespace RayaTrainer.Core.Hotkeys;

public enum ActionHotkeyScope
{
    /// <summary>游戏窗口前台时经低层键盘钩子触发（HotkeyOrchestrator 路径）。</summary>
    InGameHook,
    /// <summary>经 Win32 RegisterHotKey 注册为全局热键，修改器非前台也可触发。</summary>
    GlobalRegister,
}

/// <summary>
/// 声明式动作热键定义：在 MainViewModel 的动作热键声明表中登记一条记录，
/// 默认值、设置页行、冲突检测、绑定构建、热重载与全局注册即全部自动生效。
/// <para><see cref="GestureChanged"/> 在热键重载时收到新组合的显示文本（null=未分配），
/// 供按钮文本等呈现刷新；没有呈现面的定义可不挂回调。</para>
/// </summary>
public sealed record ActionHotkeyDefinition(
    string RawName,
    string DisplayName,
    string? DefaultGesture,
    Action Execute,
    Func<bool>? CanExecute,
    bool AllowRepeat = false,
    ActionHotkeyScope Scope = ActionHotkeyScope.InGameHook,
    Action<string?>? GestureChanged = null);

/// <summary>
/// 挂在功能目录既有 feature 上的命令转发：这类动作（如给基地车/呼叫增援）不在分组目录里、
/// 没有功能卡片，其设置页行、默认键与徽章刷新由 feature 管线负责，这里只补命令绑定与按钮文本刷新。
/// </summary>
public sealed record FeatureCommandOverride(
    string RawName,
    Action Execute,
    Func<bool>? CanExecute,
    bool AllowRepeat = false,
    Action<string?>? GestureChanged = null);
