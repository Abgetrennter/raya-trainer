using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class RuntimePatchSetCatalogTests
{
    [Fact]
    public void FrameRateUnlock_HasExpectedEntryCount()
    {
        // 27 state-changing entries plus three cave chunks and one cache reset.
        var def = RuntimePatchSetCatalog.TryGet(NativeRuntimePatchSetId.FrameRateUnlock);
        Assert.NotNull(def);

        const int expectedCount = 31;
        Assert.Equal(expectedCount, def.Value.Entries.Count);

        var dataCount = def.Value.Entries.Count(e => e.Kind == PatchSetEntryKind.Data);
        var codeFlowCount = def.Value.Entries.Count(e => e.Kind == PatchSetEntryKind.CodeFlow);
        var resetCount = def.Value.Entries.Count(e => e.Kind == PatchSetEntryKind.DerivedStateReset);
        Assert.Equal(25, dataCount);
        Assert.Equal(2, codeFlowCount);
        Assert.Equal(4, resetCount);
    }

    [Fact]
    public void FrameRateUnlock_AllEntriesHaveEqualEnableDisableLength()
    {
        var def = RuntimePatchSetCatalog.TryGet(NativeRuntimePatchSetId.FrameRateUnlock);
        Assert.NotNull(def);

        foreach (var entry in def.Value.Entries)
        {
            Assert.Equal(entry.EnableBytes.Count, entry.DisableBytes.Count);
        }
    }

    [Fact]
    public void FrameRateUnlock_KnownCodeFlowEntriesClassified()
    {
        var def = RuntimePatchSetCatalog.TryGet(NativeRuntimePatchSetId.FrameRateUnlock);
        Assert.NotNull(def);

        // 0x229853 — JMP rel8 (0xEB) enable vs JE rel8 (0x73) disable
        var codeFlowEntry1 = Assert.Single(
            def.Value.Entries,
            e => e.Rva == 0x229853);
        Assert.Equal(PatchSetEntryKind.CodeFlow, codeFlowEntry1.Kind);
        Assert.Equal([0xEB], codeFlowEntry1.EnableBytes);
        Assert.Equal([0x73], codeFlowEntry1.DisableBytes);

        // 0x2E1017 — JMP rel32 (0xE9) enable vs movss (0xF3) disable
        var codeFlowEntry2 = Assert.Single(
            def.Value.Entries,
            e => e.Rva == 0x2E1017);
        Assert.Equal(PatchSetEntryKind.CodeFlow, codeFlowEntry2.Kind);
        Assert.Equal(8, codeFlowEntry2.EnableBytes.Count);
        Assert.Equal(8, codeFlowEntry2.DisableBytes.Count);
        Assert.Equal(0xE9, codeFlowEntry2.EnableBytes[0]);
        Assert.Equal(0xF3, codeFlowEntry2.DisableBytes[0]);
    }

    [Fact]
    public void FrameRateUnlock_IdentityEntriesAreDerivedStateResets()
    {
        var def = RuntimePatchSetCatalog.TryGet(NativeRuntimePatchSetId.FrameRateUnlock);
        Assert.NotNull(def);

        foreach (var entry in def.Value.Entries)
        {
            Assert.True(
                entry.Kind == PatchSetEntryKind.DerivedStateReset ||
                !entry.EnableBytes.SequenceEqual(entry.DisableBytes),
                $"RVA 0x{entry.Rva:X8} has an unclassified identity entry.");
        }
    }

    [Fact]
    public void FrameRateUnlock_NoDuplicateAddresses()
    {
        var def = RuntimePatchSetCatalog.TryGet(NativeRuntimePatchSetId.FrameRateUnlock);
        Assert.NotNull(def);

        var rvas = def.Value.Entries.Select(e => e.Rva).ToArray();
        Assert.Equal(rvas.Length, rvas.Distinct().Count());
    }

    [Fact]
    public void FrameRateUnlock_OnlyRa3_1_12Supported()
    {
        var def = RuntimePatchSetCatalog.TryGet(NativeRuntimePatchSetId.FrameRateUnlock);
        Assert.NotNull(def);

        Assert.Single(def.Value.SupportedProfileIds);
        Assert.Equal("ra3_1.12", def.Value.SupportedProfileIds[0]);
    }

    [Fact]
    public void FrameRateUnlock_SteamAnchorsResolveSteamLayout()
    {
        var definitions = RuntimePatchSetCatalog.ResolveForTarget(
            "ra3_1.12",
            0x400000,
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["_BackFrameRateUnlockGameUpdate"] = 0x665560,
                ["_BackLogicTimeFreezeGate"] = 0x665555
            },
            signatureCompatibilityMode: false);

        var definition = Assert.Single(definitions);
        Assert.Equal(29, definition.Entries.Count);
        Assert.Contains(definition.Entries, entry =>
            entry.Rva == 0x8B4098 &&
            entry.EnableBytes.SequenceEqual(BitConverter.GetBytes(30)) &&
            entry.DisableBytes.SequenceEqual(BitConverter.GetBytes(15)));
        Assert.Contains(definition.Entries, entry =>
            entry.Rva == 0x17FF0A &&
            entry.EnableBytes.SequenceEqual(new byte[] { 0x48, 0x64, 0xBC, 0x00 }) &&
            entry.DisableBytes.SequenceEqual(new byte[] { 0x9C, 0x40, 0xCB, 0x00 }));
        Assert.Contains(definition.Entries, entry =>
            entry.Rva == 0x31F457 && entry.Kind == PatchSetEntryKind.CodeFlow);

        var caveEntries = definition.Entries
            .Where(entry => entry.Rva is >= 0x7C6420 and <= 0x7C6440)
            .ToArray();
        Assert.Equal(3, caveEntries.Length);
        Assert.All(caveEntries, entry => Assert.Equal(16, entry.EnableBytes.Count));
        Assert.All(caveEntries, entry => Assert.Equal(PatchSetEntryKind.DerivedStateReset, entry.Kind));
        Assert.All(caveEntries, entry => Assert.Equal(entry.EnableBytes, entry.DisableBytes));
        Assert.Contains(definition.Entries, entry =>
            entry.Rva == 0x8EABEC && entry.Kind == PatchSetEntryKind.DerivedStateReset);
    }

    [Fact]
    public void FrameRateUnlock_UnknownOrCompatibilityLayoutIsOmitted()
    {
        var missingAnchors = RuntimePatchSetCatalog.ResolveForTarget(
            "ra3_1.12",
            0x400000,
            null,
            signatureCompatibilityMode: false);
        var unknown = RuntimePatchSetCatalog.ResolveForTarget(
            "ra3_1.12",
            0x400000,
            new Dictionary<string, uint>
            {
                ["_BackFrameRateUnlockGameUpdate"] = 0x675560,
                ["_BackLogicTimeFreezeGate"] = 0x675555
            },
            signatureCompatibilityMode: false);
        var compatibility = RuntimePatchSetCatalog.ResolveForTarget(
            "ra3_1.12",
            0x400000,
            null,
            signatureCompatibilityMode: true);

        Assert.Empty(missingAnchors);
        Assert.Empty(unknown);
        Assert.Empty(compatibility);
    }

    [Fact]
    public void TryGet_ReturnsNullForUnknownId()
    {
        var def = RuntimePatchSetCatalog.TryGet((NativeRuntimePatchSetId)999);
        Assert.Null(def);
    }

    [Fact]
    public void All_ContainsOnlyFrameRateUnlock()
    {
        Assert.Single(RuntimePatchSetCatalog.All);
        Assert.Equal(NativeRuntimePatchSetId.FrameRateUnlock, RuntimePatchSetCatalog.All[0].Id);
    }
}
