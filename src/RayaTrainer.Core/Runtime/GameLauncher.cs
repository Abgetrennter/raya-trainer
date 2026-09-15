using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RayaTrainer.Core.Runtime;

public sealed class GameLauncher
{
    public Process Start(string launcherPath, string arguments = "", string? workingDirectory = null)
    {
        var startInfo = CreateStartInfo(launcherPath, arguments, workingDirectory);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start launcher process.");
    }

    public Process StartCommandLine(string commandLine, string workingDirectory)
    {
        var request = CreateRawProcessLaunchRequest(commandLine, workingDirectory);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Raw RA3 game launch is only supported on Windows.");
        }

        var startupInfo = new STARTUPINFOW { cb = Marshal.SizeOf<STARTUPINFOW>() };
        var commandLineBuffer = new StringBuilder(request.CommandLine);
        if (!CreateProcessW(
                null,
                commandLineBuffer,
                0,
                0,
                false,
                0,
                0,
                request.WorkingDirectory,
                ref startupInfo,
                out var processInformation))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to start game process.");
        }

        try
        {
            return Process.GetProcessById((int)processInformation.dwProcessId);
        }
        finally
        {
            CloseHandle(processInformation.hThread);
            CloseHandle(processInformation.hProcess);
        }
    }

    public static ProcessStartInfo CreateStartInfo(string launcherPath, string arguments = "", string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            throw new ArgumentException("Launcher path is required.", nameof(launcherPath));
        }

        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException("Launcher file was not found.", launcherPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = launcherPath,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(launcherPath) ?? Environment.CurrentDirectory
                : workingDirectory,
            UseShellExecute = true
        };

        return startInfo;
    }

    internal static RawProcessLaunchRequest CreateRawProcessLaunchRequest(string commandLine, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            throw new ArgumentException("Command line is required.", nameof(commandLine));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"Working directory was not found: {workingDirectory}");
        }

        return new RawProcessLaunchRequest(commandLine.Trim(), Path.GetFullPath(workingDirectory));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }
}

internal sealed record RawProcessLaunchRequest(string CommandLine, string WorkingDirectory);
