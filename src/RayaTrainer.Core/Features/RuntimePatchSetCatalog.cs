using RayaTrainer.Core.Agent;

namespace RayaTrainer.Core.Features;

/// <summary>
/// Classifies a patch-set entry's nature — Data entries are plain value writes
/// (e.g. float/int constants), CodeFlow entries alter control flow
/// (e.g. JMP/NOP over a conditional branch).
/// </summary>
public enum PatchSetEntryKind : byte
{
    Data = 0,
    CodeFlow = 1,
    DerivedStateReset = 2
}

/// <summary>
/// One address-level entry within a runtime patch set.
/// </summary>
public readonly record struct RuntimePatchSetEntry(
    uint Rva,
    IReadOnlyList<byte> EnableBytes,
    IReadOnlyList<byte> DisableBytes,
    PatchSetEntryKind Kind);

/// <summary>
/// A named collection of runtime byte patches that can be applied/enabled
/// or restored/disabled as a single unit via cmd 6 (SetRuntimePatchSet).
/// </summary>
public readonly record struct RuntimePatchSetDefinition(
    NativeRuntimePatchSetId Id,
    IReadOnlyList<RuntimePatchSetEntry> Entries,
    IReadOnlyList<string> SupportedProfileIds);

/// <summary>
/// Catalog of all known runtime patch sets. Each patch set is a named group of
/// memory writes that the native agent applies atomically when enabled.
/// </summary>
public static class RuntimePatchSetCatalog
{
    private const string Ra3112ProfileId = "ra3_1.12";
    private const string FrameRateHookKey = "_BackFrameRateUnlockGameUpdate";
    private const string LogicTimeHookKey = "_BackLogicTimeFreezeGate";

    public static IReadOnlyList<RuntimePatchSetDefinition> All { get; } =
    [
        BuildFrameRateUnlock(),
    ];

    public static RuntimePatchSetDefinition? TryGet(NativeRuntimePatchSetId id)
    {
        foreach (var def in All)
        {
            if (def.Id == id)
                return def;
        }
        return null;
    }

    public static IReadOnlyList<RuntimePatchSetDefinition> ResolveForTarget(
        string profileId,
        uint moduleBase,
        IReadOnlyDictionary<string, uint>? scannedAddresses,
        bool signatureCompatibilityMode)
    {
        if (signatureCompatibilityMode ||
            !profileId.Equals(Ra3112ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (scannedAddresses is null ||
            !TryGetScannedRva(scannedAddresses, FrameRateHookKey, moduleBase, out var frameRateHookRva) ||
            !TryGetScannedRva(scannedAddresses, LogicTimeHookKey, moduleBase, out var logicTimeHookRva))
        {
            return [];
        }

        return (frameRateHookRva, logicTimeHookRva) switch
        {
            (0x226630, 0x226625) => [BuildFrameRateUnlock()],
            (0x265560, 0x265555) => [BuildSteamFrameRateUnlock()],
            _ => []
        };
    }

    private static bool TryGetScannedRva(
        IReadOnlyDictionary<string, uint> scannedAddresses,
        string key,
        uint moduleBase,
        out uint rva)
    {
        if (scannedAddresses.TryGetValue(key, out var address) && address >= moduleBase)
        {
            rva = address - moduleBase;
            return true;
        }

        rva = 0;
        return false;
    }

    // ── Frame Rate Unlock (PatchSet 1) ──────────────────────────────────────
    // Source: the pre-v11 Frame Rate Unlock ToggleBytePatches contract.
    // The executable Bezier cave is represented as <=16-byte CodeFlow entries
    // with a zero-filled disabled baseline.
    // ─────────────────────────────────────────────────────────────────────────

    private static byte[] BezierAccelerationScaleCode =>
    [
        0xF3, 0x0F, 0x10, 0x8A, 0xF0, 0x00, 0x00, 0x00,
        0xF3, 0x0F, 0x59, 0x0D, 0x20, 0x64, 0xBC, 0x00,
        0xE9, 0x0A, 0xAC, 0xB1, 0xFF,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3E
    ];

    private static byte[] SteamBezierAccelerationScaleCode =>
    [
        0xF3, 0x0F, 0x10, 0x8A, 0xF0, 0x00, 0x00, 0x00,
        0xF3, 0x0F, 0x59, 0x0D, 0x40, 0x64, 0xBC, 0x00,
        0xE9, 0x2A, 0x90, 0xB5, 0xFF,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3E,
        0x00, 0x00, 0x00, 0x00,
        0x3C, 0x00, 0x00, 0x00,
        0x8F, 0xC2, 0xF5, 0x3C
    ];

    private static RuntimePatchSetDefinition BuildFrameRateUnlock() =>
        new(NativeRuntimePatchSetId.FrameRateUnlock, BuildFrameRateUnlockEntries(), [Ra3112ProfileId]);

    private static RuntimePatchSetDefinition BuildSteamFrameRateUnlock() =>
        new(NativeRuntimePatchSetId.FrameRateUnlock, BuildSteamFrameRateUnlockEntries(), [Ra3112ProfileId]);

    private static IReadOnlyList<RuntimePatchSetEntry> BuildFrameRateUnlockEntries()
    {
        var entries = new List<RuntimePatchSetEntry>
        {
            // L#332 - ra3_1.12.game+8AD5F4 — max frame rate (int32): 60 vs 15
            new(0x8AD5F4, BitConverter.GetBytes(60),          BitConverter.GetBytes(15),          PatchSetEntryKind.Data),
            // L#333 - ra3_1.12.game+8AF9D0 — refresh rate (int32): 30 vs 15
            new(0x8AF9D0, BitConverter.GetBytes(30),          BitConverter.GetBytes(15),          PatchSetEntryKind.Data),
            // L#334 - ra3_1.12.game+8DBC4C — frame interval float: 30*0.001 vs 15*0.001
            new(0x8DBC4C, BitConverter.GetBytes(30.0f * 0.001f), BitConverter.GetBytes(15.0f * 0.001f), PatchSetEntryKind.Data),
            // L#335 - ra3_1.12.game+8DBC1C — rate divisor float: 1000/30 vs 1000/15
            new(0x8DBC1C, BitConverter.GetBytes(1000.0f / 30.0f), BitConverter.GetBytes(1000.0f / 15.0f), PatchSetEntryKind.Data),
            // L#336 - ra3_1.12.game+8DBC58 — fps cap float: 30 vs 15
            new(0x8DBC58, BitConverter.GetBytes(30.0f),       BitConverter.GetBytes(15.0f),       PatchSetEntryKind.Data),
            // L#337 - ra3_1.12.game+8DBC50 — target fps float: 60 vs 30
            new(0x8DBC50, BitConverter.GetBytes(60.0f),       BitConverter.GetBytes(30.0f),       PatchSetEntryKind.Data),
            // L#338 - ra3_1.12.game+8DBC54 — target interval float: 1000/60 vs 1000/30
            new(0x8DBC54, BitConverter.GetBytes(1000.0f / 60.0f), BitConverter.GetBytes(1000.0f / 30.0f), PatchSetEntryKind.Data),
            // L#339 - ra3_1.12.game+8DBC94 — sleep interval float: 1/30 vs 1/15
            new(0x8DBC94, BitConverter.GetBytes(1.0f / 30.0f), BitConverter.GetBytes(1.0f / 15.0f), PatchSetEntryKind.Data),
            // L#340 - ra3_1.12.game+8DBD34 — sleep target float: 1/60 vs 1/30
            new(0x8DBD34, BitConverter.GetBytes(1.0f / 60.0f), BitConverter.GetBytes(1.0f / 30.0f), PatchSetEntryKind.Data),
            // L#341 - ra3_1.12.game+8E5A5C — SKIPPED (identity: 0==0)
            // L#342 - ra3_1.12.game+8E176C — vblank interval int32: 16 vs 33
            new(0x8E176C, BitConverter.GetBytes(16),          BitConverter.GetBytes(33),          PatchSetEntryKind.Data),
            // L#343 - ra3_1.12.game+7C63D4 first — SKIPPED (identity: 0.03==0.03)
            // L#344 - ra3_1.12.game+8DBC5C first — SKIPPED (identity: 30*0.001==30*0.001)
            // L#345 - ra3_1.12.game+1FEC91 — render mode byte: 0x10 vs 0x1D
            new(0x1FEC91, [0x10], [0x1D],                    PatchSetEntryKind.Data),
            // L#346 - ra3_1.12.game+1FECA3 — render mode byte: 0x10 vs 0x1D
            new(0x1FECA3, [0x10], [0x1D],                    PatchSetEntryKind.Data),
            // L#347 - ra3_1.12.game+229853 — JMP rel8 (0xEB) vs JE rel8 (0x73) — CodeFlow
            new(0x229853, [0xEB],        [0x73],             PatchSetEntryKind.CodeFlow),
            // L#348 - ra3_1.12.game+13E90A — float/int bytes
            new(0x13E90A, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#349 - ra3_1.12.game+1FFAD1 — float/int bytes
            new(0x1FFAD1, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#350 - ra3_1.12.game+216257 — float/int bytes
            new(0x216257, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#351 - ra3_1.12.game+2297C9 — float/int bytes
            new(0x2297C9, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#352 - ra3_1.12.game+7B30D8 — float/int bytes
            new(0x7B30D8, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#353 - ra3_1.12.game+7B3108 — float/int bytes
            new(0x7B3108, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#354 - ra3_1.12.game+7B3138 — float/int bytes
            new(0x7B3138, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#355 - ra3_1.12.game+7B3C59 — float/int bytes
            new(0x7B3C59, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#356 - ra3_1.12.game+2C17CC — 3-byte data
            new(0x2C17CC, [0xD4, 0x63, 0xBC], [0x5C, 0xBC, 0xCD], PatchSetEntryKind.Data),
            // L#357 - ra3_1.12.game+8DBC5C second (last-wins): frame interval float: 60*0.001 vs 30*0.001
            new(0x8DBC5C, BitConverter.GetBytes(60.0f * 0.001f), BitConverter.GetBytes(30.0f * 0.001f), PatchSetEntryKind.Data),
            // L#358 - ra3_1.12.game+7C63D4 second (last-wins): frame budget float: 0.03 vs 0.0
            new(0x7C63D4, BitConverter.GetBytes(0.03f),      BitConverter.GetBytes(0.0f),        PatchSetEntryKind.Data),
            // L#359 - ra3_1.12.game+1EB6F6 — float/int bytes
            new(0x1EB6F6, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
            // L#360 - ra3_1.12.game+1EB6FC — float/int bytes
            new(0x1EB6FC, [0xF4, 0xD5],  [0xD4, 0xF9],       PatchSetEntryKind.Data),
        };

        // The pre-v11 toggle wrote this cave on every transition. Same-value reset entries
        // keep the cave valid on both sides of the source jump.
        entries.AddRange(BuildCodeCaveEntries(0x7C6400, BezierAccelerationScaleCode));
        entries.Add(new RuntimePatchSetEntry(
            0x8E5A5C,
            BitConverter.GetBytes(0),
            BitConverter.GetBytes(0),
            PatchSetEntryKind.DerivedStateReset));
        entries.Add(
            new(0x2E1017,
                [0xE9, 0xE4, 0x53, 0x4E, 0x00, 0x90, 0x90, 0x90],
                [0xF3, 0x0F, 0x10, 0x8A, 0xF0, 0x00, 0x00, 0x00],
                PatchSetEntryKind.CodeFlow));
        return entries;
    }

    private static IReadOnlyList<RuntimePatchSetEntry> BuildSteamFrameRateUnlockEntries()
    {
        byte[] renderTargetAddress = [0x48, 0x64, 0xBC, 0x00]; // int32 60 @ VA 0xBC6448
        byte[] originalRenderTargetAddress = [0x9C, 0x40, 0xCB, 0x00]; // VA 0xCB409C
        var entries = new List<RuntimePatchSetEntry>
        {
            // Steam English 1.12 time constants, statically aligned against the TW init group.
            new(0x8B4098, BitConverter.GetBytes(30), BitConverter.GetBytes(15), PatchSetEntryKind.Data),
            new(0x8E0DEC, BitConverter.GetBytes(30.0f * 0.001f), BitConverter.GetBytes(15.0f * 0.001f), PatchSetEntryKind.Data),
            new(0x8E0DBC, BitConverter.GetBytes(1000.0f / 30.0f), BitConverter.GetBytes(1000.0f / 15.0f), PatchSetEntryKind.Data),
            new(0x8E0DF8, BitConverter.GetBytes(30.0f), BitConverter.GetBytes(15.0f), PatchSetEntryKind.Data),
            new(0x8E0DF0, BitConverter.GetBytes(60.0f), BitConverter.GetBytes(30.0f), PatchSetEntryKind.Data),
            new(0x8E0DF4, BitConverter.GetBytes(1000.0f / 60.0f), BitConverter.GetBytes(1000.0f / 30.0f), PatchSetEntryKind.Data),
            new(0x8E0E34, BitConverter.GetBytes(1.0f / 30.0f), BitConverter.GetBytes(1.0f / 15.0f), PatchSetEntryKind.Data),
            new(0x8E0ED4, BitConverter.GetBytes(1.0f / 60.0f), BitConverter.GetBytes(1.0f / 30.0f), PatchSetEntryKind.Data),
            new(0x8E690C, BitConverter.GetBytes(16), BitConverter.GetBytes(33), PatchSetEntryKind.Data),
            new(0x8E0DFC, BitConverter.GetBytes(60.0f * 0.001f), BitConverter.GetBytes(30.0f * 0.001f), PatchSetEntryKind.Data),

            new(0x23DC21, [0x10], [0x1D], PatchSetEntryKind.Data),
            new(0x23DC33, [0x10], [0x1D], PatchSetEntryKind.Data),
            new(0x268783, [0xEB], [0x73], PatchSetEntryKind.CodeFlow),

            // Redirect the ten render-rate consumers from CB409C to the cave constant.
            new(0x17FF0A, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x23EA21, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x255167, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x2686F9, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x7B40A8, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x7B40D8, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x7B4108, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x7B4C29, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x22A756, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),
            new(0x22A75C, renderTargetAddress, originalRenderTargetAddress, PatchSetEntryKind.Data),

            // Keep the one CDBC5C-equivalent consumer at 0.03 while the global becomes 0.06.
            new(0x2FFDAC, [0x4C, 0x64, 0xBC, 0x00], [0xFC, 0x0D, 0xCE, 0x00], PatchSetEntryKind.Data),
        };

        // BC6420 is zero-filled executable padding in the Steam image. The third chunk
        // also owns scale=0.25 @ BC6440, renderTarget=60 @ BC6448, and 0.03 @ BC644C.
        entries.AddRange(BuildCodeCaveEntries(0x7C6420, SteamBezierAccelerationScaleCode));
        entries.Add(new RuntimePatchSetEntry(
            0x8EABEC,
            BitConverter.GetBytes(0),
            BitConverter.GetBytes(0),
            PatchSetEntryKind.DerivedStateReset));
        entries.Add(new RuntimePatchSetEntry(
            0x31F457,
            [0xE9, 0xC4, 0x6F, 0x4A, 0x00, 0x90, 0x90, 0x90],
            [0xF3, 0x0F, 0x10, 0x8A, 0xF0, 0x00, 0x00, 0x00],
            PatchSetEntryKind.CodeFlow));
        return entries;
    }

    private static IReadOnlyList<RuntimePatchSetEntry> BuildCodeCaveEntries(
        uint startRva,
        IReadOnlyList<byte> code)
    {
        const int maximumEntryLength = 16;
        var entries = new List<RuntimePatchSetEntry>();
        for (var offset = 0; offset < code.Count; offset += maximumEntryLength)
        {
            var length = Math.Min(maximumEntryLength, code.Count - offset);
            var enableBytes = code.Skip(offset).Take(length).ToArray();
            entries.Add(new RuntimePatchSetEntry(
                checked(startRva + (uint)offset),
                enableBytes,
                enableBytes.ToArray(),
                PatchSetEntryKind.DerivedStateReset));
        }

        return entries;
    }
}
