using Microsoft.Win32.SafeHandles;

namespace RayaTrainer.Core.Memory;

internal interface IWin32MemoryApi
{
    int GetLastWin32Error();

    SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    bool ReadProcessMemory(SafeProcessHandle process, nint baseAddress, byte[] buffer, int size, out int bytesRead);

    bool WriteProcessMemory(SafeProcessHandle process, nint baseAddress, byte[] buffer, int size, out int bytesWritten);

    nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint allocationType, uint protect);

    bool VirtualProtectEx(SafeProcessHandle process, nint address, nuint size, uint newProtect, out uint oldProtect);

    nuint VirtualQueryEx(SafeProcessHandle process, nint address, out Win32MemoryBasicInformation buffer, nuint length);

    bool FlushInstructionCache(SafeProcessHandle process, nint baseAddress, nuint size);
}

internal struct Win32MemoryBasicInformation
{
    public nint BaseAddress;
    public nint AllocationBase;
    public uint AllocationProtect;
    public ushort PartitionId;
    public nuint RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
}
