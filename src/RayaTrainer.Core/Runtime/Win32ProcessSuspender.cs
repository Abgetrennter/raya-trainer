using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RayaTrainer.Core.Runtime;

public sealed class Win32ProcessSuspender : IProcessSuspender
{
    private const uint ThreadSuspendResume = 0x0002;
    private const uint SuspendFailed = 0xFFFFFFFF;

    public static Win32ProcessSuspender Instance { get; } = new();

    private Win32ProcessSuspender()
    {
    }

    public IDisposable Suspend(int processId)
    {
        var suspendedThreads = new List<SafeWaitHandle>();
        try
        {
            using var process = Process.GetProcessById(processId);
            foreach (ProcessThread thread in process.Threads)
            {
                var handle = OpenThread(ThreadSuspendResume, false, (uint)thread.Id);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    continue;
                }

                if (SuspendThread(handle) == SuspendFailed)
                {
                    handle.Dispose();
                    continue;
                }

                suspendedThreads.Add(handle);
            }

            if (suspendedThreads.Count == 0)
            {
                throw new InvalidOperationException($"No target threads could be suspended for process {processId}.");
            }

            return new SuspendedProcess(suspendedThreads);
        }
        catch
        {
            ResumeAndDispose(suspendedThreads);
            throw;
        }
    }

    private static void ResumeAndDispose(IReadOnlyList<SafeWaitHandle> suspendedThreads)
    {
        for (var index = suspendedThreads.Count - 1; index >= 0; index--)
        {
            var thread = suspendedThreads[index];
            ResumeThread(thread);
            thread.Dispose();
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenThread", SetLastError = true, ExactSpelling = true)]
    private static extern SafeWaitHandle OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", EntryPoint = "SuspendThread", SetLastError = true, ExactSpelling = true)]
    private static extern uint SuspendThread(SafeWaitHandle thread);

    [DllImport("kernel32.dll", EntryPoint = "ResumeThread", SetLastError = true, ExactSpelling = true)]
    private static extern uint ResumeThread(SafeWaitHandle thread);

    private sealed class SuspendedProcess : IDisposable
    {
        private readonly IReadOnlyList<SafeWaitHandle> _suspendedThreads;
        private bool _disposed;

        public SuspendedProcess(IReadOnlyList<SafeWaitHandle> suspendedThreads)
        {
            _suspendedThreads = suspendedThreads;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ResumeAndDispose(_suspendedThreads);
            _disposed = true;
        }
    }
}
