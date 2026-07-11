using Xunit;

namespace RayaTrainer.Tests;

public sealed class RepositoryValidationScriptTests
{
    [Fact]
    public void ValidationScriptCoversEveryBuildAndContractSurface()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "validate.ps1"));
        var solution = File.ReadAllText(Path.Combine(root, "RayaTrainer.sln"));
        var luaScript = File.ReadAllText(Path.Combine(root, "tools", "Ra3LuaConsole", "validate.ps1"));
        var luaSolution = File.ReadAllText(Path.Combine(root, "tools", "Ra3LuaConsole", "Ra3LuaConsole.sln"));

        Assert.Contains("RayaTrainer.sln", script, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.Smoke.csproj", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("Ra3LuaConsole.Injector.csproj", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("ModProtocolScanner", solution, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.Agent.vcxproj", script, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.Agent.Tests.vcxproj", script, StringComparison.Ordinal);
        Assert.Contains("Run Agent native tests", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Ra3LuaConsole.Dll.vcxproj", script, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.ApiGenerator", script, StringComparison.Ordinal);
        Assert.Contains("RayaTrainer.AddressLint", script, StringComparison.Ordinal);
        Assert.Contains("Assert-RepositoryLayout", script, StringComparison.Ordinal);

        Assert.Contains("Ra3LuaConsole.Injector.csproj", luaSolution, StringComparison.Ordinal);
        Assert.Contains("Ra3LuaConsole.ManagedTests.csproj", luaSolution, StringComparison.Ordinal);
        Assert.Contains("Ra3LuaConsole.Dll.vcxproj", luaScript, StringComparison.Ordinal);
        Assert.Contains("Ra3LuaConsole.ActionTests.vcxproj", luaScript, StringComparison.Ordinal);
        Assert.Contains("Ra3LuaConsole.RuntimeTests.vcxproj", luaScript, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicAllowlistContainsContractLint()
    {
        var root = RepositoryRoot();
        var allowlist = File.ReadAllText(Path.Combine(root, "scripts", "migrate-allowlist.txt"));
        Assert.Contains("tools/RayaTrainer.ContractLint/", allowlist, StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuousIntegrationUsesUnifiedValidationEntryPoint()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        Assert.Contains("./scripts/validate.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/validate.ps1", release, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTreeHashSuppressesCryptoReturnValues()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "migrate-to-public.ps1"));

        // Get-TreeHash must suppress TransformBlock/TransformFinalBlock return values
        // because PowerShell emits them into the output stream, corrupting the hashtable return.
        Assert.Contains("[void]$sha.TransformBlock(", script, StringComparison.Ordinal);
        Assert.Contains("[void]$sha.TransformFinalBlock(", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTreeHashExcludesVerifyReceiptFile()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "migrate-to-public.ps1"));

        // Get-TreeHash must exclude .verify-receipt.json so that Verify
        // (hashes before writing receipt) and Publish (hashes after receipt
        // exists) compute over the same file set.
        Assert.Contains("-ne '.verify-receipt.json'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AddressLintStepGuardedByRa3AnalysisPresence()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "validate.ps1"));

        // AddressLint requires RA3_Analysis/07_修改点索引/address_registry.md
        // which is private and not projected to the public repo.
        // The validate.ps1 must guard it behind a directory presence check.
        Assert.Contains("RA3_Analysis", script, StringComparison.Ordinal);
        Assert.Contains("Skipping AddressLint", script, StringComparison.Ordinal);
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
}
