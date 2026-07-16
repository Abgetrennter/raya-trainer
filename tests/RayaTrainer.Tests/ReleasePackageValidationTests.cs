using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ReleasePackageValidationTests
{
    [Fact]
    public void ValidateReleasePackageRejectsUserSettingsAndUndistributableEntries()
    {
        var directory = CreatePackageDirectory();
        var zipPath = Path.Combine(directory, "bad-release.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "RayaTrainer.App.exe", "binary placeholder");
            AddEntry(archive, "RayaTrainer.settings.json", """
                {
                  "LauncherPath": "E:\\Game\\rad alter 3_Small\\RA3.exe"
                }
                """);
            archive.CreateEntry("analysis/");
            AddEntry(archive, "analysis/scripts/bootstrap.asm", "analysis payload");
            AddEntry(archive, "Trainer.exe", "legacy trainer");
            AddEntry(archive, "RedAlert3_Uprising_Trainer_FINAL.exe", "legacy Uprising trainer");
        }

        var result = RunValidator(zipPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("RayaTrainer.settings.json", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("analysis/", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trainer.exe", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RedAlert3_Uprising_Trainer_FINAL.exe", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local Windows path", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateReleasePackageAcceptsCleanPublishedFiles()
    {
        var directory = CreatePackageDirectory();
        var zipPath = Path.Combine(directory, "clean-release.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "RayaTrainer.App.exe", "binary placeholder");
            AddEntry(archive, "RayaTrainer.App.runtimeconfig.json", "{}");
            AddEntry(archive, "RayaTrainer.Agent.dll", CreateMinimalPe(machine: 0x014C, isDll: true));
        }

        var result = RunValidator(zipPath);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ValidateReleasePackageRejectsNonX86AgentDll()
    {
        var directory = CreatePackageDirectory();
        var zipPath = Path.Combine(directory, "x64-agent.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "RayaTrainer.App.exe", "binary placeholder");
            AddEntry(archive, "RayaTrainer.App.runtimeconfig.json", "{}");
            AddEntry(archive, "RayaTrainer.Agent.dll", CreateMinimalPe(machine: 0x8664, isDll: true));
        }

        var result = RunValidator(zipPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("x86", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateReleasePackageRejectsAgentDllWithBlockedNativeDependency()
    {
        var directory = CreatePackageDirectory();
        var zipPath = Path.Combine(directory, "agent-with-runtime-dependency.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "RayaTrainer.App.exe", "binary placeholder");
            AddEntry(archive, "RayaTrainer.App.runtimeconfig.json", "{}");
            AddEntry(
                archive,
                "RayaTrainer.Agent.dll",
                CreateMinimalPe(machine: 0x014C, isDll: true, "KERNEL32.dll", "VCRUNTIME140.dll"));
        }

        var result = RunValidator(zipPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("VCRUNTIME140.dll", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateReleasePackageRejectsMissingAgentDll()
    {
        var directory = CreatePackageDirectory();
        var zipPath = Path.Combine(directory, "missing-agent.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "RayaTrainer.App.exe", "binary placeholder");
            AddEntry(archive, "RayaTrainer.App.runtimeconfig.json", "{}");
        }

        var result = RunValidator(zipPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("RayaTrainer.Agent.dll", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishScriptsCopyNativeAgentDllIntoOutput()
    {
        var repoRoot = FindRepoRoot();
        var common = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-common.ps1"));
        var frameworkDependent = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-framework-dependent.ps1"));
        var selfContained = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-self-contained.ps1"));

        Assert.Contains("function Copy-AgentDll", common, StringComparison.Ordinal);
        Assert.Contains("Copy-AgentDll", frameworkDependent, StringComparison.Ordinal);
        Assert.Contains("Copy-AgentDll", selfContained, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryDocsKeepClosureStatementAndRuntimeContract()
    {
        var repoRoot = FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));

        Assert.Contains("绝对不支持联机 / 多人模式。", readme, StringComparison.Ordinal);

        // CLAUDE.md is private agent configuration and is excluded from the public projection;
        // only assert its runtime contract when the file is actually present.
        var claudePath = Path.Combine(repoRoot, "CLAUDE.md");
        if (File.Exists(claudePath))
        {
            var claude = File.ReadAllText(claudePath);
            Assert.Contains("Desktop Runtime", claude, StringComparison.Ordinal);
            Assert.Contains("ASP.NET Core Runtime", claude, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PublishScriptsSupportCustomOutputRootForNonDestructiveSmoke()
    {
        var repoRoot = FindRepoRoot();
        var common = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-common.ps1"));
        var frameworkDependent = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-framework-dependent.ps1"));
        var selfContained = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-self-contained.ps1"));

        Assert.Contains("[string]$OutputRoot", frameworkDependent, StringComparison.Ordinal);
        Assert.Contains("[string]$OutputRoot", selfContained, StringComparison.Ordinal);
        Assert.Contains("[string]$PublishRoot", common, StringComparison.Ordinal);
        Assert.Contains("-PublishRoot $OutputRoot", frameworkDependent, StringComparison.Ordinal);
        Assert.Contains("-PublishRoot $OutputRoot", selfContained, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishScriptsInjectApplicationVersionMetadata()
    {
        var repoRoot = FindRepoRoot();
        var common = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-common.ps1"));
        var publish = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish.ps1"));
        var frameworkDependent = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-framework-dependent.ps1"));
        var selfContained = File.ReadAllText(Path.Combine(repoRoot, "scripts", "publish-self-contained.ps1"));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("function Get-BuildVersionMetadata", common, StringComparison.Ordinal);
        Assert.Contains("-p:Version=$($buildVersion.PackageVersion)", publish, StringComparison.Ordinal);
        Assert.Contains("-p:InformationalVersion=$($buildVersion.InformationalVersion)", publish, StringComparison.Ordinal);
        Assert.Contains("Get-BuildVersionMetadata", frameworkDependent, StringComparison.Ordinal);
        Assert.Contains("Get-BuildVersionMetadata", selfContained, StringComparison.Ordinal);
        Assert.Contains("-p:InformationalVersion=$($buildVersion.InformationalVersion)", frameworkDependent, StringComparison.Ordinal);
        Assert.Contains("-p:InformationalVersion=$($buildVersion.InformationalVersion)", selfContained, StringComparison.Ordinal);
        Assert.Contains("-BuildTag", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.release_tag.outputs.tag", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreProjectPublishesCoronaImportTables()
    {
        var repoRoot = FindRepoRoot();
        var project = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Core", "RayaTrainer.Core.csproj"));

        // Corona data now ships as an asset pack under Assets/Catalogs/Corona/ (refactor Plan 03 Task 3).
        // All three pack files are <None> + CopyToOutputDirectory so AssetPackLoader reads them from disk.
        Assert.Contains("Assets\\Catalogs\\Corona\\pack.json", project, StringComparison.Ordinal);
        Assert.Contains("Assets\\Catalogs\\Corona\\secret-protocols.txt", project, StringComparison.Ordinal);
        Assert.Contains("Assets\\Catalogs\\Corona\\reinforcements.txt", project, StringComparison.Ordinal);
        // Legacy flat paths must be gone.
        Assert.DoesNotContain("Assets\\Corona\\", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportTables\\Corona\\", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\corona-secret-protocols.txt", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\corona-reinforcements.txt", project, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreProjectReferenceResourcesAreTrackedForCleanReleaseBuilds()
    {
        var repoRoot = FindRepoRoot();
        var project = File.ReadAllText(Path.Combine(repoRoot, "src", "RayaTrainer.Core", "RayaTrainer.Core.csproj"));

        // Vendor reference docs (01_ModelState.md, 02_ObjectStatus.md) have been replaced
        // by pre-parsed JSON asset packs under Assets/Catalogs/ReferenceNotes/.
        Assert.Contains("Assets\\Catalogs\\ReferenceNotes\\pack.json", project, StringComparison.Ordinal);
        Assert.Contains("Assets\\Catalogs\\ReferenceNotes\\model-state-notes.json", project, StringComparison.Ordinal);
        Assert.Contains("Assets\\Catalogs\\ReferenceNotes\\object-status-notes.json", project, StringComparison.Ordinal);
        // The raw vendor MD files are no longer embedded resources but still tracked in the repo.
        // vendor/ is excluded from the public projection and the export tree has no .git index,
        // so only assert git-tracking when an actual repository index is present.
        if (Directory.Exists(Path.Combine(repoRoot, ".git")) || File.Exists(Path.Combine(repoRoot, ".git")))
        {
            AssertTracked(repoRoot, "vendor/RA3_Engine_Reference/01_ModelState.md");
            AssertTracked(repoRoot, "vendor/RA3_Engine_Reference/02_ObjectStatus.md");
        }
    }

    [Fact]
    public void ClearPublishOutputDirectoryRejectsPathsOutsideCustomPublishRoot()
    {
        var repoRoot = FindRepoRoot();
        var smokeRoot = Path.Combine(Path.GetTempPath(), "RayaTrainerPublishSmoke", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "RayaTrainerPublishOutside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeRoot);
        Directory.CreateDirectory(outsideRoot);
        var allowedOutput = Path.Combine(smokeRoot, "framework");
        var forbiddenOutput = Path.Combine(outsideRoot, "framework");
        Directory.CreateDirectory(allowedOutput);

        var command = string.Join(
            "; ",
            [
                "$ErrorActionPreference='Stop'",
                $". '{Path.Combine(repoRoot, "scripts", "publish-common.ps1").Replace("'", "''")}'",
                $"Clear-PublishOutputDirectory -RepoRoot '{repoRoot.Replace("'", "''")}' -PublishRoot '{smokeRoot.Replace("'", "''")}' -OutputPath '{allowedOutput.Replace("'", "''")}'",
                $"try {{ Clear-PublishOutputDirectory -RepoRoot '{repoRoot.Replace("'", "''")}' -PublishRoot '{smokeRoot.Replace("'", "''")}' -OutputPath '{forbiddenOutput.Replace("'", "''")}' }} catch {{ Write-Output $_.Exception.Message; exit 23 }}",
                "exit 1"
            ]);

        var result = RunPowerShellCommand(command);

        Assert.Equal(23, result.ExitCode);
        Assert.Contains("outside publish root", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(allowedOutput));
        Assert.True(Directory.Exists(outsideRoot));
    }

    private static string CreatePackageDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RayaTrainerReleaseValidation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] CreateMinimalPe(ushort machine, bool isDll, params string[] asciiMarkers)
    {
        const int peHeaderOffset = 0x80;
        var markerBytes = asciiMarkers
            .SelectMany(marker => Encoding.ASCII.GetBytes(marker + "\0"))
            .ToArray();
        var bytes = new byte[0x100 + markerBytes.Length];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(peHeaderOffset).CopyTo(bytes, 0x3C);
        bytes[peHeaderOffset] = (byte)'P';
        bytes[peHeaderOffset + 1] = (byte)'E';
        bytes[peHeaderOffset + 2] = 0;
        bytes[peHeaderOffset + 3] = 0;
        BitConverter.GetBytes(machine).CopyTo(bytes, peHeaderOffset + 4);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, peHeaderOffset + 6);
        BitConverter.GetBytes((ushort)0xE0).CopyTo(bytes, peHeaderOffset + 20);
        var characteristics = isDll ? (ushort)0x2000 : (ushort)0;
        BitConverter.GetBytes(characteristics).CopyTo(bytes, peHeaderOffset + 22);
        markerBytes.CopyTo(bytes, 0x100);
        return bytes;
    }

    private static CommandResult RunValidator(string zipPath)
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "validate-release-package.ps1");
        return RunPowerShellCommand(
            [
                "-File",
                scriptPath,
                "-ZipPath",
                zipPath
            ]);
    }

    private static CommandResult RunPowerShellCommand(string command)
    {
        return RunPowerShellCommand(
            [
                "-Command",
                command
            ]);
    }

    private static CommandResult RunPowerShellCommand(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindPowerShellExecutable(),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, output + error);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string FindPowerShellExecutable()
    {
        return OperatingSystem.IsWindows() && CommandExists("pwsh")
            ? "pwsh"
            : "powershell";
    }

    private static bool CommandExists(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                if (File.Exists(Path.Combine(directory, fileName + extension)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AssertTracked(string repoRoot, string relativePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("--error-unmatch");
        startInfo.ArgumentList.Add(relativePath);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Expected {relativePath} to be tracked for clean release builds. git output: {output}{error}");
    }

    private sealed record CommandResult(int ExitCode, string Output);
}
