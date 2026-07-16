using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class FeatureParameterCatalogTests
{
    [Fact]
    public void Definitions_HaveUniqueNonEmptyIds()
    {
        var ids = FeatureParameterCatalog.Definitions.Select(d => d.Id).ToArray();
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Definitions_OwnerRawName_ExistsInFeatureCatalog()
    {
        var allRawNames = TrainerFeatureCatalog.CreateGridFeatures(
            TestAssets.LoadManifest().Features).Select(f => f.RawName).ToHashSet();

        foreach (var def in FeatureParameterCatalog.Definitions)
        {
            Assert.True(allRawNames.Contains(def.OwnerFeatureRawName),
                $"参数 {def.Id} 的 OwnerRawName '{def.OwnerFeatureRawName}' 不在功能目录中");
        }
    }

    [Fact]
    public void Definitions_DefaultValue_ParsesAccordingToValueKind()
    {
        foreach (var def in FeatureParameterCatalog.Definitions)
        {
            var ok = def.ValueKind switch
            {
                FeatureParameterValueKind.Integer => int.TryParse(def.DefaultValue, out _),
                FeatureParameterValueKind.Float => float.TryParse(def.DefaultValue, out _),
                FeatureParameterValueKind.String => true,
                _ => false
            };
            Assert.True(ok, $"参数 {def.Id} 默认值 '{def.DefaultValue}' 无法按 {def.ValueKind} 解析");
        }
    }

    [Fact]
    public void TryFind_ReturnsDefinition_WhenIdExists()
    {
        var def = FeatureParameterCatalog.TryFind("resources.moneyAmount");
        Assert.NotNull(def);
        Assert.Equal(FeatureParameterApplyMode.OnAction, def!.ApplyMode);
    }

    [Fact]
    public void TryFind_ReturnsNull_WhenIdMissing()
    {
        Assert.Null(FeatureParameterCatalog.TryFind("nonexistent.param"));
    }

    [Fact]
    public void Validate_OutOfRange_ReturnsFalse()
    {
        var def = FeatureParameterCatalog.TryFind("resources.scPointValue")!;
        Assert.False(def.Validate("-1"));
        Assert.False(def.Validate("16"));
        Assert.True(def.Validate("10"));
    }
}
