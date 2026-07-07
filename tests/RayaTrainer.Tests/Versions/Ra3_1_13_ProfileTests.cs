using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class Ra3_1_13_ProfileTests
{
    [Fact]
    public void ProfileUsesNativeHooksAndCatalog()
    {
        var profile = Ra3VersionProfileRegistry.Ra3113;
        var manifest = TrainerRuntimeAssets.LoadManifest();
        var expectedHooks = manifest.PatchManifest.Hooks
            .Where(hook => hook.SupportsProfile(profile.Id))
            .Select(hook => string.IsNullOrWhiteSpace(hook.ReturnLabel) ? hook.Address : hook.ReturnLabel)
            .Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expectedHooks, profile.Hooks.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(NativeAgentCatalog.ExpectedEntryCount, profile.BuildNativeAgentCatalogRvas().Count);
    }
}
