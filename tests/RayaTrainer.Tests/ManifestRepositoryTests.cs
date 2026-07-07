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
        Assert.Equal(13, manifest.ActionDispatch.Count);

        var money = Assert.Single(manifest.Features, feature => feature.RawName == "Moeny");
        Assert.Equal("Money", money.DisplayName);
        Assert.Equal("Ctrl+F1", money.Hotkey);
        Assert.Equal(new[] { "iEnable+8" }, money.EnableFlags);

        var destroy = Assert.Single(manifest.Features, feature => feature.RawName == "Destory Select Unit");
        Assert.Equal("Destroy Select Unit", destroy.DisplayName);
        Assert.Equal("MustCode2+900", destroy.DispatchTarget);

        Assert.Equal(
            new[] { "iEnable+13" },
            Assert.Single(manifest.Features, feature => feature.RawName == "Danger Level MIN").EnableFlags);
        Assert.Equal(
            new[] { "iEnable+14" },
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

        Assert.Equal(new[] { "iEnable+1C" }, challengeTime.EnableFlags);
        Assert.Equal(new[] { "iEnable+1D" }, challengeMoney.EnableFlags);
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
    public void LoadIncludesFreeBuildContextHook()
    {
        var manifest = TestAssets.LoadManifest();

        var hook = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Player Free Build Code");
        var feature = Assert.Single(manifest.Features, feature => feature.RawName == "Free Build");

        Assert.Equal("ra3_1.12.game+E7563", hook.Address);
        Assert.Equal("MustCode+1EC0", hook.TrampolineTarget);
        Assert.Equal("_BackPlayerFreeBuild", hook.ReturnLabel);
        Assert.Equal(new[] { "iEnable+68" }, hook.EnableFlags);
        Assert.Equal(new[] { "iEnable+68" }, feature.EnableFlags);
        Assert.Equal(
            new[] { "db 84 DB 75 0C B8 02 00 00 00" },
            hook.OriginalAssembly);
    }

    [Fact]
    public void LoadIncludesSecretProtocolDependencyBypassHook()
    {
        var manifest = TestAssets.LoadManifest();

        var hook = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Secret Protocol Dependency Bypass Code");

        Assert.Equal("ra3_1.12.game+30A6CD", hook.Address);
        Assert.Equal("MustCode+1500", hook.TrampolineTarget);
        Assert.Equal("_BackSecretProtocolDependency", hook.ReturnLabel);
        Assert.Equal(new[] { "iEnable+64" }, hook.EnableFlags);
        Assert.Equal(
            new[] { "db 8D 54 24 14 52 E8 39 85 DD FF" },
            hook.OriginalAssembly);
    }

    [Fact]
    public void LoadHooksFastBuildContextAtFunctionEntry()
    {
        var manifest = TestAssets.LoadManifest();

        var hook = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Player Fast Build Context Code");
        var fastBuild1 = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Player Fast Build 1 Code");
        var fastBuild2 = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Player Fast Build 2 Code");
        var fastBuild3 = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Player Fast Build 3 Code");

        Assert.Equal("ra3_1.12.game+30F440", hook.Address);
        Assert.Equal("MustCode+1D00", hook.TrampolineTarget);
        Assert.Equal("_BackPlayerFastBuildContext", hook.ReturnLabel);
        Assert.Equal(new[] { "iEnable+C" }, hook.EnableFlags);
        Assert.Equal(
            new[] { "db 83 EC 08 55 8B 6C 24 10" },
            hook.OriginalAssembly);

        Assert.Equal("MustCode+1D40", fastBuild1.TrampolineTarget);
        Assert.Equal("MustCode+1D80", fastBuild2.TrampolineTarget);
        Assert.Equal("MustCode+1DC0", fastBuild3.TrampolineTarget);
    }

    [Fact]
    public void LoadHooksDisableAllSuperPowerAtEntryGuards()
    {
        var manifest = TestAssets.LoadManifest();

        var disableAllSuperPower = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Disable All Super Power Code");
        var disableAllSuperPower2 = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Disable All Super Power 2 Code");

        Assert.Equal("ra3_1.12.game+43EC70", disableAllSuperPower.Address);
        Assert.Equal("MustCode+250", disableAllSuperPower.TrampolineTarget);
        Assert.Equal("_BackDisableAllSuperPower", disableAllSuperPower.ReturnLabel);
        Assert.Equal(new[] { "iEnable+E" }, disableAllSuperPower.EnableFlags);
        Assert.Equal(
            new[] { "db 8B 44 24 04 8B 50 08" },
            disableAllSuperPower.OriginalAssembly);

        Assert.Equal("ra3_1.12.game+30A580", disableAllSuperPower2.Address);
        Assert.Equal("MustCode+1E40", disableAllSuperPower2.TrampolineTarget);
        Assert.Equal("_BackDisableAllSuperPower2", disableAllSuperPower2.ReturnLabel);
        Assert.Equal(new[] { "iEnable+E" }, disableAllSuperPower2.EnableFlags);
        Assert.Equal(
            new[] { "db A1 E4 8C CD 00 80 B8 A5 00 00 00 00" },
            disableAllSuperPower2.OriginalAssembly);
    }

    [Fact]
    public void LoadHooksIgnorePrerequisitesAtFilteredDependencyChecks()
    {
        var manifest = TestAssets.LoadManifest();

        var hooks = manifest.PatchManifest.Hooks
            .Where(hook => hook.SectionTitle == "Ignore Prerequisites Code")
            .ToArray();

        Assert.Equal(8, hooks.Length);

        var buildableHook = Assert.Single(
            hooks,
            hook => hook.Address == "ra3_1.12.game+3DF770");
        Assert.Equal("MustCode+1700", buildableHook.TrampolineTarget);
        Assert.Equal("_BackIgnorePrerequisites", buildableHook.ReturnLabel);
        Assert.Equal(new[] { "iEnable+19" }, buildableHook.EnableFlags);
        Assert.Equal(
            new[] { "db 51 56 8B F1 8B 0D E4 8C CD 00" },
            buildableHook.OriginalAssembly);

        var scienceHook = Assert.Single(
            hooks,
            hook => hook.Address == "ra3_1.12.game+4451F0");
        Assert.Equal("MustCode+1800", scienceHook.TrampolineTarget);
        Assert.Equal("_BackIgnorePrerequisitesScience", scienceHook.ReturnLabel);
        Assert.Equal(new[] { "iEnable+19" }, scienceHook.EnableFlags);
        Assert.Equal(
            new[] { "db 51 56 8B 74 24 0C 85 F6 57 8B F9" },
            scienceHook.OriginalAssembly);

        var buildabilityHook = Assert.Single(
            manifest.PatchManifest.Hooks,
            hook => hook.SectionTitle == "Ignore Prerequisites Code" &&
                hook.Address == "ra3_1.12.game+445250");

        Assert.Equal("MustCode+1880", buildabilityHook.TrampolineTarget);
        Assert.Equal("_BackIgnorePrerequisitesBuildability", buildabilityHook.ReturnLabel);
        Assert.Equal(new[] { "iEnable+19" }, buildabilityHook.EnableFlags);
        Assert.Equal(
            new[] { "db 56 8B 74 24 08 85 F6 57 8B F9" },
            buildabilityHook.OriginalAssembly);

        Assert.Contains(
            hooks,
            hook => hook.Address == "ra3_1.12.game+3BA8F7" &&
                hook.TrampolineTarget == "MustCode+1900" &&
                hook.ReturnLabel == "_BackIgnorePrerequisitesUiDependency" &&
                hook.OriginalAssembly.SequenceEqual(["db E8 14 83 D2 FF"]));
        Assert.Contains(
            hooks,
            hook => hook.Address == "ra3_1.12.game+3BA935" &&
                hook.TrampolineTarget == "MustCode+1940" &&
                hook.ReturnLabel == "_BackIgnorePrerequisitesUiChildDependency" &&
                hook.OriginalAssembly.SequenceEqual(["db E8 D6 82 D2 FF"]));
        Assert.Contains(
            hooks,
            hook => hook.Address == "ra3_1.12.game+6BB455" &&
                hook.TrampolineTarget == "MustCode+1980" &&
                hook.ReturnLabel == "_BackIgnorePrerequisitesCommandStatusDependency" &&
                hook.OriginalAssembly.SequenceEqual(["db E8 B6 77 A2 FF"]));
        Assert.Contains(
            hooks,
            hook => hook.Address == "ra3_1.12.game+14674D" &&
                hook.TrampolineTarget == "MustCode+19C0" &&
                hook.ReturnLabel == "_BackIgnorePrerequisitesPlacementDependency" &&
                hook.OriginalAssembly.SequenceEqual(["db E8 BE C4 F9 FF"]));
        Assert.Contains(
            hooks,
            hook => hook.Address == "ra3_1.12.game+38E511" &&
                hook.TrampolineTarget == "MustCode+1A00" &&
                hook.ReturnLabel == "_BackIgnorePrerequisitesProductionDependency" &&
                hook.OriginalAssembly.SequenceEqual(["db E8 FA 46 D5 FF"]));
    }

    [Fact]
    public void LoadIncludesOneKillCallerTrackingHooks()
    {
        var manifest = TestAssets.LoadManifest();

        var hooks = manifest.PatchManifest.Hooks
            .Where(hook => hook.SectionTitle == "Player One Kill Caller Filter Code")
            .ToArray();

        Assert.Equal(2, hooks.Length);
        Assert.Contains(hooks, hook =>
            hook.Address == "ra3_1.12.game+38E2F3" &&
            hook.TrampolineTarget == "MustCode+12C0" &&
            hook.ReturnLabel == "_BackPlayerOneKillItModeCaller" &&
            hook.OriginalAssembly.SequenceEqual(["db f6 86 71 04 00 00 01"]));
        Assert.Contains(hooks, hook =>
            hook.Address == "ra3_1.12.game+38E360" &&
            hook.TrampolineTarget == "MustCode+12E0" &&
            hook.ReturnLabel == "_BackPlayerOneKillItModeCaller2" &&
            hook.OriginalAssembly.SequenceEqual(["db 53 55 57 8B F9"]));
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
