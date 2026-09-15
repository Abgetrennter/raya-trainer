using System.Windows;
using RayaTrainer.App.Hotkeys;
using RayaTrainer.Core.Hotkeys;

namespace RayaTrainer.App.Services;

public sealed class HotkeyOrchestrator : IDisposable
{
    private readonly LowLevelKeyboardHook _keyboardHook = new();
    private readonly HotkeyFeatureDispatcher _dispatcher = new();
    private Func<bool>? _isTargetGameForeground;

    public HotkeyOrchestrator()
    {
        _keyboardHook.KeyDown += OnKeyboardHookKeyDown;
        _keyboardHook.KeyUp += OnKeyboardHookKeyUp;
    }

    public void SetForegroundChecker(Func<bool> checker) => _isTargetGameForeground = checker;

    public void Start(IEnumerable<HotkeyActionBinding> bindings)
    {
        _dispatcher.Update(bindings.ToArray(), enabled: true);
        _keyboardHook.Install();
    }

    public void Stop()
    {
        _keyboardHook.Uninstall();
        _dispatcher.Update([], enabled: false);
    }

    public void Dispose()
    {
        Stop();
        _keyboardHook.Dispose();
    }

    private void OnKeyboardHookKeyDown(object? sender, KeyboardHookEventArgs e)
    {
        if (_isTargetGameForeground?.Invoke() != true)
        {
            return;
        }

        App.Current.Dispatcher.Invoke(() =>
        {
            e.Handled = _dispatcher.TryDispatch(e.VirtualKey, e.Modifiers);
        });
    }

    private void OnKeyboardHookKeyUp(object? sender, int virtualKey)
    {
        _dispatcher.Release(virtualKey);
    }
}
