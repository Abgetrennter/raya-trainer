using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace RayaTrainer.Core.Memory;

public sealed class Win32ProcessMemory : IProcessMemory, IDisposable
{
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint RequiredProcessAccess =
        ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation;
    private const uint PageExecuteReadWrite = 0x40;

    private readonly IWin32MemoryApi _api;
    private readonly SafeProcessHandle _handle;

    public Win32ProcessMemory(int processId)
        : this(processId, Kernel32MemoryApi.Instance)
    {
    }

    internal Win32ProcessMemory(int processId, IWin32MemoryApi api)
    {
        _api = api;
        _handle = _api.OpenProcess(RequiredProcessAccess, false, processId);
        if (_handle.IsInvalid)
        {
            throw CreateLastWin32Exception("OpenProcess failed.");
        }
    }

    public byte[] ReadBytes(nint address, int count)
    {
        var buffer = new byte[count];
        if (!_api.ReadProcessMemory(_handle, address, buffer, buffer.Length, out var read) || read != count)
        {
            throw CreateLastWin32Exception($"ReadProcessMemory failed at 0x{address:X}.");
        }
        return buffer;
    }

    public void WriteBytes(nint address, ReadOnlySpan<byte> bytes)
    {
        var buffer = bytes.ToArray();
        if (buffer.Length == 0)
        {
            return;
        }

        if (!_api.VirtualProtectEx(_handle, address, (nuint)buffer.Length, PageExecuteReadWrite, out var oldProtect))
        {
            throw CreateLastWin32Exception($"VirtualProtectEx failed at 0x{address:X}.");
        }

        try
        {
            if (!_api.WriteProcessMemory(_handle, address, buffer, buffer.Length, out var written) || written != buffer.Length)
            {
                throw CreateLastWin32Exception($"WriteProcessMemory failed at 0x{address:X}.");
            }

            if (!_api.FlushInstructionCache(_handle, address, (nuint)buffer.Length))
            {
                throw CreateLastWin32Exception($"FlushInstructionCache failed at 0x{address:X}.");
            }
        }
        finally
        {
            _api.VirtualProtectEx(_handle, address, (nuint)buffer.Length, oldProtect, out _);
        }
    }

    public Win32MemoryRegion? Query(nint address)
    {
        var size = (nuint)System.Runtime.InteropServices.Marshal.SizeOf<Win32MemoryBasicInformation>();
        var result = _api.VirtualQueryEx(_handle, address, out var info, size);
        if (result == 0)
        {
            return null;
        }

        return new Win32MemoryRegion(
            info.BaseAddress,
            info.RegionSize,
            info.AllocationProtect,
            info.State,
            info.Protect,
            info.Type);
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    private Win32Exception CreateLastWin32Exception(string message)
    {
        var error = _api.GetLastWin32Error();
        var region = QueryContext(message);
        return new Win32Exception(error, $"{message} Win32 error {error}: {new Win32Exception(error).Message}{region}");
    }

    private string QueryContext(string message)
    {
        var marker = message.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return string.Empty;
        }

        var end = marker + 2;
        while (end < message.Length && Uri.IsHexDigit(message[end]))
        {
            end++;
        }

        if (!nint.TryParse(message[(marker + 2)..end], System.Globalization.NumberStyles.HexNumber, null, out var address))
        {
            return string.Empty;
        }

        var region = Query(address);
        return region is null ? " Memory region: unavailable." : $" Memory region: {region}.";
    }
}
