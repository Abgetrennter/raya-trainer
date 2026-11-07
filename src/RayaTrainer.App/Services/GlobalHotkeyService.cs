using System.Runtime.InteropServices;
using System.Windows.Interop;
using RayaTrainer.Core.Hotkeys;

namespace RayaTrainer.App.Services;

/// <summary>
/// 全局热键服务：基于 Win32 RegisterHotKey，让指定组合键在修改器窗口最小化或非前台时也能触发。
/// 与 <see cref="HotkeyOrchestrator"/>（低层键盘钩子，要求游戏窗口前台）互补：
/// 本服务面向"修改器主控操作"（如立刻检测、装载并启动），这些按钮逻辑上需要在游戏未启动/未附加时触发。
/// 所有调用必须在拥有 hwnd 的同一线程（UI 线程）上进行。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_NOREPEAT = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly HwndSourceHook _hook;
    // id → 回调。注册失败的 id 不进集合，故集合状态即"当前已注册的全局热键"。
    private readonly Dictionary<int, Action> _callbacksById = new();
    private bool _disposed;

    public GlobalHotkeyService(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("必须提供有效的窗口句柄。", nameof(hwnd));
        }
        _hwnd = hwnd;
        _hook = WndProc;
        // HwndSource.FromHwnd 在窗口已销毁时返回 null，此时无挂载点，构造失败更安全。
        if (HwndSource.FromHwnd(hwnd) is not { } source)
        {
            throw new ArgumentException("无法从句柄获取 HwndSource，窗口可能已关闭。", nameof(hwnd));
        }
        source.AddHook(_hook);
    }

    /// <summary>
    /// 注册全局热键。返回 false 表示组合键已被其他程序占用或参数无效。
    /// 重复 id 会先反注册旧的再注册新的，保证幂等。
    /// </summary>
    public bool Register(int id, HotkeyGesture gesture, Action callback)
    {
        Unregister(id);
        var fsModifiers = TranslateModifiers(gesture.Modifiers) | MOD_NOREPEAT;
        if (!RegisterHotKey(_hwnd, id, fsModifiers, gesture.VirtualKey))
        {
            return false;
        }
        _callbacksById[id] = callback;
        return true;
    }

    /// <summary>反注册指定 id；未注册过的 id 静默忽略。</summary>
    public void Unregister(int id)
    {
        if (_callbacksById.Remove(id))
        {
            UnregisterHotKey(_hwnd, id);
        }
    }

    /// <summary>反注册所有已注册热键。</summary>
    public void UnregisterAll()
    {
        foreach (var id in _callbacksById.Keys.ToList())
        {
            UnregisterHotKey(_hwnd, id);
        }
        _callbacksById.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_callbacksById.TryGetValue(id, out var callback))
            {
                callback();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static int TranslateModifiers(HotkeyModifiers modifiers)
    {
        // HotkeyModifiers 枚举值与 Win32 MOD_* 不一致，必须显式映射。
        var result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= MOD_ALT;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= MOD_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= MOD_SHIFT;
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        UnregisterAll();
        if (HwndSource.FromHwnd(_hwnd) is { } source)
        {
            source.RemoveHook(_hook);
        }
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
