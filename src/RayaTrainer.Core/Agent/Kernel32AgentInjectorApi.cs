using System.Runtime.InteropServices;

namespace RayaTrainer.Core.Agent;

public sealed class Kernel32AgentInjectorApi : IAgentInjectorApi
{
    public static readonly Kernel32AgentInjectorApi Instance = new();

    private Kernel32AgentInjectorApi()
    {
    }

    public nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId)
    {
        return OpenProcessNative(desiredAccess, inheritHandle, processId);
    }

    public nint GetModuleHandle(string moduleName)
    {
        return GetModuleHandleNative(moduleName);
    }

    public nint GetProcAddress(nint moduleHandle, string procName)
    {
        return GetProcAddressNative(moduleHandle, procName);
    }

    public nint VirtualAllocEx(nint processHandle, nuint size, uint allocationType, uint protect)
    {
        return VirtualAllocExNative(processHandle, 0, size, allocationType, protect);
    }

    public bool VirtualFreeEx(nint processHandle, nint address, nuint size, uint freeType)
    {
        return VirtualFreeExNative(processHandle, address, size, freeType);
    }

    public bool WriteProcessMemory(nint processHandle, nint address, byte[] buffer, out nuint bytesWritten)
    {
        return WriteProcessMemoryNative(processHandle, address, buffer, (nuint)buffer.Length, out bytesWritten);
    }

    public nint CreateRemoteThread(nint processHandle, nint startAddress, nint parameter, out uint threadId)
    {
        return CreateRemoteThreadNative(processHandle, 0, 0, startAddress, parameter, 0, out threadId);
    }

    public uint WaitForSingleObject(nint handle, uint milliseconds)
    {
        return WaitForSingleObjectNative(handle, milliseconds);
    }

    public bool GetExitCodeThread(nint threadHandle, out uint exitCode)
    {
        return GetExitCodeThreadNative(threadHandle, out exitCode);
    }

    public bool CloseHandle(nint handle)
    {
        return CloseHandleNative(handle);
    }

    public int GetLastWin32Error()
    {
        return Marshal.GetLastWin32Error();
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern nint OpenProcessNative(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleNative(string moduleName);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddressNative(nint moduleHandle, string procName);

    [DllImport("kernel32.dll", EntryPoint = "VirtualAllocEx", SetLastError = true)]
    private static extern nint VirtualAllocExNative(
        nint processHandle,
        nint address,
        nuint size,
        uint allocationType,
        uint protect);

    [DllImport("kernel32.dll", EntryPoint = "VirtualFreeEx", SetLastError = true)]
    private static extern bool VirtualFreeExNative(
        nint processHandle,
        nint address,
        nuint size,
        uint freeType);

    [DllImport("kernel32.dll", EntryPoint = "WriteProcessMemory", SetLastError = true)]
    private static extern bool WriteProcessMemoryNative(
        nint processHandle,
        nint address,
        byte[] buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", EntryPoint = "CreateRemoteThread", SetLastError = true)]
    private static extern nint CreateRemoteThreadNative(
        nint processHandle,
        nint threadAttributes,
        nuint stackSize,
        nint startAddress,
        nint parameter,
        uint creationFlags,
        out uint threadId);

    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
    private static extern uint WaitForSingleObjectNative(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", EntryPoint = "GetExitCodeThread", SetLastError = true)]
    private static extern bool GetExitCodeThreadNative(nint threadHandle, out uint exitCode);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseHandleNative(nint handle);
}
