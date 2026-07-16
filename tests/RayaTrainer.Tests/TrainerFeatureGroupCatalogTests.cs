using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerFeatureGroupCatalogTests
{
    private static IReadOnlyList<TrainerFeature> Features =>
        TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);

    /// <summary>
    /// 功能通过专用完整面板渲染（秘密协议页面 + 增援面板），
    /// 不参与通用功能分组网格，因此从 render-guard 中排除。
    /// </summary>
    private static readonly HashSet<string> DedicatedUiFeatureRawNames = new(StringComparer.Ordinal)
    {
        TrainerFeatureIds.SuperPower,
        TrainerFeatureIds.DisableAllSecretProtocols,
        TrainerFeatureIds.SecretProtocolDependencyBypass,
        TrainerFeatureIds.GetBase,
        TrainerFeatureIds.CopySelectedUnit,
        TrainerFeatureIds.Reinforcement,
    };

    [Fact]
    public void EveryFeatureDisplayNameAppearsInSomeGroupOrSelectedUnitGrouping()
    {
        var allCoveredNames = TrainerFeatureGroupCatalog.Groups
            .SelectMany(g => g.FeatureDisplayNames)
            .Concat(TrainerFeatureGroupCatalog.SelectedUnitGroupingNames)
            .ToHashSet(StringComparer.Ordinal);

        var missingFeatures = Features
            .Where(f => !DedicatedUiFeatureRawNames.Contains(f.RawName))
            .Where(f => !allCoveredNames.Contains(f.DisplayName))
            .Select(f => $"{f.RawName} ({f.DisplayName})")
            .ToList();

        Assert.False(missingFeatures.Count > 0,
            $"{missingFeatures.Count} 个功能的 DisplayName 未出现在 Groups 或 SelectedUnitGroupingNames，将被静默丢弃：{string.Join("、", missingFeatures)}");
    }

    [Fact]
    public void ChallengeMoneyFeature_IsInSomeGroup()
    {
        var displayName = Features
            .First(f => f.RawName == "Challenge Money").DisplayName;

        var found = TrainerFeatureGroupCatalog.Groups
            .Any(g => g.FeatureDisplayNames.Contains(displayName, StringComparer.Ordinal));

        Assert.True(found, $"「{displayName}」未出现在任何分组的 FeatureDisplayNames 中");
    }

    [Fact]
    public void ChallengeTimeFeature_IsInSomeGroup()
    {
        var displayName = Features
            .First(f => f.RawName == "Challenge Time").DisplayName;

        var found = TrainerFeatureGroupCatalog.Groups
            .Any(g => g.FeatureDisplayNames.Contains(displayName, StringComparer.Ordinal));

        Assert.True(found, $"「{displayName}」未出现在任何分组的 FeatureDisplayNames 中");
    }

    [Fact]
    public void Groups_HaveUniqueNonEmptyGroupIds()
    {
        var ids = TrainerFeatureGroupCatalog.Groups.Select(g => g.GroupId).ToArray();
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SelectedUnitGroupIds_AreUniqueAndStable()
    {
        var ids = new[]
        {
            GroupIds.SelectedUnitDamage,
            GroupIds.SelectedUnitHealth,
            GroupIds.SelectedUnitSpeed,
            GroupIds.SelectedUnitOther
        };
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
