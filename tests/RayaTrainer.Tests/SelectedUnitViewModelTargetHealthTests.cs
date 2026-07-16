using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class SelectedUnitViewModelTargetHealthTests
{
    private static IReadOnlyList<TrainerFeature> Features =>
        TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);

    private static readonly TrainerFeature TargetHealthFeature =
        Features.First(f => f.RawName == TrainerFeatureIds.SetSelectedUnitTargetHealth);

    [Fact]
    public void WriteTargetHealthIfNeeded_ValidText_CallsController()
    {
        var controller = new CapturingController();
        var host = new FakeFeatureHost(controller);
        var vm = CreateVm(host);

        vm.SelectedUnitTargetHealthText = "5000";
        vm.SelectedUnitTargetMaxHealthText = "10000";

        vm.WriteTargetHealthIfNeeded(TargetHealthFeature);

        Assert.Equal(1, controller.WriteTargetHealthCallCount);
        Assert.Equal(5000f, controller.LastTargetHealth);
        Assert.Equal(10000f, controller.LastTargetMaxHealth);
    }

    [Fact]
    public void WriteTargetHealthIfNeeded_EmptyText_DoesNotCall()
    {
        var controller = new CapturingController();
        var host = new FakeFeatureHost(controller);
        var vm = CreateVm(host);

        vm.SelectedUnitTargetHealthText = "";
        vm.WriteTargetHealthIfNeeded(TargetHealthFeature);

        Assert.Equal(0, controller.WriteTargetHealthCallCount);
    }

    [Fact]
    public void WriteTargetHealthIfNeeded_NonMatchingFeature_DoesNotCall()
    {
        var controller = new CapturingController();
        var host = new FakeFeatureHost(controller);
        var vm = CreateVm(host);

        vm.SelectedUnitTargetHealthText = "5000";
        var otherFeature = Features.First(
            f => f.RawName != TrainerFeatureIds.SetSelectedUnitTargetHealth);
        vm.WriteTargetHealthIfNeeded(otherFeature);

        Assert.Equal(0, controller.WriteTargetHealthCallCount);
    }

    [Fact]
    public void Properties_RaisePropertyChanged()
    {
        var controller = new CapturingController();
        var host = new FakeFeatureHost(controller);
        var vm = CreateVm(host);

        bool healthChanged = false, maxHealthChanged = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedUnitTargetHealthText)) healthChanged = true;
            if (e.PropertyName == nameof(vm.SelectedUnitTargetMaxHealthText)) maxHealthChanged = true;
        };

        vm.SelectedUnitTargetHealthText = "100";
        vm.SelectedUnitTargetMaxHealthText = "200";

        Assert.True(healthChanged);
        Assert.True(maxHealthChanged);
    }

    [Fact]
    public void ClearAttackSpeedEffects_IsInSomeGroup()
    {
        var displayName = Features
            .First(f => f.RawName == "Clear Selected Attack Speed Effects").DisplayName;

        var host = new FakeFeatureHost(new CapturingController());
        var vm = CreateVm(host);

        var found = vm.Groups
            .Any(g => g.Features.Any(f => f.DisplayName == displayName));

        Assert.True(found, $"「{displayName}」未出现在 SelectedUnitViewModel 的任何分组中");
    }

    [Fact]
    public void ClearAttackRangeEffects_IsInSomeGroup()
    {
        var displayName = Features
            .First(f => f.RawName == "Clear Selected Attack Range Effects").DisplayName;

        var host = new FakeFeatureHost(new CapturingController());
        var vm = CreateVm(host);

        var found = vm.Groups
            .Any(g => g.Features.Any(f => f.DisplayName == displayName));

        Assert.True(found, $"「{displayName}」未出现在 SelectedUnitViewModel 的任何分组中");
    }

    // --- Helpers ---

    private static SelectedUnitViewModel CreateVm(IFeatureHost host)
    {
        return new SelectedUnitViewModel(
            host,
            Features.ToList(),
            () => host.FeatureController,
            _ => new FeatureCapabilitySnapshot(
                "test", "test", "test",
                FeatureCapabilityState.Ready, "READY", "就绪"));
    }

    private sealed class FakeFeatureHost : IFeatureHost
    {
        public FakeFeatureHost(ITrainerFeatureController? controller) => FeatureController = controller;
        public bool ArePatchesInstalled => true;
        public ITrainerFeatureController? FeatureController { get; }
        public string StatusMessage { set { } }
        public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature f) =>
            new("test", "test", "test", FeatureCapabilityState.Ready, "READY", "就绪");
        public void WriteResourceValuesIfNeeded(TrainerFeature f) { }
        public void WriteTargetHealthIfNeeded(TrainerFeature f) { }
        public void OnFeatureToggleChanged(TrainerFeature f, bool e) { }
        public void CompleteActionIfNeeded(TrainerFeature f, ActionDispatchResult r) { }
        public ReinforcementSettings GetReinforcementSettings() => default!;
        public void OpenHotkeySettings() { }
        public void ClearHotkey(TrainerFeature f) { }
    }

    private sealed class CapturingController : ITrainerFeatureController
    {
        public int WriteTargetHealthCallCount { get; private set; }
        public float LastTargetHealth { get; private set; }
        public float LastTargetMaxHealth { get; private set; }

        public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f)
        {
            WriteTargetHealthCallCount++;
            LastTargetHealth = targetHealth;
            LastTargetMaxHealth = targetMaxHealth;
        }

        // Unused — throw like existing CapturingController pattern
        public void SetToggle(TrainerFeature f, bool e) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature f) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature f, ReinforcementSettings? s) => throw new NotImplementedException();
        public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
            TrainerFeature f, ReinforcementSettings? s = null, TimeSpan? timeout = null,
            TimeSpan? poll = null, Action? onDispatched = null, CancellationToken ct = default,
            TimeSpan? grace = null, Action<DispatchWaitStatus>? onWait = null) => throw new NotImplementedException();
        public void WriteReinforcementSettings(ReinforcementSettings s) => throw new NotImplementedException();
        public void WriteResourceValues(ResourceValueSettings s) => throw new NotImplementedException();
        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings s) => throw new NotImplementedException();
        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings s) => throw new NotImplementedException();
        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings s) => throw new NotImplementedException();
        public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotImplementedException();
        public void PulseAutoRepair() => throw new NotImplementedException();
        public void ClearAutoRepairPulse() => throw new NotImplementedException();
        public uint ReadSelectedUnitCode() => throw new NotImplementedException();
        public byte ReadActionDispatch() => throw new NotImplementedException();
        public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades() => throw new NotImplementedException();
        public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null) => throw new NotImplementedException();
        public uint ReadGameThreadTick() => throw new NotImplementedException();
        public int ReadGameMode() => throw new NotImplementedException();
        public void Reset(TrainerFeature f) => throw new NotImplementedException();
        public bool ReadToggleState(TrainerFeature f) => throw new NotImplementedException();
    }
}
