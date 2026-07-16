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
}
