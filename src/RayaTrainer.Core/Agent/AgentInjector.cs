using System.ComponentModel;
using System.Text;

namespace RayaTrainer.Core.Agent;

public sealed class AgentInjector : IAgentInjector
{
    private const uint ProcessCreateThread = 0x0002;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint RequiredProcessAccess =
        ProcessCreateThread |
        ProcessVmOperation |
        ProcessVmRead |
        ProcessVmWrite |
        ProcessQueryInformation;
    private const uint MemCommitReserve = 0x3000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private readonly IAgentInjectorApi _api;

    public AgentInjector()
        : this(Kernel32AgentInjectorApi.Instance)
    {
    }

    public AgentInjector(IAgentInjectorApi api)
    {
        _api = api;
    }

    public AgentInjectionResult Inject(int processId, string agentDllPath, TimeSpan timeout)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process id must be positive.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        if (string.IsNullOrWhiteSpace(agentDllPath) || !File.Exists(agentDllPath))
        {
            throw new FileNotFoundException("Agent DLL was not found.", agentDllPath);
        }

        var fullPath = Path.GetFullPath(agentDllPath);
        var pathBytes = Encoding.Unicode.GetBytes(fullPath + '\0');
        var processHandle = _api.OpenProcess(RequiredProcessAccess, inheritHandle: false, processId);
        if (processHandle == 0)
        {
            throw CreateLastWin32Exception("OpenProcess failed.");
        }

        nint remotePath = 0;
        nint threadHandle = 0;
        try
        {
            var kernel32 = _api.GetModuleHandle("kernel32.dll");
            if (kernel32 == 0)
            {
                throw CreateLastWin32Exception("GetModuleHandle(kernel32.dll) failed.");
            }

            var loadLibrary = _api.GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == 0)
            {
                throw CreateLastWin32Exception("GetProcAddress(LoadLibraryW) failed.");
            }

            remotePath = _api.VirtualAllocEx(
                processHandle,
                (nuint)pathBytes.Length,
                MemCommitReserve,
                PageReadWrite);
            if (remotePath == 0)
            {
                throw CreateLastWin32Exception("VirtualAllocEx failed.");
            }

            if (!_api.WriteProcessMemory(processHandle, remotePath, pathBytes, out var written) ||
                written != (nuint)pathBytes.Length)
            {
                throw CreateLastWin32Exception("WriteProcessMemory failed.");
            }

            threadHandle = _api.CreateRemoteThread(processHandle, loadLibrary, remotePath, out _);
            if (threadHandle == 0)
            {
                throw CreateLastWin32Exception("CreateRemoteThread failed.");
            }

            var waitResult = _api.WaitForSingleObject(threadHandle, ToWaitMilliseconds(timeout));
            if (waitResult == WaitTimeout)
            {
                throw new TimeoutException("Timed out waiting for LoadLibraryW remote thread.");
            }

            if (waitResult != WaitObject0)
            {
                throw CreateLastWin32Exception($"WaitForSingleObject returned 0x{waitResult:X8}.");
            }

            if (!_api.GetExitCodeThread(threadHandle, out var exitCode))
            {
                throw CreateLastWin32Exception("GetExitCodeThread failed.");
            }

            if (exitCode == 0)
            {
                return new AgentInjectionResult(false, "LoadLibraryW returned null.", 0);
            }

            return new AgentInjectionResult(true, "Agent DLL injected.", unchecked((nint)exitCode));
        }
        finally
        {
            if (remotePath != 0)
            {
                _api.VirtualFreeEx(processHandle, remotePath, 0, MemRelease);
            }

            if (threadHandle != 0)
            {
                _api.CloseHandle(threadHandle);
            }

            _api.CloseHandle(processHandle);
        }
    }

    private static uint ToWaitMilliseconds(TimeSpan timeout)
    {
        var milliseconds = timeout.TotalMilliseconds;
        if (milliseconds >= uint.MaxValue)
        {
            return uint.MaxValue - 1;
        }

        return Math.Max(1, (uint)Math.Ceiling(milliseconds));
    }

    private Win32Exception CreateLastWin32Exception(string message)
    {
        var error = _api.GetLastWin32Error();
        return new Win32Exception(error, $"{message} Win32 error {error}: {new Win32Exception(error).Message}");
    }
}
