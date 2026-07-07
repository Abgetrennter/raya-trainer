namespace RayaTrainer.Core.Agent;

public interface IAgentInjectorApi
{
    nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);
    nint GetModuleHandle(string moduleName);
    nint GetProcAddress(nint moduleHandle, string procName);
    nint VirtualAllocEx(nint processHandle, nuint size, uint allocationType, uint protect);
    bool VirtualFreeEx(nint processHandle, nint address, nuint size, uint freeType);
    bool WriteProcessMemory(nint processHandle, nint address, byte[] buffer, out nuint bytesWritten);
    nint CreateRemoteThread(nint processHandle, nint startAddress, nint parameter, out uint threadId);
    uint WaitForSingleObject(nint handle, uint milliseconds);
    bool GetExitCodeThread(nint threadHandle, out uint exitCode);
    bool CloseHandle(nint handle);
    int GetLastWin32Error();
}
