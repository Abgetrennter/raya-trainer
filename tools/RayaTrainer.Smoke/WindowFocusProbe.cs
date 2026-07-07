using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed record WindowFocusCycleResult(bool GameActivated, bool ReturnedToPrevious);

internal static class WindowFocusProbe
{
    public static async Task<WindowFocusCycleResult> CycleGameToBackgroundAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        var previousWindow = GetForegroundWindow();
        using var process = Process.GetProcessById(processId);
        process.Refresh();
        var gameWindow = process.MainWindowHandle;
        if (gameWindow == 0)
        {
            return new WindowFocusCycleResult(false, false);
        }

        var backgroundWindow = previousWindow != 0 && previousWindow != gameWindow
            ? previousWindow
            : FindBackgroundWindow(processId);
        if (backgroundWindow == 0)
        {
            return new WindowFocusCycleResult(false, false);
        }

        if (GetForegroundWindow() != gameWindow)
        {
            ForceForegroundWindow(gameWindow);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var gameActivated = GetForegroundWindow() == gameWindow;
        ForceForegroundWindow(backgroundWindow);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        return new WindowFocusCycleResult(gameActivated, GetForegroundWindow() == backgroundWindow);
    }

    private static void ForceForegroundWindow(nint targetWindow)
    {
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var targetThread = GetWindowThreadProcessId(targetWindow, out _);
        var currentThread = GetCurrentThreadId();
        var attachedForeground = foregroundThread != 0 &&
            foregroundThread != currentThread &&
            AttachThreadInput(currentThread, foregroundThread, true);
        var attachedTarget = targetThread != 0 &&
            targetThread != currentThread &&
            targetThread != foregroundThread &&
            AttachThreadInput(currentThread, targetThread, true);

        try
        {
            ShowWindow(targetWindow, 9);
            BringWindowToTop(targetWindow);
            SetForegroundWindow(targetWindow);
            SetFocus(targetWindow);
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
            if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static nint FindBackgroundWindow(int gameProcessId)
    {
        foreach (var candidate in Process.GetProcesses()
                     .OrderByDescending(process =>
                         process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase)))
        {
            using (candidate)
            {
                try
                {
                    if (candidate.Id == gameProcessId || candidate.Id == Environment.ProcessId) continue;
                    candidate.Refresh();
                    if (candidate.MainWindowHandle != 0) return candidate.MainWindowHandle;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return 0;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);
}
