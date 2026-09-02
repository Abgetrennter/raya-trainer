using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RayaTrainer.App.ViewModels;

namespace RayaTrainer.App.Pages;

/// <summary>
/// 快捷键设置页。承载「修改快捷键」跳转定位：功能徽章右键跳转后滚动到目标行并短暂高亮。
/// </summary>
public partial class HotkeySettingsPage : UserControl
{
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(2.5);

    private HotkeyRowViewModel? _highlightedRow;
    private DispatcherTimer? _highlightTimer;

    public HotkeySettingsPage()
    {
        InitializeComponent();
        // DataContext 由 MainWindow.xaml 绑定，晚于构造；经 DataContextChanged 挂接跳转事件。
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is HotkeySettingsViewModel oldViewModel)
        {
            oldViewModel.RevealRequested -= OnRevealRequested;
        }
        if (e.NewValue is HotkeySettingsViewModel newViewModel)
        {
            newViewModel.RevealRequested += OnRevealRequested;
        }
    }

    private void OnRevealRequested(HotkeyRowViewModel row)
    {
        if (_highlightedRow is not null)
        {
            _highlightedRow.IsHighlighted = false;
        }

        _highlightedRow = row;
        row.IsHighlighted = true;
        // 页面可能刚经导航从折叠变为可见，等一拍布局完成后再滚动定位。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FindRowElement(this, row)?.BringIntoView());
        RestartHighlightTimer();
    }

    private void RestartHighlightTimer()
    {
        if (_highlightTimer is null)
        {
            _highlightTimer = new DispatcherTimer { Interval = HighlightDuration };
            _highlightTimer.Tick += OnHighlightTimeout;
        }

        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void OnHighlightTimeout(object? sender, System.EventArgs e)
    {
        _highlightTimer?.Stop();
        if (_highlightedRow is not null)
        {
            _highlightedRow.IsHighlighted = false;
            _highlightedRow = null;
        }
    }

    /// <summary>在可视树中查找 DataContext 为目标行 VM 的元素（行模板是普通 ItemsControl，无虚拟化，可直接遍历）。</summary>
    private static FrameworkElement? FindRowElement(DependencyObject root, object dataContext)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { DataContext: var context } element && ReferenceEquals(context, dataContext))
            {
                return element;
            }

            var deeper = FindRowElement(child, dataContext);
            if (deeper is not null)
            {
                return deeper;
            }
        }

        return null;
    }
}
