using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RayaTrainer.App.Pages;
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
        AttachPrivateSurfaces(viewModel);
        InitializePageHost(viewModel);
        SourceInitialized += OnSourceInitializedRestoreBounds;
        LocationChanged += OnWindowGeometryChanged;
        SizeChanged += OnWindowGeometryChanged;
        StateChanged += OnWindowGeometryChanged;
        Closing += OnClosingFlush;
    }

    // 页面懒构建（修复启动白屏）：原实现把 10 个页面写在 XAML 里随 InitializeComponent
    // 一次性实例化+绑定，全部压进首帧之前。现在启动只构建当前选中页，其余页在首帧后
    // 的空闲时段逐个补齐；补齐前切页则同步构建。已建页面常驻 PageHost，只切 Visibility，
    // 与旧的 Visibility 绑定行为等价。
    private readonly Dictionary<int, Func<UIElement>> _pageFactories = new();
    private readonly Dictionary<int, UIElement> _pages = new();
    private int _visiblePageIndex = -1;

    private void InitializePageHost(MainViewModel viewModel)
    {
        _pageFactories[PageIds.ToIndex(PageIds.Features)] = () => new FeaturesPage { DataContext = viewModel.FeatureToggle };
        _pageFactories[PageIds.ToIndex(PageIds.SelectedUnit)] = () => new SelectedUnitPage { DataContext = viewModel.SelectedUnit };
        _pageFactories[PageIds.ToIndex(PageIds.Reinforcement)] = () => new ReinforcementPage { DataContext = viewModel.Reinforcement };
        _pageFactories[PageIds.ToIndex(PageIds.SecretProtocol)] = () => new SecretProtocolPage { DataContext = viewModel.SecretProtocol };
        _pageFactories[PageIds.ToIndex(PageIds.Tools)] = () => new ToolsPage { DataContext = viewModel.Tools };
        _pageFactories[PageIds.ToIndex(PageIds.StatusEditor)] = () => new StatusEditorPage();
        _pageFactories[PageIds.ToIndex(PageIds.Diagnostics)] = () => new DiagnosticsPage { DataContext = viewModel.Diagnostics };
        _pageFactories[PageIds.ToIndex(PageIds.HotkeySettings)] = () => new HotkeySettingsPage { DataContext = viewModel.HotkeySettings };
        _pageFactories[PageIds.ToIndex(PageIds.ProductConsole)] = () => new ProductConsolePage { DataContext = viewModel.ProductConsole };
        _pageFactories[PageIds.ToIndex(PageIds.Ascension)] = () => new AscensionPage { DataContext = viewModel.Ascension };

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPageIndex))
            {
                ShowPage(viewModel.SelectedPageIndex);
            }
        };
        ShowPage(viewModel.SelectedPageIndex);

        // 首帧之后的空闲时段逐帧补一页，避免一次性构建再把 UI 线程占满。
        var dispatcher = Dispatcher;
        foreach (var index in _pageFactories.Keys)
        {
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => EnsurePage(index)));
        }
    }

    private void ShowPage(int index)
    {
        if (index == _visiblePageIndex)
        {
            return;
        }

        _visiblePageIndex = index;
        EnsurePage(index);
        foreach (var (pageIndex, page) in _pages)
        {
            page.Visibility = pageIndex == index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private UIElement EnsurePage(int index)
    {
        if (!_pages.TryGetValue(index, out var page))
        {
            page = _pageFactories[index]();
            // 预构建的页面必须以隐藏态进树，否则空闲补页时全部叠在当前页上，
            // 直到下一次切页才被 ShowPage 纠正。
            page.Visibility = index == _visiblePageIndex ? Visibility.Visible : Visibility.Collapsed;
            PageHost.Children.Add(page);
            _pages[index] = page;
        }

        return page;
    }

    // 私有构建钩子：向导航与页面宿主注入私有开发页（Private/MainWindow.Private.cs 实现）；
    // 公共投影排除 Private/** 后为空操作，公共 XAML 保持不含私有页入口。
    partial void AttachPrivateSurfaces(MainViewModel viewModel);

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
