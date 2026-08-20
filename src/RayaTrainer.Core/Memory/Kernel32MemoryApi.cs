using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RayaTrainer.Core.Memory;

internal sealed class Kernel32MemoryApi : IWin32MemoryApi
{
    public static Kernel32MemoryApi Instance { get; } = new();

    private Kernel32MemoryApi()
    {
    }

    public int GetLastWin32Error() => Marshal.GetLastWin32Error();

    public SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId) =>
        OpenProcessNative(desiredAccess, inheritHandle, processId);

    public bool ReadProcessMemory(SafeProcessHandle process, nint baseAddress, byte[] buffer, int size, out int bytesRead) =>
        ReadProcessMemoryNative(process, baseAddress, buffer, size, out bytesRead);

    public bool WriteProcessMemory(SafeProcessHandle process, nint baseAddress, byte[] buffer, int size, out int bytesWritten) =>
        WriteProcessMemoryNative(process, baseAddress, buffer, size, out bytesWritten);

    public nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint allocationType, uint protect) =>
        VirtualAllocExNative(process, address, size, allocationType, protect);

    public bool VirtualProtectEx(SafeProcessHandle process, nint address, nuint size, uint newProtect, out uint oldProtect) =>
        VirtualProtectExNative(process, address, size, newProtect, out oldProtect);

    public nuint VirtualQueryEx(SafeProcessHandle process, nint address, out Win32MemoryBasicInformation buffer, nuint length) =>
        VirtualQueryExNative(process, address, out buffer, length);

    public bool FlushInstructionCache(SafeProcessHandle process, nint baseAddress, nuint size) =>
        FlushInstructionCacheNative(process, baseAddress, size);

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true, ExactSpelling = true)]
    private static extern SafeProcessHandle OpenProcessNative(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", SetLastError = true, ExactSpelling = true)]
    private static extern bool ReadProcessMemoryNative(
        SafeProcessHandle process,
        nint baseAddress,
        [Out] byte[] buffer,
        int size,
        out int bytesRead);

    [DllImport("kernel32.dll", EntryPoint = "WriteProcessMemory", SetLastError = true, ExactSpelling = true)]
    private static extern bool WriteProcessMemoryNative(
        SafeProcessHandle process,
        nint baseAddress,
        byte[] buffer,
        int size,
        out int bytesWritten);

    [DllImport("kernel32.dll", EntryPoint = "VirtualAllocEx", SetLastError = true, ExactSpelling = true)]
    private static extern nint VirtualAllocExNative(
        SafeProcessHandle process,
        nint address,
        nuint size,
        uint allocationType,
        uint protect);

    [DllImport("kernel32.dll", EntryPoint = "VirtualProtectEx", SetLastError = true, ExactSpelling = true)]
    private static extern bool VirtualProtectExNative(
        SafeProcessHandle process,
        nint address,
        nuint size,
        uint newProtect,
        out uint oldProtect);

    [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx", SetLastError = true, ExactSpelling = true)]
    private static extern nuint VirtualQueryExNative(
        SafeProcessHandle process,
        nint address,
        out Win32MemoryBasicInformation buffer,
        nuint length);

    [DllImport("kernel32.dll", EntryPoint = "FlushInstructionCache", SetLastError = true, ExactSpelling = true)]
    private static extern bool FlushInstructionCacheNative(
        SafeProcessHandle process,
        nint baseAddress,
        nuint size);
}
