using RayaTrainer.App.Services;
using RayaTrainer.App.Web;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerApiHandlerFeaturePresetTests
{
    [Fact]
    public async Task GetFeaturePresets_ReturnsListFromSource()
    {
        var (handler, presetSource) = CreateHandler();
        presetSource.AddTestPreset("战斗", new FeatureStateSnapshot(
            new Dictionary<string, bool> { ["Power"] = true },
            new Dictionary<string, string>()));

        var result = await handler.GetFeaturePresets();

        Assert.Single(result.Presets);
        Assert.Equal("战斗", result.Presets[0].Name);
    }

    [Fact]
    public async Task PostFeaturePreset_SavesViaSource()
    {
        var (handler, presetSource) = CreateHandler();
        var request = new FeaturePresetSaveRequest("防守", FeatureStateSnapshot.Empty);

        var result = await handler.SaveFeaturePreset(request);

        Assert.True(result.Success);
        Assert.Contains("防守", presetSource.GetFeaturePresets().Select(p => p.Name));
    }

    [Fact]
    public async Task DeleteFeaturePreset_RemovesFromSource()
    {
        var (handler, presetSource) = CreateHandler();
        presetSource.AddTestPreset("临时", FeatureStateSnapshot.Empty);

        await handler.DeleteFeaturePreset("临时");

        Assert.Empty(presetSource.GetFeaturePresets());
    }

    private static (TrainerApiHandler Handler, FakeFeaturePresetSource PresetSource) CreateHandler()
    {
        var presetSource = new FakeFeaturePresetSource();
        var handler = new TrainerApiHandler(
            new StubSessionService(),
            new GameApiCommandQueue(),
            Array.Empty<TrainerFeature>(),
            presetSource: presetSource);
        return (handler, presetSource);
    }

    private sealed class StubSessionService : ITrainerSessionService
    {
        public ITrainerFeatureController? FeatureController => null;
        public bool ArePatchesInstalled => false;
        public int? TargetProcessId => null;
        public bool CanUseFeatures => false;
        public int InstalledHookCount => 0;
        public string RemoteSymbolSummary => string.Empty;

        public AttachResult AttachTarget(TrainerManifest manifest, TrainerTarget target) =>
            throw new NotSupportedException();
        public SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir) =>
            throw new NotSupportedException();
        public void ResetPatchesState() { }
        public void MarkTargetOffline() { }
        public bool IsTargetGameForeground() => false;
        public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature) =>
            new(feature.RawName, feature.DisplayName, string.Empty,
                FeatureCapabilityState.Ready, null, null);
        public void Dispose() { }
    }

    private sealed class FakeFeaturePresetSource : ITrainerPresetSource
    {
        private readonly List<FeaturePreset> _featurePresets = new();

        public IReadOnlyList<ReinforcementPreset> GetReinforcementPresets() =>
            Array.Empty<ReinforcementPreset>();

        public IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets() =>
            Array.Empty<SecretProtocolQueuePreset>();

        public IReadOnlyList<FeaturePreset> GetFeaturePresets() => _featurePresets;

        public void SaveFeaturePreset(string name, FeatureStateSnapshot snapshot)
        {
            var existing = _featurePresets.FirstOrDefault(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                var idx = _featurePresets.IndexOf(existing);
                _featurePresets[idx] = existing with { Snapshot = snapshot, UpdatedAtUtc = DateTimeOffset.UtcNow };
            }
            else
            {
                _featurePresets.Add(new FeaturePreset(name, snapshot,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }
        }

        public bool DeleteFeaturePreset(string name)
        {
            var removed = _featurePresets.RemoveAll(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }

        public void AddTestPreset(string name, FeatureStateSnapshot snapshot)
        {
            _featurePresets.Add(new FeaturePreset(name, snapshot,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }
}
