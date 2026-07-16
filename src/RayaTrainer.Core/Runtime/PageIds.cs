namespace RayaTrainer.Core.Runtime;

/// <summary>
/// 稳定页面 ID。settings 持久化用 ID，运行时映射到 SelectedPageIndex。
/// 顺序对应 MainWindow.xaml 侧边栏索引（0-7）。
/// </summary>
public static class PageIds
{
    public const string Features = "features";              // 0
    public const string SelectedUnit = "selected-unit";     // 1
    public const string Reinforcement = "reinforcement";    // 2
    public const string SecretProtocol = "secret-protocol"; // 3
    public const string Tools = "tools";                    // 4
    public const string StatusEditor = "status-editor";     // 5
    public const string Diagnostics = "diagnostics";        // 6
    public const string HotkeySettings = "hotkey-settings"; // 7

    public static readonly IReadOnlyList<string> InIndexOrder =
    [
        Features, SelectedUnit, Reinforcement, SecretProtocol,
        Tools, StatusEditor, Diagnostics, HotkeySettings
    ];

    public static int ToIndex(string? pageId)
    {
        if (pageId is null) return 0;
        for (var i = 0; i < InIndexOrder.Count; i++)
        {
            if (string.Equals(InIndexOrder[i], pageId, StringComparison.Ordinal))
                return i;
        }
        return 0;
    }

    public static string FromIndex(int index)
    {
        return (uint)index < (uint)InIndexOrder.Count
            ? InIndexOrder[index]
            : Features;
    }
}
