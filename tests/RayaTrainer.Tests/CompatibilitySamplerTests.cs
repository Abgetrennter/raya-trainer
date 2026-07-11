using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class CompatibilitySamplerTests
{
    [Fact]
    public void PlannerCreatesHookPoints()
    {
        var manifest = new TrainerManifest(
            "ra3_1.12.game",
            [],
            new PatchManifest([
                new PatchHook(
                    Address: "ra3_1.12.game+1000",
                    SectionTitle: "Player Money Code",
                    PatchAssembly: ["jmp MustCode+29"],
                    TrampolineTarget: "MustCode+29",
                    ReturnLabel: "_BackPlayerMoney",
                    EnableFlags: ["iEnable+8"],
                    OriginalAssembly: ["add edi,[eax+04]", "mov edx,[ecx]"]),
                new PatchHook(
                    Address: "ra3_1.12.game+2000",
                    SectionTitle: "Non trampoline helper",
                    PatchAssembly: [],
                    TrampolineTarget: null,
                    ReturnLabel: null,
                    EnableFlags: [],
                    OriginalAssembly: [])
            ]),
            []);
        var resolver = new AddressResolver(0x400000, new Dictionary<string, nint>());

        var points = CompatibilitySamplePlanner.Create(manifest, resolver);

        Assert.Equal(2, points.Count);
        var hook = Assert.Single(points, point => point.AddressExpression == "ra3_1.12.game+1000");
        Assert.Equal(CompatibilitySamplePointCategory.Hook, hook.Category);
        Assert.Equal("Player Money Code", hook.Title);
        Assert.Equal((nint)0x401000, hook.AbsoluteAddress);
        Assert.Equal(new[] { "iEnable+8" }, hook.EnableFlags);
        Assert.Equal(new byte[] { 0x03, 0x78, 0x04, 0x8B, 0x11 }, hook.ExpectedBytes);
        Assert.True(hook.Disassemble);

    }

    [Fact]
    public void PlannerUsesVersionProfileForHooks()
    {
        var manifest = TestAssets.LoadManifest();
        var profile = Ra3VersionProfileRegistry.Ra3113;
        var resolver = new AddressResolver(0x400000, new Dictionary<string, nint>(), profile);

        var points = CompatibilitySamplePlanner.Create(manifest, resolver, profile);

        var hook = Assert.Single(points, point => point.AddressExpression == "ra3_1.13.game+333810");
        Assert.Equal(CompatibilitySamplePointCategory.Hook, hook.Category);
        Assert.Equal((nint)0x733810, hook.AbsoluteAddress);
        Assert.Equal(new byte[] { 0xA1, 0xA4, 0x21, 0xCE, 0x00, 0x80, 0xB8, 0xA5, 0x00, 0x00, 0x00, 0x00 }, hook.ExpectedBytes);

    }

    [Fact]
    public void SamplerCapturesRangesMarksMismatchAndDisassemblesCodePoints()
    {
        var memory = new FakeProcessMemory();
        memory.WriteBytes(
            0x83EC96,
            [
                0x90, 0x90, 0x90, 0x90,
                0x90, 0x90, 0x90, 0x90,
                0x90, 0x90, 0x90, 0x90,
                0x90, 0x90, 0x90, 0x90,
                0x8B, 0x51, 0x50,
                0xC7, 0x40, 0x0C, 0x01, 0x00, 0x00, 0x00,
                0x1B, 0xC0,
                0x83, 0xC0, 0x01,
                0xC2, 0x04, 0x00
            ]);
        var point = new CompatibilitySamplePoint(
            "ra3_1.12.game+43ECA6",
            (nint)0x83ECA6,
            CompatibilitySamplePointCategory.Hook,
            "Disable All Super Power Code",
            ["iEnable+E"],
            [0x8B, 0x51, 0x50, 0x3B, 0x50, 0x0C],
            Disassemble: true);
        var sampler = new CompatibilitySampler(memory);

        var result = sampler.Capture(
            point,
            new CompatibilitySampleOptions(BytesBefore: 0x10, BytesAfter: 0x12));

        Assert.Null(result.Error);
        Assert.Equal((nint)0x83EC96, result.RangeStart);
        Assert.Equal(0x22, result.Bytes.Length);
        Assert.Equal(new byte[] { 0x8B, 0x51, 0x50, 0xC7, 0x40, 0x0C }, result.ActualBytes);
        Assert.False(result.MatchesExpected);
        Assert.Contains(result.Instructions, instruction =>
            instruction.Text.Contains("mov", StringComparison.OrdinalIgnoreCase) &&
            instruction.Text.Contains("edx", StringComparison.OrdinalIgnoreCase) &&
            instruction.Text.Contains("ecx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Instructions, instruction =>
            instruction.Text.Contains("mov", StringComparison.OrdinalIgnoreCase) &&
            instruction.Text.Contains("eax", StringComparison.OrdinalIgnoreCase) &&
            instruction.Text.Contains("0C", StringComparison.OrdinalIgnoreCase));
    }
}
