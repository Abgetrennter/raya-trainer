using Xunit;

namespace RayaTrainer.Tests;

public sealed class SmokeToolTests
{
    [Fact]
    public void SmokeToolExposesDllAgentProbeOptions()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "RayaTrainer.Smoke", "Program.cs"));

        Assert.Contains("--agent-probe", source, StringComparison.Ordinal);
        Assert.Contains("--agent-dll", source, StringComparison.Ordinal);
        Assert.Contains("ProbeAgentAsync", source, StringComparison.Ordinal);
        Assert.Contains("AgentInjector", source, StringComparison.Ordinal);
        Assert.Contains("AgentNamedPipeClient", source, StringComparison.Ordinal);
        Assert.Contains("GetMismatchDiagnosticsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeToolExposesDllAgentFeatureScanOptions()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "RayaTrainer.Smoke", "Program.cs"));

        Assert.Contains("--agent-feature-scan", source, StringComparison.Ordinal);
        Assert.Contains("ProbeAgentFeaturesAsync", source, StringComparison.Ordinal);
        Assert.Contains("AgentFeatureController", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeToolExposesDirectGameApiGetThingClassSmokeOnly()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "RayaTrainer.Smoke", "Program.cs"));

        Assert.Contains("--agent-game-api-smoke", source, StringComparison.Ordinal);
        Assert.Contains("SmokeGetThingClassAsync", source, StringComparison.Ordinal);
        Assert.Contains("ReadSelectedUnitSnapshotViaGameApiAsync", source, StringComparison.Ordinal);
        Assert.Contains("NoGameTick", source, StringComparison.Ordinal);
        Assert.Contains("gameApiCommandTimeout = TimeSpan.FromSeconds(8)", source, StringComparison.Ordinal);
        Assert.Contains("TimeoutMilliseconds: 5000", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SmokeCreateUnit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SmokeLevelUp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SmokeKillUnit", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeToolExposesTargetedRuntimeFeatureSmoke()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "RayaTrainer.Smoke", "Program.cs"));

        Assert.Contains("--agent-runtime-feature-smoke", source, StringComparison.Ordinal);
        Assert.Contains("SecretProtocolBindingProbeAsync", source, StringComparison.Ordinal);
        Assert.Contains("CycleGameToBackgroundAsync", source, StringComparison.Ordinal);
        Assert.Contains("Run In Background", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeToolUsesRuntimeManifestAndTargetProfiles()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "RayaTrainer.Smoke", "Program.cs"));

        Assert.Contains("TrainerRuntimeAssets.LoadManifest()", source, StringComparison.Ordinal);
        Assert.Contains("ResolveTargetProfile(target)", source, StringComparison.Ordinal);
        Assert.Contains("PatchHookInspector.Capture(manifest.PatchManifest, memory, resolver, profile)", source, StringComparison.Ordinal);
        Assert.Contains("PatchHookInspector.CaptureResolved", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TrainerManifestRepository.Load(options.AnalysisDirectory)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadBootstrapAssemblyLines", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
