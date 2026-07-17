using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class FeatureItemViewModelDesiredObservedTests
{
    private static TrainerFeature ToggleFeature =>
        TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features)
            .First(f => f.RawName == "Power");

    [Fact]
    public void Initial_DesiredNull_ObservedNull_StatusNotEnabled()
    {
        var (vm, _, _) = CreateVm();
        Assert.Null(vm.DesiredEnabled);
        Assert.Null(vm.ObservedEnabled);
        Assert.False(vm.IsFeatureEnabled);
    }

    [Fact]
    public void SetDesired_True_SuppressApply_RecordsDesired_DoesNotCallController()
    {
        var (vm, controller, _) = CreateVm();
        vm.SetDesired(true, suppressApply: true);

        Assert.True(vm.DesiredEnabled);
        Assert.Equal(0, controller.SetToggleCalls);
        Assert.False(vm.IsFeatureEnabled); // observed 仍 null → 不显示已生效
    }

    [Fact]
    public void SetDesired_True_Apply_CallsController_SetsObserved()
    {
        var (vm, controller, _) = CreateVm();
        vm.SetDesired(true, suppressApply: false);

        Assert.True(vm.DesiredEnabled);
        Assert.True(vm.ObservedEnabled);
        Assert.Equal(1, controller.SetToggleCalls);
        Assert.True(vm.IsFeatureEnabled);
    }

    [Fact]
    public void ClearObserved_KeepsDesired()
    {
        var (vm, _, _) = CreateVm();
        vm.SetDesired(true, suppressApply: true);
        vm.ClearObserved();

        Assert.True(vm.DesiredEnabled);
        Assert.Null(vm.ObservedEnabled);
    }

    [Fact]
    public void RefreshObserved_UpdatesObserved_NotDesired()
    {
        var (vm, controller, _) = CreateVm();
        vm.SetDesired(true, suppressApply: true);
        controller.ToggleState = false; // Agent 实际未开

        vm.RefreshObserved();

        Assert.True(vm.DesiredEnabled);
        Assert.False(vm.ObservedEnabled);
        Assert.False(vm.IsFeatureEnabled);
    }

    [Fact]
    public void ActionText_ReflectsDesiredWhenObservedNull()
    {
        var (vm, _, _) = CreateVm();
        vm.SetDesired(true, suppressApply: true);
        // 待连接：desired=true, observed=null → 显示"关闭"（用户可点关）
        Assert.Equal("关闭", vm.ActionText);
    }

    private static (FeatureItemViewModel vm, CapturingController controller, FakeHost host) CreateVm()
    {
        var controller = new CapturingController();
        var host = new FakeHost(controller);
        var vm = new FeatureItemViewModel(ToggleFeature, host);
        return (vm, controller, host);
    }

    private sealed class FakeHost : IFeatureHost
    {
        public FakeHost(ITrainerFeatureController? controller) => FeatureController = controller;
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
        public int SetToggleCalls;
        public bool ToggleState = true;
        public void SetToggle(TrainerFeature f, bool e) { SetToggleCalls++; ToggleState = e; }
        public bool? ReadToggleState(TrainerFeature f) => ToggleState;
        public bool? ReadPulseFired(TrainerFeature f) => null;
        public bool IsPulseFeature(TrainerFeature f) => false;
        public Task<FeatureStatesResponse> RefreshRuntimeStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new FeatureStatesResponse(AgentStatusCode.Ok, AgentProtocol.Version, Array.Empty<FeatureStateEntry>()));
        public void WriteTargetHealthValue(float h, float m = 0f) { }
        public void WriteResourceValues(ResourceValueSettings s) { }
        public void WriteReinforcementSettings(ReinforcementSettings s) { }
        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings s) { }
        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings s) { }
        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings s) { }
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
