using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;

namespace RayaTrainer.Tests;

internal static class TestAssets
{
    public const int CurrentManifestHookCount = 42;
    public const int CurrentStandardHookCount = 40;
    public const int CurrentRa3113HookCount = 39;
    public const int CurrentUprisingHookCount = 37;
    public const int CurrentUiFeatureCount = 46;

    public static TrainerManifest LoadManifest()
    {
        return TrainerRuntimeAssets.LoadManifest();
    }
}
