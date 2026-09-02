using System.Runtime.InteropServices;
using System.Windows;
using RayaTrainer.Core.Hotkeys;

namespace RayaTrainer.App.Hotkeys;

public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _callback;
    private nint _hook;

    public LowLevelKeyboardHook()
    {
        _callback = HookCallback;
    }

    public event EventHandler<KeyboardHookEventArgs>? KeyDown;

    public event EventHandler<int>? KeyUp;

    public bool IsInstalled => _hook != 0;

    public void Install()
    {
        if (_hook != 0)
        {
            return;
        }

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, 0, 0);
        if (_hook == 0)
        {
            throw new InvalidOperationException(
                $"SetWindowsHookEx failed. Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void Uninstall()
    {
        if (_hook == 0)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    public void Dispose()
    {
        Uninstall();
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        var virtualKey = Marshal.ReadInt32(lParam);

        if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            {
                var key = virtualKey;
                dispatcher.BeginInvoke(() => KeyUp?.Invoke(this, key));
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            var args = new KeyboardHookEventArgs(virtualKey, CurrentModifiers());
            KeyDown?.Invoke(this, args);
            if (args.Handled)
            {
                return 1;
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsKeyDown(0x11) || IsKeyDown(0xA2) || IsKeyDown(0xA3))
        {
            modifiers |= HotkeyModifiers.Control;
        }
        if (IsKeyDown(0x12) || IsKeyDown(0xA4) || IsKeyDown(0xA5))
        {
            modifiers |= HotkeyModifiers.Alt;
        }
        if (IsKeyDown(0x10) || IsKeyDown(0xA0) || IsKeyDown(0xA1))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        nint hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
