namespace RayaTrainer.Core.Patching;

public sealed record PatchHookPlan(
    string Address,
    int PatchLength,
    byte[] OriginalBytes)
{
    public string SectionTitle { get; init; } = string.Empty;

    public IReadOnlyList<string> EnableFlags { get; init; } = Array.Empty<string>();

    public string? ReturnLabel { get; init; }
}

public sealed record PatchHookPlanResult(
    IReadOnlyList<PatchHookPlan> Plans,
    IReadOnlyList<SkippedPatchHookPlan> SkippedHooks);

public sealed record SkippedPatchHookPlan(
    PatchHookPlan Hook,
    int HookIndex,
    int HookCount,
    string Reason);
