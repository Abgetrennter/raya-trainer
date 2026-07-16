using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.App.Web;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.App;

public partial class App : Application
{
    private TrainerWebHost? _webHost;

    public App()
    {
        ConfigureRendering();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        // 兜底通道：async/await 链路里没人 await 的 Task 抛出的异常（如广播器轮询读
        // 游戏内存时抛 Win32Exception）会在 GC 时经这里触发。不注册的话，.NET 默认
        // 会在 Task 被 GC 时把它升级为进程级未处理异常，导致崩溃码 0xe0434352。
        // 注册后 Observed 设为 true，仅记录、不杀进程。
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void ConfigureRendering()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mobileRemoteAvailability = new MobileRemoteAvailability();

        try
        {
            var manifest = TrainerRuntimeAssets.LoadManifest();
            var sessionManager = new TrainerSessionManager();
            var settingsStore = new TrainerAppSettingsStore();
            var viewModel = MainViewModel.Load(
                manifest,
                settingsStore,
                sessionManager: sessionManager,
                mobileRemoteAvailability: mobileRemoteAvailability);

            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();

            _webHost = TrainerWebHost.Create(
                sessionManager,
                manifest,
                settingsStore: settingsStore,
                presetSource: viewModel,
                featureStateCoordinator: viewModel.FeatureState);
        }
        catch (Exception exception)
        {
            RayaTrainerCrashLog.Write(exception);
            MessageBox.Show(
                exception.Message,
                "RAЯ Trainer 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        try
        {
            await _webHost.StartAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            mobileRemoteAvailability.MarkUnavailable(exception.Message);
            if (MainWindow?.DataContext is MainViewModel viewModel)
            {
                viewModel.Tools.MobileRemote.GenerateQrCodeCommand.RaiseCanExecuteChanged();
            }
            RayaTrainerCrashLog.Write(exception);
            MessageBox.Show(
                $"Web 控制面板启动失败：{exception.Message}",
                "RAЯ Trainer Web",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_webHost is not null)
            {
                await _webHost.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            RayaTrainerCrashLog.Write(exception);
        }
        finally
        {
            base.OnExit(e);
        }
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
