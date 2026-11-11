namespace RayaTrainer.Core.Patching;

public sealed record PatchInstallResult(
    int HookCount,
    int InstalledHookCount,
    IReadOnlyList<SkippedPatchHook> SkippedHooks)
{
    public static PatchInstallResult Empty { get; } = new(0, 0, Array.Empty<SkippedPatchHook>());
}

public sealed record SkippedPatchHook(
    string Name,
    string Reason);
