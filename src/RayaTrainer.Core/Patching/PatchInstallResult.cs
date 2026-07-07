namespace RayaTrainer.Core.Patching;

public sealed record PatchInstallOptions(
    bool SkipMismatchedHooks,
    int DumpBytesBefore,
    int DumpBytesAfter)
{
    public static PatchInstallOptions Strict { get; } =
        new(SkipMismatchedHooks: false, DumpBytesBefore: 0, DumpBytesAfter: 0);

    public static PatchInstallOptions SkipMismatches(int dumpBytesBefore = 16, int dumpBytesAfter = 96)
    {
        if (dumpBytesBefore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dumpBytesBefore), "Dump byte count before hook must be non-negative.");
        }

        if (dumpBytesAfter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dumpBytesAfter), "Dump byte count after hook must be positive.");
        }

        return new PatchInstallOptions(true, dumpBytesBefore, dumpBytesAfter);
    }
}

public sealed record PatchInstallResult(
    int HookCount,
    int InstalledHookCount,
    IReadOnlyList<SkippedPatchHook> SkippedHooks)
{
    public static PatchInstallResult Empty { get; } = new(0, 0, Array.Empty<SkippedPatchHook>());
}

public sealed record SkippedPatchHook(
    PatchHookPlan Hook,
    int HookIndex,
    int HookCount,
    nint AbsoluteAddress,
    byte[] ExpectedBytes,
    byte[] ActualBytes,
    nint DumpStartAddress,
    byte[] DumpBytes,
    string Reason);
