using Microsoft.Win32.SafeHandles;
using RayaTrainer.Core.Memory;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class Win32ProcessMemoryTests
{
    [Theory]
    [InlineData("OpenProcessNative", "OpenProcess")]
    [InlineData("ReadProcessMemoryNative", "ReadProcessMemory")]
    [InlineData("WriteProcessMemoryNative", "WriteProcessMemory")]
    [InlineData("VirtualAllocExNative", "VirtualAllocEx")]
    [InlineData("VirtualProtectExNative", "VirtualProtectEx")]
    [InlineData("VirtualQueryExNative", "VirtualQueryEx")]
    [InlineData("FlushInstructionCacheNative", "FlushInstructionCache")]
    public void Kernel32NativeMethodsBindToExportedEntryPoints(string methodName, string expectedEntryPoint)
    {
        var method = typeof(Kernel32MemoryApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var dllImport = method.GetCustomAttribute<DllImportAttribute>();
        Assert.NotNull(dllImport);
        Assert.Equal("kernel32.dll", dllImport.Value);
        Assert.Equal(expectedEntryPoint, dllImport.EntryPoint);
    }

    [Fact]
    public void WriteBytesTemporarilyEnablesExecuteReadWriteAndRestoresProtection()
    {
        var api = new FakeWin32MemoryApi();
        using var memory = new Win32ProcessMemory(1234, api);

        memory.WriteBytes(0x6E24E3, new byte[] { 0xE9, 0x01, 0x02, 0x03, 0x04 });

        Assert.Equal(
            [
                "OpenProcess 1234 0x438",
                "Protect 0x6E24E3 5 0x40",
                "Write 0x6E24E3 E9-01-02-03-04",
                "Flush 0x6E24E3 5",
                "Protect 0x6E24E3 5 0x20"
            ],
            api.Calls);
    }

    [Fact]
    public void WriteBytesReportsNativeErrorCodeWhenVirtualProtectFails()
    {
        var api = new FakeWin32MemoryApi { FailVirtualProtect = true };
        using var memory = new Win32ProcessMemory(1234, api);

        var exception = Assert.Throws<Win32Exception>(
            () => memory.WriteBytes(0x6E24E3, new byte[] { 0xE9 }));

        Assert.Equal(5, exception.NativeErrorCode);
        Assert.Contains("VirtualProtectEx failed at 0x6E24E3", exception.Message);
        Assert.Contains("Win32 error 5", exception.Message);
        Assert.Contains("Memory region: base=0x6E2000", exception.Message);
    }

    private sealed class FakeWin32MemoryApi : IWin32MemoryApi
    {
        private int _protectCallCount;

        public List<string> Calls { get; } = [];

        public bool FailVirtualProtect { get; init; }

        public int GetLastWin32Error() => 5;

        public SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId)
        {
            Calls.Add($"OpenProcess {processId} 0x{desiredAccess:X}");
            return new SafeProcessHandle(0x1234, ownsHandle: false);
        }

        public bool ReadProcessMemory(SafeProcessHandle process, nint baseAddress, byte[] buffer, int size, out int bytesRead)
        {
            bytesRead = size;
            return true;
        }

        public bool WriteProcessMemory(SafeProcessHandle process, nint baseAddress, byte[] buffer, int size, out int bytesWritten)
        {
            bytesWritten = size;
            Calls.Add($"Write 0x{baseAddress:X} {BitConverter.ToString(buffer)}");
            return true;
        }

        public nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint allocationType, uint protect) => 0x20000000;

        public bool VirtualProtectEx(SafeProcessHandle process, nint address, nuint size, uint newProtect, out uint oldProtect)
        {
            Calls.Add($"Protect 0x{address:X} {size} 0x{newProtect:X}");
            oldProtect = _protectCallCount++ == 0 ? 0x20u : 0x40u;
            return !FailVirtualProtect;
        }

        public nuint VirtualQueryEx(SafeProcessHandle process, nint address, out Win32MemoryBasicInformation buffer, nuint length)
        {
            buffer = new Win32MemoryBasicInformation
            {
                BaseAddress = 0x6E2000,
                RegionSize = 0x1000,
                AllocationProtect = 0x20,
                State = 0x1000,
                Protect = 0x20,
                Type = 0x1000000
            };
            return length;
        }

        public bool FlushInstructionCache(SafeProcessHandle process, nint baseAddress, nuint size)
        {
            Calls.Add($"Flush 0x{baseAddress:X} {size}");
            return true;
        }
    }
}
