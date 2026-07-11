using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Tests;

internal static class TestAssets
{
    public const int CurrentManifestHookCount = 44;
    public const int CurrentStandardHookCount = 42;
    public const int CurrentRa3113HookCount = 40;
    public const int CurrentUprisingHookCount = 38;
    public const int CurrentUiFeatureCount = 47;

    public static TrainerManifest LoadManifest()
    {
        return TrainerRuntimeAssets.LoadManifest();
    }
}
