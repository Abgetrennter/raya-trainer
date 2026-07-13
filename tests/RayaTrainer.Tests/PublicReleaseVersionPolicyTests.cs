using System.Diagnostics;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class PublicReleaseVersionPolicyTests
{
    [Theory]
    [InlineData("v0.0.3", "v0.0.2", true)]
    [InlineData("v0.1.0", "v0.0.2", true)]
    [InlineData("v0.0.4", "v0.0.2", false)]
    [InlineData("v0.2.0", "v0.0.2", false)]
    [InlineData("v1.0.0", "v0.0.2", false)]
    [InlineData("0.0.3", "v0.0.2", false)]
    [InlineData("v0.0.3-beta", "v0.0.2", false)]
    public void PolicyAllowsOnlyNextPatchOrNextMinor(string candidate, string previous, bool expectedSuccess)
    {
        var result = RunPolicy(candidate, previous);

        Assert.Equal(expectedSuccess, result.ExitCode == 0);
    }

    [Theory]
    [InlineData("v0.1.0", true)]
    [InlineData("v0.0.1", false)]
    [InlineData("v1.0.0", false)]
    public void FirstPublicReleaseStartsAtV010(string candidate, bool expectedSuccess)
    {
        var result = RunPolicy(candidate, string.Empty);

        Assert.Equal(expectedSuccess, result.ExitCode == 0);
    }

    private static ProcessResult RunPolicy(string candidate, string previous)
    {
        var root = RepositoryRoot();
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
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(root, "scripts", "release-version-policy.ps1"));
        startInfo.ArgumentList.Add("-CandidateTag");
        startInfo.ArgumentList.Add(candidate);
        startInfo.ArgumentList.Add("-PreviousTag");
        startInfo.ArgumentList.Add(previous);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output + error);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static string FindPowerShellExecutable() => "pwsh";

    private sealed record ProcessResult(int ExitCode, string Output);
}
