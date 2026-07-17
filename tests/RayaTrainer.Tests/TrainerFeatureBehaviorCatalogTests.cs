using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerFeatureBehaviorCatalogTests
{
    /// <summary>
    /// Every RawName from the manifest (SourceTrainer overrides) + statically
    /// defined features must have a behavior entry.
    /// </summary>
    [Fact]
    public void EveryLiveFeatureRawName_HasBehaviorEntry()
    {
        var manifest = TestAssets.LoadManifest();
        var uiFeatures = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);
        var panelActions = TrainerFeatureCatalog.CreatePanelActions();
        var allLive = uiFeatures.Concat(panelActions).Select(f => f.RawName).Distinct().ToHashSet();

        foreach (var rawName in allLive)
        {
            var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior(rawName);
            Assert.True(behavior is not null,
                $"Missing behavior entry for live feature RawName '{rawName}'.");
        }
    }

    [Fact]
    public void NoDuplicateRawNames()
    {
        // The catalog constructor throws on duplicates; this just verifies it loaded.
        var all = TrainerFeatureBehaviorCatalog.All;
        Assert.NotNull(all);
        Assert.NotEmpty(all);
    }

    [Fact]
    public void NativeToggleBindings_HaveStateId()
    {
        foreach (var entry in TrainerFeatureBehaviorCatalog.All)
        {
            var toggle = entry.AsNativeToggle();
            if (toggle is null) continue;

            Assert.True(toggle.Value.StateId.HasValue,
                $"NativeToggle '{entry.RawName}' has no StateId.");
            Assert.NotEqual(0u, toggle.Value.StateId.Value);
        }
    }

    [Fact]
    public void NativePulseBindings_HaveStateIdInExpectedSet()
    {
        var knownPulseStates = new HashSet<uint> { 1, 13, 21, 22, 23 };

        foreach (var entry in TrainerFeatureBehaviorCatalog.All)
        {
            var pulse = entry.AsNativePulse();
            if (pulse is null) continue;

            Assert.Contains(pulse.Value.StateId, knownPulseStates);
        }
    }

    [Fact]
    public void NativeActionBindings_HaveActionIdInRange()
    {
        foreach (var entry in TrainerFeatureBehaviorCatalog.All)
        {
            var action = entry.AsNativeAction();
            if (action is null) continue;

            Assert.InRange(action.Value.ActionId, 8u, 47u);
        }
    }

    [Fact]
    public void FrameRateUnlock_HasCompositeBinding()
    {
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior("Frame Rate Unlock 60fps");
        Assert.NotNull(behavior);

        var toggle = behavior.AsNativeToggle();
        Assert.NotNull(toggle);
        Assert.Equal(20u, toggle.Value.StateId);
        Assert.Equal(1u, toggle.Value.PatchSetId);
        Assert.Equal(41u, toggle.Value.HookId);
    }

    [Fact]
    public void MoneyUsesCorrectedRawName()
    {
        // Typo "Moeny" is now fixed to "Money" — catalog uses corrected name.
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior("Money");
        Assert.NotNull(behavior);
        Assert.Equal("Money", behavior.RawName);

        // Verify no catalog entry exists for old typo
        Assert.Null(TrainerFeatureBehaviorCatalog.TryGetBehavior("Moeny"));
    }

    [Fact]
    public void TryGetBehavior_UnknownRawName_ReturnsNull()
    {
        Assert.Null(TrainerFeatureBehaviorCatalog.TryGetBehavior("__NONEXISTENT_FEATURE__"));
    }

    [Fact]
    public void AllToggles_HaveMatchingStateIdInNativeFeatureStateId()
    {
        var knownStateIds = Enum.GetValues<NativeFeatureStateId>()
            .Cast<uint>()
            .ToHashSet();

        foreach (var entry in TrainerFeatureBehaviorCatalog.All)
        {
            var toggle = entry.AsNativeToggle();
            if (toggle is null) continue;

            Assert.True(toggle.Value.StateId.HasValue);
            Assert.Contains(toggle.Value.StateId.Value, knownStateIds);
        }
    }

    [Fact]
    public void Catalog_IsFrozenAtConstruction()
    {
        // All collection returns a frozen dictionary's Values — should be read-only.
        var all = TrainerFeatureBehaviorCatalog.All;
        Assert.NotNull(all);

        // Verify the AllByRawName dictionary is a frozen dictionary
        var allByRawName = TrainerFeatureBehaviorCatalog.AllByRawName;
            Assert.IsAssignableFrom<System.Collections.Frozen.FrozenDictionary<string, TrainerFeatureBehavior>>(
            allByRawName);
    }

    [Fact]
    public void DestorySelectUnit_PreservesTypoInValue()
    {
        var behavior = TrainerFeatureBehaviorCatalog.TryGetBehavior("Destory Select Unit");
        Assert.NotNull(behavior);
        Assert.Equal("Destory Select Unit", behavior.RawName);
    }
}
