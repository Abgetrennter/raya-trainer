using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Features;

public static class FeatureDispatchDefaults
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan PausedGracePeriod = TimeSpan.FromSeconds(60);

    public static bool IsToggle(TrainerFeature feature) =>
        feature.ValueHint is null && feature.EnableFlags.Count > 0;

    public static bool IsAction(TrainerFeature feature) =>
        feature.ValueHint is not null;
}
