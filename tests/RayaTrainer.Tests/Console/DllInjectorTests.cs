namespace RayaTrainer.Tests.Console;

using Ra3LuaConsole.Injector;
using Xunit;

public class FakeInjectorApi : IAgentInjectorApi
{
    public List<string> CallSequence { get; } = new();
    public nint OpenProcessResult { get; set; } = (nint)1;
    public nint ModuleHandleResult { get; set; } = (nint)2;
    public nint ProcAddressResult { get; set; } = (nint)3;
    public nint AllocResult { get; set; } = (nint)0x10000;
    public bool WriteResult { get; set; } = true;
    public nint ThreadResult { get; set; } = (nint)4;
    public uint WaitResult { get; set; } = 0;
    public bool GetExitCodeResult { get; set; } = true;
    public uint ExitCode { get; set; } = 1;
    public nuint BytesWritten { get; set; } = 0;

    public nint OpenProcess(uint d, bool i, int pid) { CallSequence.Add($"OpenProcess({pid})"); return OpenProcessResult; }
    public nint GetModuleHandle(string m) { CallSequence.Add($"GetModuleHandle({m})"); return ModuleHandleResult; }
    public nint GetProcAddress(nint h, string p) { CallSequence.Add($"GetProcAddress({p})"); return ProcAddressResult; }
    public nint VirtualAllocEx(nint h, nuint s, uint a, uint p) { CallSequence.Add($"VirtualAllocEx({s})"); return AllocResult; }
    public bool VirtualFreeEx(nint h, nint a, nuint s, uint f) { CallSequence.Add("VirtualFreeEx"); return true; }
    public bool WriteProcessMemory(nint h, nint a, byte[] b, out nuint w) { CallSequence.Add($"WriteProcessMemory({b.Length})"); w = BytesWritten; return WriteResult; }
    public nint CreateRemoteThread(nint h, nint s, nint p, out uint tid) { CallSequence.Add("CreateRemoteThread"); tid = 99; return ThreadResult; }
    public uint WaitForSingleObject(nint h, uint ms) { CallSequence.Add("WaitForSingleObject"); return WaitResult; }
    public bool GetExitCodeThread(nint h, out uint c) { CallSequence.Add("GetExitCodeThread"); c = ExitCode; return GetExitCodeResult; }
    public bool CloseHandle(nint h) { CallSequence.Add($"CloseHandle({h})"); return true; }
    public int GetLastWin32Error() => 0;
}

public class DllInjectorTests
{
    [Fact]
    public void Inject_WithValidInputs_CallsApiInCorrectOrder()
    {
        var fake = new FakeInjectorApi();
        var injector = new DllInjector(fake);

        var result = injector.Inject(processId: 1234, dllPath: @"C:\test\my.dll");

        Assert.True(result.Success);
        Assert.Equal(1234, result.ProcessId);
        Assert.Contains("OpenProcess(1234)", fake.CallSequence);
        Assert.Contains("GetModuleHandle(kernel32.dll)", fake.CallSequence);
        Assert.Contains("GetProcAddress(LoadLibraryW)", fake.CallSequence);
        Assert.True(fake.CallSequence.Any(c => c.StartsWith("WriteProcessMemory")), "should write DLL path");
        Assert.Contains("CreateRemoteThread", fake.CallSequence);
        var closeCalls = fake.CallSequence.Where(c => c.StartsWith("CloseHandle")).ToList();
        Assert.True(closeCalls.Count >= 2, "should close process handle and thread handle");
    }

    [Fact]
    public void Inject_WhenWriteProcessMemoryFails_ReturnsFail()
    {
        var fake = new FakeInjectorApi { WriteResult = false };
        var injector = new DllInjector(fake);

        var result = injector.Inject(1234, @"C:\test\my.dll");

        Assert.False(result.Success);
        Assert.Contains("WriteProcessMemory", result.ErrorMessage!);
    }

    [Fact]
    public void Inject_WhenOpenProcessReturnsZero_ReturnsFail()
    {
        var fake = new FakeInjectorApi { OpenProcessResult = nint.Zero };
        var injector = new DllInjector(fake);

        var result = injector.Inject(1234, @"C:\test\my.dll");

        Assert.False(result.Success);
        Assert.Contains("OpenProcess", result.ErrorMessage!);
    }

    [Fact]
    public void Inject_WhenLoadLibraryReturnsZero_ReturnsFail()
    {
        var fake = new FakeInjectorApi { ExitCode = 0 };
        var injector = new DllInjector(fake);

        var result = injector.Inject(1234, @"C:\test\my.dll");

        Assert.False(result.Success);
        Assert.Contains("LoadLibraryW", result.ErrorMessage!);
    }
}
