using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using System.Text.Json;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainViewModelPatchMismatchTests
{
    [Fact]
    public void PatchMismatchReportKeepsHumanReadableReasonsAndSkippedFlags()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var installResult = new PatchInstallResult(
            HookCount: 1,
            InstalledHookCount: 0,
            [
                new SkippedPatchHook(
                    new PatchHookPlan(
                        "ra3_1.12.game+43ECA6",
                        6,
                        [0x8B, 0x51, 0x50, 0x3B, 0x50, 0x0C])
                    {
                        SectionTitle = "Disable All Super Power Code",
                        EnableFlags = ["iEnable+E"]
                    },
                    HookIndex: 1,
                    HookCount: 1,
                    AbsoluteAddress: 0x83ECA6,
                    ExpectedBytes: [0x8B, 0x51, 0x50, 0x3B, 0x50, 0x0C],
                    ActualBytes: [0x8B, 0x51, 0x50, 0xC7, 0x40, 0x0C],
                    DumpStartAddress: 0x83EC96,
                    DumpBytes: [
                        0x90, 0x90, 0x90, 0x90,
                        0x90, 0x90, 0x90, 0x90,
                        0x90, 0x90, 0x90, 0x90,
                        0x90, 0x90, 0x90, 0x90,
                        0x8B, 0x51, 0x50,
                        0xC7, 0x40, 0x0C, 0x01, 0x00, 0x00, 0x00
                    ],
                    Reason: "Patch 点原始字节不匹配，已跳过该 hook；可能原因：该位置已经被 patch 过、游戏版本不一致，或者 MOD 加载时修改了代码段。")
            ]);

        var path = PatchMismatchReportWriter.Write(
            directory,
            new TrainerTarget(
                "ra3_1.12.game",
                0x400000,
                true,
                true,
                ProcessId: 1234,
                ModulePath: @"E:\Game\rad alter 3_Small\Data\ra3_1.12.game",
                FileVersion: "1.12.3444.25830"),
            installResult,
            new PatchMismatchReportOptions(DumpBytesBefore: 16, DumpBytesAfter: 96));

        var json = File.ReadAllText(path);
        Assert.Contains("Disable All Super Power Code", json);
        Assert.Contains("ra3_1.12.game+43ECA6", json);
        Assert.Contains("iEnable+E", json);
        Assert.Contains("8B 51 50 3B 50 0C", json);
        Assert.Contains("8B 51 50 C7 40 0C", json);
        Assert.Contains("已经被 patch 过", json);
        Assert.Contains("版本不一致", json);
        Assert.Contains("MOD 加载时修改", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("skippedCount").GetInt32());
    }

}
