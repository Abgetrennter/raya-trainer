using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class PatchHookInspectorTests
{
    [Fact]
    public void CaptureReadsExpectedAndActualHookBytes()
    {
        var memory = new FakeProcessMemory();
        memory.WriteBytes(0x70F530, new byte[] { 0xE9, 0x11, 0x22, 0x33, 0x44 });
        var resolver = new AddressResolver(0x400000, new Dictionary<string, nint>());
        var manifest = new PatchManifest([
            new PatchHook(
                Address: "ra3_1.12.game+30F530",
                SectionTitle: "Enemy Can't Build Code",
                PatchAssembly: ["jmp MustCode+3d0"],
                TrampolineTarget: "MustCode+3d0",
                ReturnLabel: "_BackEnemyCantBuild",
                EnableFlags: ["iEnable+15"],
                OriginalAssembly: ["add edx,[eax+04]", "cmp edx,edi"])
        ]);

        var snapshot = Assert.Single(PatchHookInspector.Capture(manifest, memory, resolver));

        Assert.Equal("ra3_1.12.game+30F530", snapshot.Address);
        Assert.Equal("Enemy Can't Build Code", snapshot.SectionTitle);
        Assert.Equal((nint)0x70F530, snapshot.AbsoluteAddress);
        Assert.Equal(new byte[] { 0x03, 0x50, 0x04, 0x3B, 0xD7 }, snapshot.ExpectedBytes);
        Assert.Equal(new byte[] { 0xE9, 0x11, 0x22, 0x33, 0x44 }, snapshot.ActualBytes);
        Assert.False(snapshot.Matches);
    }

    [Fact]
    public void CaptureUsesVersionProfileHookAddressAndExpectedBytes()
    {
        var manifestHook = TestAssets.LoadManifest().PatchManifest.Hooks.Single(
            hook => hook.Address.Equals("ra3_1.12.game+30A580", StringComparison.OrdinalIgnoreCase));
        var manifest = new PatchManifest([manifestHook]);
        var profile = Ra3VersionProfileRegistry.Ra3113;
        var hookKey = string.IsNullOrWhiteSpace(manifestHook.ReturnLabel)
            ? manifestHook.Address
            : manifestHook.ReturnLabel;
        var profileHook = profile.Hooks[hookKey];
        Assert.NotNull(profileHook.ExpectedBytes);

        var memory = new FakeProcessMemory();
        memory.WriteBytes(0x400000 + profileHook.Rva!.Value, profileHook.ExpectedBytes.ToArray());
        var resolver = new AddressResolver(0x400000, new Dictionary<string, nint>(), profile);

        var snapshot = Assert.Single(PatchHookInspector.Capture(manifest, memory, resolver, profile));

        Assert.Equal("ra3_1.13.game+333810", snapshot.Address);
        Assert.Equal((nint)0x733810, snapshot.AbsoluteAddress);
        Assert.Equal(profileHook.ExpectedBytes, snapshot.ExpectedBytes);
        Assert.Equal(profileHook.ExpectedBytes, snapshot.ActualBytes);
        Assert.True(snapshot.Matches);
    }

    [Fact]
    public void CaptureResolvedUsesSignatureCatalogAddress()
    {
        var memory = new FakeProcessMemory();
        memory.WriteBytes(0x713579, new byte[] { 0x03, 0x50, 0x04, 0x3B, 0xD7 });
        var manifest = new PatchManifest([
            new PatchHook(
                Address: "ra3_1.12.game+30F530",
                SectionTitle: "Enemy Can't Build Code",
                PatchAssembly: ["jmp MustCode+3d0"],
                TrampolineTarget: "MustCode+3d0",
                ReturnLabel: "_BackEnemyCantBuild",
                EnableFlags: ["iEnable+15"],
                OriginalAssembly: ["add edx,[eax+04]", "cmp edx,edi"])
        ]);

        var snapshot = Assert.Single(PatchHookInspector.CaptureResolved(
            manifest,
            memory,
            new Dictionary<string, uint>
            {
                ["_BackEnemyCantBuild"] = 0x713579
            }));

        Assert.Equal("_BackEnemyCantBuild", snapshot.Address);
        Assert.Equal((nint)0x713579, snapshot.AbsoluteAddress);
        Assert.True(snapshot.Matches);
    }

    [Fact]
    public void CaptureResolvedRejectsMissingHookAddress()
    {
        var manifest = new PatchManifest([
            new PatchHook(
                Address: "ra3_1.12.game+30F530",
                SectionTitle: "Enemy Can't Build Code",
                PatchAssembly: ["jmp MustCode+3d0"],
                TrampolineTarget: "MustCode+3d0",
                ReturnLabel: "_BackEnemyCantBuild",
                EnableFlags: ["iEnable+15"],
                OriginalAssembly: ["add edx,[eax+04]", "cmp edx,edi"])
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PatchHookInspector.CaptureResolved(manifest, new FakeProcessMemory(), new Dictionary<string, uint>()));

        Assert.Contains("_BackEnemyCantBuild", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureResolvedCanCompareAgainstRuntimeBaseline()
    {
        var memory = new FakeProcessMemory();
        memory.WriteBytes(0x748990, new byte[] { 0xA1, 0x84, 0xDE, 0xCD, 0x00 });
        var manifest = new PatchManifest([
            new PatchHook(
                Address: "ra3_1.12.game+30A580",
                SectionTitle: "Disable All Super Power 2 Code",
                PatchAssembly: ["jmp MustCode+1E40"],
                TrampolineTarget: "MustCode+1E40",
                ReturnLabel: "_BackDisableAllSuperPower2",
                EnableFlags: ["iEnable+E"],
                OriginalAssembly: ["db A1 E4 8C CD 00"])
        ]);

        var snapshot = Assert.Single(PatchHookInspector.CaptureResolved(
            manifest,
            memory,
            new Dictionary<string, uint>
            {
                ["_BackDisableAllSuperPower2"] = 0x748990
            },
            new Dictionary<string, byte[]>
            {
                ["_BackDisableAllSuperPower2"] = [0xA1, 0x84, 0xDE, 0xCD, 0x00]
            }));

        Assert.True(snapshot.Matches);
    }
}
