using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Tests;

internal static class TestAssets
{
    public const int CurrentManifestHookCount = 49;
    public const int CurrentStandardHookCount = 47;
    public const int CurrentRa3113HookCount = 47;
    public const int CurrentUprisingHookCount = 45;
    public const int CurrentUiFeatureCount = 51;

    public static TrainerManifest LoadManifest()
    {
        return TrainerRuntimeAssets.LoadManifest();
    }

    public static byte[]? ResolveRuntimePatchSetDisableBytes(
        uint address,
        uint byteCount,
        uint moduleBase = 0x400000,
        IReadOnlyList<RuntimePatchSetDefinition>? definitions = null)
    {
        foreach (var definition in definitions ?? RuntimePatchSetCatalog.All)
        {
            foreach (var entry in definition.Entries)
            {
                if (address == moduleBase + entry.Rva && byteCount == entry.DisableBytes.Count)
                {
                    return entry.DisableBytes.ToArray();
                }
            }
        }

        return null;
    }
}
