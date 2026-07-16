using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(MainViewModel.LoadDefault())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitializedRestoreBounds;
        LocationChanged += OnWindowGeometryChanged;
        SizeChanged += OnWindowGeometryChanged;
        StateChanged += OnWindowGeometryChanged;
        Closing += OnClosingFlush;
    }

    // HWND 已创建但窗口未显示时触发：恢复窗口几何 + 注册 Win32 全局热键。
    private void OnSourceInitializedRestoreBounds(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (vm.LastWindowBounds is { } b && b.IsValidOnAnyScreen())
            {
                Left = b.X;
                Top = b.Y;
                Width = b.Width;
                Height = b.Height;
                if (b.IsMaximized) WindowState = WindowState.Maximized;
            }
            var hwnd = new WindowInteropHelper(this).Handle;
            vm.InitializeGlobalHotkeys(hwnd);
        }
    }

    // 窗口位置/尺寸/状态变化时捕获几何并标记脏。最小化时不覆盖（保留正常几何）。
    private void OnWindowGeometryChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (WindowState == WindowState.Minimized) return;
        vm.UpdateWindowBounds(new WindowBounds(Left, Top, Width, Height, WindowState == WindowState.Maximized));
    }

    // 退出时同步 flush 持久化协调器，确保偏好写入磁盘。
    private void OnClosingFlush(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Persistence?.Flush();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
        base.OnClosed(e);
    }
}
