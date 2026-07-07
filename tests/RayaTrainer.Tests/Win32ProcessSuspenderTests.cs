using System.Reflection;
using System.Runtime.InteropServices;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class Win32ProcessSuspenderTests
{
    [Theory]
    [InlineData("OpenThread", "OpenThread")]
    [InlineData("SuspendThread", "SuspendThread")]
    [InlineData("ResumeThread", "ResumeThread")]
    public void NativeMethodsBindToExportedEntryPoints(string methodName, string expectedEntryPoint)
    {
        var method = typeof(Win32ProcessSuspender).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var dllImport = method.GetCustomAttribute<DllImportAttribute>();
        Assert.NotNull(dllImport);
        Assert.Equal("kernel32.dll", dllImport.Value);
        Assert.Equal(expectedEntryPoint, dllImport.EntryPoint);
    }
}
