using System.Runtime.InteropServices;

namespace RayaTrainer.App.Hotkeys;

public sealed class ForegroundWindowProcess
{
    public int? GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == 0 ? null : unchecked((int)processId);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
