using RayaTrainer.Core.Patching;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ManifestRepositoryTests
{
    [Fact]
    public void LoadReadsParsedTrainerArtifacts()
    {
        var manifest = TestAssets.LoadManifest();

        Assert.Equal("ra3_1.12.game", manifest.TargetProcess);
        Assert.Equal(34, manifest.Features.Count);
        Assert.Equal(TestAssets.CurrentManifestHookCount, manifest.PatchManifest.Hooks.Count);
        Assert.Empty(manifest.ActionDispatch);

        var money = Assert.Single(manifest.Features, feature => feature.RawName == "Moeny");
        Assert.Equal("Money", money.DisplayName);
        Assert.Equal("Ctrl+F1", money.Hotkey);
        Assert.Equal(new[] { "Moeny" }, money.EnableFlags);

        var destroy = Assert.Single(manifest.Features, feature => feature.RawName == "Destory Select Unit");
        Assert.Equal("Destroy Select Unit", destroy.DisplayName);
        Assert.Null(destroy.DispatchTarget);

        Assert.Equal(
            Array.Empty<string>(),
            Assert.Single(manifest.Features, feature => feature.RawName == "Danger Level MIN").EnableFlags);
        Assert.Equal(
            Array.Empty<string>(),
            Assert.Single(manifest.Features, feature => feature.RawName == "Restore Select Ore Mine").EnableFlags);
    }

    [Fact]
    public void LoadIncludesUprisingOnlyChallengeFeaturesAndHooks()
    {
        var manifest = TestAssets.LoadManifest();
        var challengeTime = Assert.Single(manifest.Features, feature => feature.RawName == "Challenge Time");
        var challengeMoney = Assert.Single(manifest.Features, feature => feature.RawName == "Challenge Money");
        var hooks = manifest.PatchManifest.Hooks
            .Where(hook => hook.SectionTitle.StartsWith("Uprising Challenge", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(new[] { "Challenge Time" }, challengeTime.EnableFlags);
        Assert.Equal(new[] { "Challenge Money" }, challengeMoney.EnableFlags);
        Assert.True(challengeTime.SupportsProfile("ra3_uprising_1.0"));
        Assert.True(challengeTime.SupportsProfile("ra3_uprising_1.1"));
        Assert.False(challengeTime.SupportsProfile("ra3_1.12"));
        Assert.Equal(2, hooks.Length);
        Assert.All(hooks, hook =>
        {
            Assert.True(hook.SupportsProfile("ra3_uprising_1.0"));
            Assert.True(hook.SupportsProfile("ra3_uprising_1.1"));
            Assert.False(hook.SupportsProfile("ra3_1.13"));
        });
    }

    [Fact]
    public void EveryNonChallengeHookIsAdvertisedForAllKnownProfiles()
    {
        var hooks = TestAssets.LoadManifest().PatchManifest.Hooks;
        var knownProfiles = new[]
        {
            "ra3_1.12",
            "ra3_1.13",
            "ra3_uprising_1.0",
            "ra3_uprising_1.1"
        };

        Assert.All(
            hooks.Where(hook => !hook.SectionTitle.StartsWith("Uprising Challenge", StringComparison.Ordinal)),
            hook => Assert.All(knownProfiles, profileId => Assert.True(
                hook.SupportsProfile(profileId),
                $"{hook.ReturnLabel ?? hook.Address} should support {profileId}.")));
    }

    [Fact]
    public void AllPatchRestoreAssemblyEncodesToConcreteBytes()
    {
        var manifest = TestAssets.LoadManifest();

        foreach (var hook in manifest.PatchManifest.Hooks)
        {
            var bytes = OriginalByteParser.Parse(hook.OriginalAssembly);

            Assert.True(bytes.Length >= 5, $"{hook.Address} restore bytes are too short.");
            Assert.Contains(bytes, value => value != 0);
        }
    }
}
