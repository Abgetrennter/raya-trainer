using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Diagnostics;

public static class CompatibilitySamplePlanner
{
    public static IReadOnlyList<CompatibilitySamplePoint> Create(
        TrainerManifest manifest,
        AddressResolver resolver,
        Ra3VersionProfile? profile = null)
    {
        var points = new List<CompatibilitySamplePoint>();
        foreach (var hook in manifest.PatchManifest.Hooks)
        {
            var sample = CreateHookSampleInput(hook, profile);
            if (sample is null)
            {
                continue;
            }

            points.Add(new CompatibilitySamplePoint(
                sample.AddressExpression,
                resolver.Resolve(sample.AddressExpression),
                CompatibilitySamplePointCategory.Hook,
                hook.SectionTitle,
                hook.EnableFlags.ToArray(),
                sample.ExpectedBytes,
                Disassemble: true));
        }

        return points.OrderBy(point => point.AbsoluteAddress).ToArray();
    }

    private static HookSampleInput? CreateHookSampleInput(
        PatchHook hook,
        Ra3VersionProfile? profile)
    {
        if (profile is null)
        {
            return new HookSampleInput(
                hook.Address,
                OriginalByteParser.Parse(hook.OriginalAssembly));
        }

        var key = string.IsNullOrWhiteSpace(hook.ReturnLabel) ? hook.Address : hook.ReturnLabel;
        if (!profile.Hooks.TryGetValue(key, out var profileHook) ||
            profileHook.Status != AddressSupportStatus.Verified ||
            profileHook.Rva is null)
        {
            return null;
        }

        return new HookSampleInput(
            $"{profile.ProcessName}+{profileHook.Rva.Value:X}",
            profileHook.ExpectedBytes?.ToArray() ?? OriginalByteParser.Parse(hook.OriginalAssembly));
    }

    private sealed record HookSampleInput(string AddressExpression, byte[] ExpectedBytes);
}
