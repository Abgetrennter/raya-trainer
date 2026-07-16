using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class FeatureStateCoordinatorTests
{
    [Fact]
    public void ReplayDesiredState_AppliesAllDesiredTrueToggles()
    {
        var (coord, items, controller) = CreateCoordinator();
        // 设两个 desired=true（模拟启动恢复后的状态）
        items["Power"].SetDesired(true, suppressApply: true);
        items["MAP"].SetDesired(true, suppressApply: true);

        controller.IsReady = true;
        coord.ReplayDesiredState();

        Assert.True(items["Power"].ObservedEnabled); // 已应用
        Assert.True(items["MAP"].ObservedEnabled);
        Assert.Equal(2, controller.SetToggleCalls);
    }

    [Fact]
    public void ReplayDesiredState_SkipsDesiredFalseOrNull()
    {
        var (coord, items, controller) = CreateCoordinator();
        items["Power"].SetDesired(true, suppressApply: true);
        // MAP desired=null（未设过）

        controller.IsReady = true;
        coord.ReplayDesiredState();

        Assert.Equal(1, controller.SetToggleCalls); // 只 Power
    }

    [Fact]
    public void ReplayDesiredState_ControllerNull_NoOp()
    {
        var (coord, items, controller) = CreateCoordinator();
        items["Power"].SetDesired(true, suppressApply: true);
        controller.IsReady = false; // controller 不可用

        coord.ReplayDesiredState();

        Assert.Equal(0, controller.SetToggleCalls);
    }

    [Fact]
    public void ReplayDesiredState_UnavailableCapability_SkipsItem()
    {
        var (coord, items, controller) = CreateCoordinator();
        items["Power"].SetDesired(true, suppressApply: true);
        controller.IsReady = true;
        controller.PowerCapability = FeatureCapabilityState.Unavailable; // 该功能不可用

        coord.ReplayDesiredState();

        Assert.Equal(0, controller.SetToggleCalls); // 跳过不可用
    }

    [Fact]
    public void CaptureSnapshot_IncludesDesiredTogglesAndProviderParams()
    {
        var (coord, items, controller) = CreateCoordinatorWithProvider();
        items["Power"].SetDesired(true, suppressApply: true);

        var snap = coord.CaptureSnapshot();

        Assert.True(snap.ToggleStates["Power"]);
        // provider 参数（若注入）
        Assert.Contains("resources.moneyAmount", snap.ParameterValues.Keys);
    }

    [Fact]
    public void ApplySnapshot_SuppressRuntime_RestoresDesiredAndParams()
    {
        var (coord, items, controller) = CreateCoordinatorWithProvider();
        var snap = new FeatureStateSnapshot(
            new Dictionary<string, bool> { ["Power"] = true },
            new Dictionary<string, string> { ["resources.powerValue"] = "777" });

        var result = coord.ApplySnapshot(snap, suppressRuntimeApply: true);

        Assert.True(items["Power"].DesiredEnabled);
        Assert.Null(items["Power"].ObservedEnabled); // suppress → 未应用
        Assert.Contains("Power", result.AppliedToggles);
    }

    [Fact]
    public void ApplySnapshot_Apply_EnablesReadyToggles()
    {
        var (coord, items, controller) = CreateCoordinatorWithProvider();
        controller.IsReady = true;
        var snap = new FeatureStateSnapshot(
            new Dictionary<string, bool> { ["Power"] = true },
            new Dictionary<string, string>());

        var result = coord.ApplySnapshot(snap, suppressRuntimeApply: false);

        Assert.True(items["Power"].ObservedEnabled);
        Assert.Contains("Power", result.AppliedToggles);
    }

    [Fact]
    public void ApplySnapshot_UnavailableCapability_SkipsAndReports()
    {
        var (coord, items, controller) = CreateCoordinatorWithProvider();
        controller.IsReady = true;
        controller.PowerCapability = FeatureCapabilityState.Unavailable;
        var snap = new FeatureStateSnapshot(
            new Dictionary<string, bool> { ["Power"] = true },
            new Dictionary<string, string>());

        var result = coord.ApplySnapshot(snap, suppressRuntimeApply: false);

        Assert.Empty(result.AppliedToggles);
        Assert.Contains("Power", result.SkippedToggles);
    }

    // Helpers

    private static (FeatureStateCoordinator coord, Dictionary<string, FeatureItemViewModel> items, FakeController controller) CreateCoordinator()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);
        var controller = new FakeController();
        var host = new FakeHost(controller);
        var items = features.ToDictionary(f => f.RawName, f => new FeatureItemViewModel(f, host));
        var coord = new FeatureStateCoordinator(
            () => items.Values,
            () => controller.IsReady ? controller : null,
            f => new FeatureCapabilitySnapshot(
                f.RawName, f.DisplayName, "test",
                f.RawName == "Power" ? controller.PowerCapability : FeatureCapabilityState.Ready,
                "READY", "就绪"),
            Array.Empty<IFeatureParameterProvider>());
        return (coord, items, controller);
    }

    private static (FeatureStateCoordinator coord, Dictionary<string, FeatureItemViewModel> items, FakeController controller) CreateCoordinatorWithProvider()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);
        var controller = new FakeController();
        var host = new FakeHost(controller);
        var items = features.ToDictionary(f => f.RawName, f => new FeatureItemViewModel(f, host));
        var providers = new List<IFeatureParameterProvider> { new FakeProvider() };
        var coord = new FeatureStateCoordinator(
            () => items.Values,
            () => controller.IsReady ? controller : null,
            f => new FeatureCapabilitySnapshot(
                f.RawName, f.DisplayName, "test",
                f.RawName == "Power" ? controller.PowerCapability : FeatureCapabilityState.Ready,
                "READY", "就绪"),
            providers);
        return (coord, items, controller);
    }

    private sealed class FakeProvider : IFeatureParameterProvider
    {
        public string ProviderId => "test";
        public IReadOnlyCollection<string> ParameterIds => new[] { "resources.moneyAmount", "resources.powerValue" };
        public event EventHandler? ValidValueChanged;
        public IReadOnlyDictionary<string, string> CaptureValidated() =>
            new Dictionary<string, string> { ["resources.moneyAmount"] = "9999" };
        public ParameterRestoreResult RestoreValidated(IReadOnlyDictionary<string, string> values, bool suppressRuntimeApply) =>
            new ParameterRestoreResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
    }

    private sealed class FakeHost : IFeatureHost
    {
        public FakeHost(ITrainerFeatureController? c) => FeatureController = c;
        public bool ArePatchesInstalled => true;
        public ITrainerFeatureController? FeatureController { get; }
        public string StatusMessage { set { } }
        public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature f) =>
            new(f.RawName, f.DisplayName, "test", FeatureCapabilityState.Ready, "READY", "就绪");
        public void WriteResourceValuesIfNeeded(TrainerFeature f) { }
        public void WriteTargetHealthIfNeeded(TrainerFeature f) { }
        public void OnFeatureToggleChanged(TrainerFeature f, bool e) { }
        public void CompleteActionIfNeeded(TrainerFeature f, ActionDispatchResult r) { }
        public ReinforcementSettings GetReinforcementSettings() => default!;
        public void OpenHotkeySettings() { }
        public void ClearHotkey(TrainerFeature f) { }
    }

    private sealed class FakeController : ITrainerFeatureController
    {
        public bool IsReady = true;
        public FeatureCapabilityState PowerCapability = FeatureCapabilityState.Ready;
        public int SetToggleCalls;
        public bool LastToggleState;
        public void SetToggle(TrainerFeature f, bool e) { SetToggleCalls++; LastToggleState = e; }
        public bool ReadToggleState(TrainerFeature f) => LastToggleState;
        public void WriteTargetHealthValue(float h, float m = 0f) { }
        public void WriteResourceValues(ResourceValueSettings s) { }
        public void WriteReinforcementSettings(ReinforcementSettings s) => throw new NotImplementedException();
        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings s) => throw new NotImplementedException();
        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings s) => throw new NotImplementedException();
        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings s) => throw new NotImplementedException();
        public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotImplementedException();
        public void PulseAutoRepair() { }
        public void ClearAutoRepairPulse() { }
        public uint ReadSelectedUnitCode() => throw new NotImplementedException();
        public byte ReadActionDispatch() => throw new NotImplementedException();
        public uint ReadGameThreadTick() => throw new NotImplementedException();
        public int ReadGameMode() => throw new NotImplementedException();
        public void Reset(TrainerFeature f) { }
        public void TriggerAction(TrainerFeature f) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature f, ReinforcementSettings? s) => throw new NotImplementedException();
        public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
            TrainerFeature f, ReinforcementSettings? s = null, TimeSpan? timeout = null,
            TimeSpan? poll = null, Action? onDispatched = null, CancellationToken ct = default,
            TimeSpan? grace = null, Action<DispatchWaitStatus>? onWait = null) => throw new NotImplementedException();
        public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades() => throw new NotImplementedException();
        public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint h, TimeSpan? t = null) => throw new NotImplementedException();
    }
}
