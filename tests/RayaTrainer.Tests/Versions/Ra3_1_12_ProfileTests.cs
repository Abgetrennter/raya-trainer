using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class Ra3_1_12_ProfileTests
{
    [Fact]
    public void HooksMatchCurrentTrainerManifest()
    {
        var manifest = TrainerRuntimeAssets.LoadManifest();
        var profile = Ra3VersionProfileRegistry.Ra3112;
        var expected = manifest.PatchManifest.Hooks
            .Where(hook => hook.SupportsProfile(profile.Id))
            .Select(hook => string.IsNullOrWhiteSpace(hook.ReturnLabel) ? hook.Address : hook.ReturnLabel)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, profile.Hooks.Keys.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void NativeCatalogContainsAllRequiredEntries()
    {
        Assert.Equal(
            RayaTrainer.Core.Agent.NativeAgentCatalog.ExpectedEntryCount,
            Ra3VersionProfileRegistry.Ra3112.BuildNativeAgentCatalogRvas().Count);
    }
}
