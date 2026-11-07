namespace RayaTrainer.Core.Runtime;

/// <summary>
/// 稳定页面 ID。settings 持久化用 ID，运行时映射到 SelectedPageIndex。
/// 顺序对应 MainWindow.xaml 侧边栏索引（0-9，公共外壳共 10 页）。
/// operation-explorer 只存在于私有构建（索引 10），由 App 私有分部承接其索引/持久化映射，不进公共 InIndexOrder。
/// </summary>
public static class PageIds
{
    public const string Features = "features";              // 0
    public const string SelectedUnit = "selected-unit";     // 1
    public const string Ascension = "ascension";            // 2
    public const string Reinforcement = "reinforcement";    // 3
    public const string SecretProtocol = "secret-protocol"; // 4
    public const string Diagnostics = "diagnostics";        // 5
    public const string HotkeySettings = "hotkey-settings"; // 6
    public const string Tools = "tools";                    // 7
    public const string StatusEditor = "status-editor";     // 8
    public const string ProductConsole = "product-console"; // 9

    public static readonly IReadOnlyList<string> InIndexOrder =
    [
        // 游戏功能（高频）→ 诊断（出问题时的出口）→ 配置 → 高级
        Features, SelectedUnit, Ascension, Reinforcement, SecretProtocol,
        Diagnostics, HotkeySettings, Tools, StatusEditor, ProductConsole
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
