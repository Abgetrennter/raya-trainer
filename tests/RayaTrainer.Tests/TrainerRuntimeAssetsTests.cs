using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerRuntimeAssetsTests
{
    [Fact]
    public void EmbeddedManifestContainsNativeHookPlan()
    {
        var manifest = TrainerRuntimeAssets.LoadManifest();

        Assert.NotEmpty(manifest.PatchManifest.Hooks);
        Assert.All(manifest.PatchManifest.Hooks, hook => Assert.False(string.IsNullOrWhiteSpace(hook.ReturnLabel)));
    }
}
