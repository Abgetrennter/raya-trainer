using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Host;
using RayaTrainer.Host.Services;

namespace RayaTrainer.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        // 兜底通道：async/await 链路里没人 await 的 Task 抛出的异常（如广播器轮询读
        // 游戏内存时抛 Win32Exception）会在 GC 时经这里触发。不注册的话，.NET 默认
        // 会在 Task 被 GC 时把它升级为进程级未处理异常，导致崩溃码 0xe0434352。
        // 注册后 Observed 设为 true，仅记录、不杀进程。
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 自制启动闪屏：WPF 自动闪屏（<SplashScreen> 项）被 WPF 接管后会跳到屏幕
        // 左上角且无法控制淡出时机，弃用。这个窗口全程居中、不抢焦点，盖住主窗
        // 构建与首帧；主窗 ContentRendered（首帧真正画完）后才淡出，观感无缝。
        ShowSplash();

        // Web 遥控已拆分为独立可选组件 RayaTrainer.WebMini：主程序不再创建任何
        // Web 服务，默认完全不监听网络端口。
        try
        {
            var manifest = TrainerRuntimeAssets.LoadManifest();
            var sessionManager = new TrainerSessionManager();
            var settingsStore = new TrainerAppSettingsStore();
            var viewModel = MainViewModel.Load(
                manifest,
                settingsStore,
                sessionManager: sessionManager);

            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.ContentRendered += (_, _) => CloseSplash();
            window.Show();
        }
        catch (Exception exception)
        {
            CloseSplash();
            RayaTrainerCrashLog.Write(exception);
            MessageBox.Show(
                exception.Message,
                "RAЯ Trainer 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    // ---- 启动闪屏 ----

    private Window? _splash;

    private void ShowSplash()
    {
        var image = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/splash.png")),
            Width = 440,
            Height = 440
        };
        _splash = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x17)),
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = image,
            Width = 440,
            Height = 440,
            Topmost = true
        };
        _splash.Show();
    }

    private void CloseSplash()
    {
        var splash = _splash;
        _splash = null;
        if (splash is null)
        {
            return;
        }

        // 先降置顶再淡出：主窗已可见可交互，闪屏不能挡住任何点击。
        splash.Topmost = false;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => splash.Close();
        splash.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RayaTrainerCrashLog.Write(e.Exception);
        MessageBox.Show(
            e.Exception.Message,
            "RAЯ Trainer 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            RayaTrainerCrashLog.Write(exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 异步链路里没人 await 的 Task 抛出的异常会在这里触发（GC 时）。
        // 设 Observed = true 阻止 .NET 把它升级为进程级未处理异常；仅记录。
        RayaTrainerCrashLog.Write(e.Exception);
        e.SetObserved();
    }
}
