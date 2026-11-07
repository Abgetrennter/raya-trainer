using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RayaTrainer.Host.Services.GameCrashCapture;

/// <summary>
/// 精确进程查询（P/Invoke，与 Kernel32AgentInjectorApi 同风格）。.NET 的
/// Process.StartTime 有毫秒截断，不能用于 CrashCapture 的 PID+创建时间身份绑定；
/// 提交内存（PagefileUsage）用于磁盘余量预检（设计 §6）。
/// </summary>
public static class NativeProcessQueries
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern nint OpenProcessNative(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTimeUnion
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        nint handle,
        out FileTimeUnion creation,
        out FileTimeUnion exit,
        out FileTimeUnion kernel,
        out FileTimeUnion user);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        nint handle,
        uint flags,
        StringBuilder imageName,
        ref uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint handle, ref ProcessMemoryCounters counters, uint cb);

    public static bool TryGetProcessIdentity(int pid, out ulong createTimeUtc, out string imagePath)
    {
        createTimeUtc = 0;
        imagePath = string.Empty;
        var handle = OpenProcessNative(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return false;
        }

        try
        {
            if (!GetProcessTimes(handle, out var creation, out _, out _, out _))
            {
                return false;
            }

            createTimeUtc = ((ulong)creation.High << 32) | creation.Low;

            var builder = new StringBuilder(1024);
            uint size = 1024;
            if (!QueryFullProcessImageName(handle, 0, builder, ref size))
            {
                return false;
            }

            imagePath = builder.ToString(0, (int)size);
            return true;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    public static bool TryGetCommitChargeBytes(int pid, out ulong commitChargeBytes)
    {
        commitChargeBytes = 0;
        var handle = OpenProcessNative(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return false;
        }

        try
        {
            var counters = new ProcessMemoryCounters { Cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            if (!GetProcessMemoryInfo(handle, ref counters, counters.Cb))
            {
                return false;
            }

            commitChargeBytes = counters.PagefileUsage;
            return true;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }
}
