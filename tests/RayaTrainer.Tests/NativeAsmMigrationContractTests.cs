using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class NativeAsmMigrationContractTests
{
    [Theory]
    [InlineData("ra3_1.12", 0x50u, 0x54u, 0x310u, 0x1360u, 1u)]
    [InlineData("ra3_1.13", 0x50u, 0x54u, 0x310u, 0x1360u, 1u)]
    [InlineData("ra3_uprising_1.0", 0x54u, 0x54u, 0x320u, 0u, 2u)]
    [InlineData("ra3_uprising_1.1", 0x54u, 0x54u, 0x320u, 0u, 2u)]
    public void ProfileCatalogPreservesLegacyAsmStructuralDifferences(
        string profileId,
        uint selectionHead,
        uint destroySelectionHead,
        uint productionModules,
        uint localContextSibling,
        uint restoreOreMode)
    {
        var profile = Ra3VersionProfileRegistry.Profiles.Single(item => item.Id == profileId);
        var values = profile.BuildNativeAgentCatalogRvas();

        Assert.Equal(selectionHead, Entry(values, "SelectionListHeadOffset"));
        Assert.Equal(destroySelectionHead, Entry(values, "DestroySelectionListHeadOffset"));
        Assert.Equal(productionModules, Entry(values, "ProductionModulesOffset"));
        Assert.Equal(localContextSibling, Entry(values, "LocalContextSiblingOffset"));
        Assert.Equal(restoreOreMode, Entry(values, "RestoreOreCapacityMode"));
    }

    [Fact]
    public void NativeHandlersKeepDistinctLegacyPointerChains()
    {
        var root = RepositoryRoot();
        var gameApi = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.Agent", "AgentGameApi.cpp"));
        var hooks = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.Agent", "AgentNativeHooks.cpp"));

        Assert.Contains("uint32_t FindFirstSelectedComponent()", gameApi, StringComparison.Ordinal);
        Assert.Contains("const auto component = FindFirstSelectedObjectComponent();", gameApi, StringComparison.Ordinal);
        Assert.Contains("SafeReadU32(object + 0x138u, component)", gameApi, StringComparison.Ordinal);
        Assert.Contains("SafeReadStructureU32(component, NativeCatalogEntry::ProductionModulesOffset", gameApi, StringComparison.Ordinal);

        // The legacy clear action indexes the GameClient lock table with GameClient+0x28,
        // then separately resolves the current player for the science containers.
        Assert.Contains("SafeReadU32(gameClient + 0x84u, lockWords)", gameApi, StringComparison.Ordinal);
        Assert.Contains("SafeReadU32(gameClient + 0x28u, indexedPlayer)", gameApi, StringComparison.Ordinal);
        Assert.Contains("SafeReadU32(indexedPlayer + 0x20u, playerIndex)", gameApi, StringComparison.Ordinal);

        Assert.Contains("NativeCatalogEntry::LocalContextSiblingOffset", hooks, StringComparison.Ordinal);
        Assert.Contains("NativeCatalogEntry::RestoreOreCapacityMode", hooks, StringComparison.Ordinal);
        Assert.Contains("ResetNativeGameApiRuntimeState();", hooks, StringComparison.Ordinal);
    }

    private static uint Entry(IReadOnlyList<uint> values, string name)
    {
        var index = NativeAgentCatalog.EntryNames
            .Select((entry, index) => (entry, index))
            .Where(item => string.Equals(item.entry, name, StringComparison.Ordinal))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Single();
        Assert.True(index >= 0, $"Missing native catalog entry: {name}");
        return values[index];
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
