using Iced.Intel;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Patching;

public static class PatchHookPlanner
{
    public static IReadOnlyList<PatchHookPlan> CreatePlans(PatchManifest manifest)
    {
        return manifest.Hooks
            // The legacy planner is the canonical RA3 1.12 path. Keep globally
            // supported hooks and hooks explicitly verified for 1.12, while
            // continuing to exclude Uprising-only entries.
            .Where(hook => hook.SupportedProfileIds is not { Count: > 0 } || hook.SupportsProfile("ra3_1.12"))
            .Select(hook => CreatePlan(hook, hook.Address))
            .ToArray();
    }

    public static PatchHookPlanResult CreateSupportedPlans(
        PatchManifest manifest,
        Ra3VersionProfile profile,
        bool includeUnlistedHooks = false)
    {
        var plans = new List<PatchHookPlan>();
        var skipped = new List<SkippedPatchHookPlan>();
        var patchableHooks = manifest.Hooks
            .Where(hook => hook.SupportsProfile(profile.Id))
            .ToArray();

        for (var index = 0; index < patchableHooks.Length; index++)
        {
            var hook = patchableHooks[index];
            var key = HookKey(hook);
            if (profile.SupersededHooks.Contains(key))
            {
                continue;
            }

            if (!profile.Hooks.TryGetValue(key, out var profileHook))
            {
                if (includeUnlistedHooks)
                {
                    // Hook is not in the profile catalog but signature scanning is active —
                    // include it so it can be resolved from the scanned address. This allows
                    // test hooks and hooks that rely solely on signature scanning to work.
                    plans.Add(CreatePlan(hook, hook.Address));
                    continue;
                }

                skipped.Add(new SkippedPatchHookPlan(
                    CreatePlan(hook, hook.Address),
                    index + 1,
                    patchableHooks.Length,
                    $"当前版本 profile 中该 hook 未验证，已跳过相关功能：{key}。"));
                continue;
            }

            if (profileHook.Status != AddressSupportStatus.Verified || profileHook.Rva is null)
            {
                skipped.Add(new SkippedPatchHookPlan(
                    CreatePlan(hook, hook.Address),
                    index + 1,
                    patchableHooks.Length,
                    $"当前版本 profile 中该 hook 未验证，已跳过相关功能：{key}。"));
                continue;
            }

            plans.Add(CreatePlan(hook, $"{profile.ProcessName}+{profileHook.Rva.Value:X}", profileHook.ExpectedBytes));
        }

        return new PatchHookPlanResult(plans, skipped);
    }

    private static PatchHookPlan CreatePlan(
        PatchHook hook,
        string address,
        IReadOnlyList<byte>? expectedBytes = null)
    {
        var originalBytes = expectedBytes?.ToArray() ?? OriginalByteParser.Parse(hook.OriginalAssembly);
        ValidateInstructionBoundaries(hook, originalBytes);
        return new PatchHookPlan(
            address,
            Math.Max(5, originalBytes.Length),
            originalBytes)
        {
            SectionTitle = hook.SectionTitle,
            EnableFlags = hook.EnableFlags.ToArray(),
            ReturnLabel = hook.ReturnLabel
        };
    }

    private static void ValidateInstructionBoundaries(PatchHook hook, byte[] originalBytes)
    {
        var decoder = Decoder.Create(32, originalBytes);
        while (decoder.IP < (uint)originalBytes.Length)
        {
            decoder.Decode(out var instruction);
            if (instruction.Code == Code.INVALID)
            {
                var key = HookKey(hook);
                throw new InvalidDataException(
                    $"Hook '{key}' contains a truncated or invalid x86 instruction at byte offset {decoder.IP - (uint)instruction.Length}.");
            }
        }

        if (decoder.IP != (uint)originalBytes.Length)
        {
            throw new InvalidDataException(
                $"Hook '{HookKey(hook)}' does not end on an x86 instruction boundary.");
        }
    }

    private static string HookKey(PatchHook hook)
    {
        return string.IsNullOrWhiteSpace(hook.ReturnLabel)
            ? hook.Address
            : hook.ReturnLabel;
    }
}
