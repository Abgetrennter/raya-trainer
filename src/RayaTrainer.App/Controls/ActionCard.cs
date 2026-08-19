using System.Windows;
using System.Windows.Controls;

namespace RayaTrainer.App.Controls;

/// <summary>
/// 可折叠卡片，带标题栏。Header 为标题文本，Content 为展开内容。
/// IsExpanded 控制展开/折叠状态。
/// </summary>
public sealed class ActionCard : HeaderedContentControl
{
    static ActionCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ActionCard),
            new FrameworkPropertyMetadata(typeof(ActionCard)));
    }

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(ActionCard),
            new FrameworkPropertyMetadata(true));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }
}
