using RayaTrainer.Core.Manifest;

namespace RayaTrainer.Core.Features;

public enum FeatureScanKind
{
    Toggle,
    Action,
    Unsupported
}

public sealed record FeatureScanItem(
    TrainerFeature Feature,
    FeatureScanKind Kind,
    string? SkipReason)
{
    public bool CanScan => SkipReason is null;
}

public static class FeatureScanPlanner
{
    public static IReadOnlyList<FeatureScanItem> Create(IEnumerable<TrainerFeature> features)
    {
        return features.Select(CreateItem).ToArray();
    }

    private static FeatureScanItem CreateItem(TrainerFeature feature)
    {
        if (FeatureDispatchDefaults.IsToggle(feature))
        {
            return new FeatureScanItem(feature, FeatureScanKind.Toggle, null);
        }

        if (FeatureDispatchDefaults.IsAction(feature))
        {
            return new FeatureScanItem(feature, FeatureScanKind.Action, null);
        }

        return new FeatureScanItem(
            feature,
            FeatureScanKind.Unsupported,
            "不是可触发的 toggle/action 功能。");
    }
}
