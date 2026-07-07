using System.Text;
using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentInjectorTests
{
    [Fact]
    public void InjectWritesAbsoluteDllPathAndStartsLoadLibraryThread()
    {
        var dllPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(dllPath, [0x4D, 0x5A]);
        var api = new FakeAgentInjectorApi();
        var injector = new AgentInjector(api);

        try
        {
            var result = injector.Inject(1234, dllPath, TimeSpan.FromSeconds(2));

            Assert.True(result.Success);
            Assert.Equal((nint)0x5000, result.RemoteModuleHandle);
            Assert.Equal(1234, api.OpenedProcessId);
            Assert.Equal("kernel32.dll", api.ModuleName);
            Assert.Equal("LoadLibraryW", api.ProcName);
            Assert.Equal((nint)0x30, api.ThreadStartAddress);
            Assert.Equal((nint)0x7000, api.ThreadParameter);
            Assert.Equal((nint)0x7000, api.FreedAddress);
            Assert.Contains((nint)0x10, api.ClosedHandles);
            Assert.Contains((nint)0x40, api.ClosedHandles);

            var writtenPath = Encoding.Unicode.GetString(api.WrittenBytes);
            Assert.Equal(Path.GetFullPath(dllPath) + '\0', writtenPath);
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    [Fact]
    public void InjectRejectsMissingDll()
    {
        var injector = new AgentInjector(new FakeAgentInjectorApi());

        var ex = Assert.Throws<FileNotFoundException>(() =>
            injector.Inject(1234, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll"), TimeSpan.FromSeconds(2)));

        Assert.Contains("Agent DLL", ex.Message);
    }

    private sealed class FakeAgentInjectorApi : IAgentInjectorApi
    {
        public int OpenedProcessId { get; private set; }
        public string? ModuleName { get; private set; }
        public string? ProcName { get; private set; }
        public byte[] WrittenBytes { get; private set; } = [];
        public nint ThreadStartAddress { get; private set; }
        public nint ThreadParameter { get; private set; }
        public nint FreedAddress { get; private set; }
        public List<nint> ClosedHandles { get; } = [];

        public nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId)
        {
            OpenedProcessId = processId;
            return 0x10;
        }

        public nint GetModuleHandle(string moduleName)
        {
            ModuleName = moduleName;
            return 0x20;
        }

        public nint GetProcAddress(nint moduleHandle, string procName)
        {
            ProcName = procName;
            return 0x30;
        }

        public nint VirtualAllocEx(nint processHandle, nuint size, uint allocationType, uint protect)
        {
            return 0x7000;
        }

        public bool VirtualFreeEx(nint processHandle, nint address, nuint size, uint freeType)
        {
            FreedAddress = address;
            return true;
        }

        public bool WriteProcessMemory(nint processHandle, nint address, byte[] buffer, out nuint bytesWritten)
        {
            WrittenBytes = buffer;
            bytesWritten = (nuint)buffer.Length;
            return true;
        }

        public nint CreateRemoteThread(nint processHandle, nint startAddress, nint parameter, out uint threadId)
        {
            ThreadStartAddress = startAddress;
            ThreadParameter = parameter;
            threadId = 456;
            return 0x40;
        }

        public uint WaitForSingleObject(nint handle, uint milliseconds)
        {
            return 0;
        }

        public bool GetExitCodeThread(nint threadHandle, out uint exitCode)
        {
            exitCode = 0x5000;
            return true;
        }

        public bool CloseHandle(nint handle)
        {
            ClosedHandles.Add(handle);
            return true;
        }

        public int GetLastWin32Error()
        {
            return 5;
        }
    }
}
