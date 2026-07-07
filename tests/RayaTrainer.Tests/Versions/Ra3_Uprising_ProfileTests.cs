using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class Ra3_Uprising_ProfileTests
{
    [Theory]
    [InlineData("ra3_uprising_1.0")]
    [InlineData("ra3_uprising_1.1")]
    public void UprisingProfilesUseNativeHooksAndCatalog(string profileId)
    {
        var profile = Ra3VersionProfileRegistry.Profiles.Single(item => item.Id == profileId);
        var manifest = TrainerRuntimeAssets.LoadManifest();
        var expectedHooks = manifest.PatchManifest.Hooks
            .Where(hook => hook.SupportsProfile(profile.Id))
            .Select(hook => string.IsNullOrWhiteSpace(hook.ReturnLabel) ? hook.Address : hook.ReturnLabel)
            .Order(StringComparer.OrdinalIgnoreCase);

        Assert.True(profile.SupportsAgentBackend);
        Assert.True(profile.SupportsDirectGameApi);
        Assert.Equal(expectedHooks, profile.Hooks.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(NativeAgentCatalog.ExpectedEntryCount, profile.BuildNativeAgentCatalogRvas().Count);
    }
}
