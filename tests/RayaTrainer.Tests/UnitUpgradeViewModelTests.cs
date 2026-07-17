using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class UnitUpgradeViewModelTests
{
    private static readonly TrainerFeature UnitUpgradeFeature =
        TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature;

    [Fact]
    public void NotReady_ReadyTransition_RaisesCanExecute()
    {
        var vm = CreateVm(capabilityState: FeatureCapabilityState.Waiting, reasonCode: "NO_TARGET");
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.GrantCommand.CanExecute(null));
        Assert.Equal(UnitUpgradeState.NotReady, vm.State);

        // Simulate capability becoming Ready
        SetCapability(vm, FeatureCapabilityState.Ready);
        vm.RaiseCommands();

        Assert.True(vm.RefreshCommand.CanExecute(null));
        // Grant still needs a non-null item
        Assert.False(vm.GrantCommand.CanExecute(null));
        Assert.Equal(UnitUpgradeState.Idle, vm.State);
    }

    [Fact]
    public async Task CapabilityUnavailable_CommandsDisabled_ControllerNotCalled()
    {
        var controller = new CapturingController();
        var vm = CreateVm(controller, FeatureCapabilityState.Unavailable,
            reasonCode: "UNIT_UPGRADE_PROFILE_NOT_SUPPORTED",
            reason: "单位升级功能当前仅在 RA3 1.12 完成验证。请使用 1.12 版本游戏。");

        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.GrantCommand.CanExecute(null));

        // Attempt refresh — controller should not be called
        await vm.RefreshUpgradesAsync();

        Assert.Equal(0, controller.ReadCallCount);
        Assert.Equal(UnitUpgradeState.NotReady, vm.State);
        Assert.Contains("不可用", vm.StatusMessage);
        Assert.Contains("1.12", vm.StatusMessage);
    }

    [Fact]
    public async Task NoSelection_ShowsDistinctStatusMessage()
    {
        var controller = new CapturingController
        {
            SnapshotToReturn = new SelectedUnitUpgradesSnapshot(0, 0, 0, Array.Empty<uint>())
        };
        var vm = CreateReadyVm(controller);

        await vm.RefreshUpgradesAsync();

        Assert.Equal(1, controller.ReadCallCount);
        Assert.Equal(UnitUpgradeState.Loaded, vm.State);
        Assert.Contains("请先在游戏中选中一个单位", vm.StatusMessage);
    }

    [Fact]
    public async Task NoUpgrades_ShowsDistinctStatusMessage()
    {
        var controller = new CapturingController
        {
            // ThingTemplateAddress non-zero but Count=0
            SnapshotToReturn = new SelectedUnitUpgradesSnapshot(42, 0x12345678, 0, Array.Empty<uint>())
        };
        var vm = CreateReadyVm(controller);

        await vm.RefreshUpgradesAsync();

        Assert.Equal(1, controller.ReadCallCount);
        Assert.Equal(UnitUpgradeState.Loaded, vm.State);
        Assert.Empty(vm.AvailableUpgrades);
        Assert.Contains("没有可授予的对象级升级", vm.StatusMessage);
    }

    [Fact]
    public async Task GrantCompleted_ShowsSuccessStatusMessage()
    {
        var controller = new CapturingController
        {
            SnapshotToReturn = new SelectedUnitUpgradesSnapshot(42, 0x12345678, 1,
                new uint[] { 0xAABBCCDD }),
            GrantResult = GameApiDispatchStatus.Completed
        };
        var vm = CreateReadyVm(controller);
        await vm.RefreshUpgradesAsync();
        Assert.Single(vm.AvailableUpgrades);

        await vm.GrantUpgradeAsync(vm.AvailableUpgrades[0]);

        Assert.Equal(UnitUpgradeState.Loaded, vm.State);
        Assert.True(vm.IsListVisible);
        Assert.Contains("已授予升级", vm.StatusMessage);
    }

    [Fact]
    public async Task GrantFailed_ShowsFailureStatusMessage()
    {
        var controller = new CapturingController
        {
            SnapshotToReturn = new SelectedUnitUpgradesSnapshot(42, 0x12345678, 1,
                new uint[] { 0xAABBCCDD }),
            GrantResult = GameApiDispatchStatus.Failed
        };
        var vm = CreateReadyVm(controller);
        await vm.RefreshUpgradesAsync();

        await vm.GrantUpgradeAsync(vm.AvailableUpgrades[0]);

        Assert.Equal(UnitUpgradeState.Loaded, vm.State);
        Assert.Contains("失败，请重试", vm.StatusMessage);
    }

    [Fact]
    public async Task GrantTimedOut_ShowsTimeoutStatusMessage()
    {
        var controller = new CapturingController
        {
            SnapshotToReturn = new SelectedUnitUpgradesSnapshot(42, 0x12345678, 1,
                new uint[] { 0xAABBCCDD }),
            GrantResult = GameApiDispatchStatus.TimedOut
        };
        var vm = CreateReadyVm(controller);
        await vm.RefreshUpgradesAsync();

        await vm.GrantUpgradeAsync(vm.AvailableUpgrades[0]);

        Assert.Equal(UnitUpgradeState.Loaded, vm.State);
        Assert.Contains("超时，游戏可能已暂停", vm.StatusMessage);
    }

    [Fact]
    public async Task Loading_PreventsReentrantRefreshAndGrant()
    {
        var controller = new CapturingController
        {
            // Use a real slow controller that blocks until released
            SnapshotToReturn = new SelectedUnitUpgradesSnapshot(42, 0x12345678, 0, Array.Empty<uint>())
        };
        var vm = CreateReadyVm(controller);

        // Start first refresh but don't await it yet
        var firstTask = vm.RefreshUpgradesAsync();

        // Now commands should be disabled
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.GrantCommand.CanExecute(null));

        // Wait for completion
        await firstTask;

        // Commands re-enabled
        Assert.True(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.GrantCommand.CanExecute(null)); // No items
    }

    [Fact]
    public async Task ExceptionRefresh_CollapsesToErrorState()
    {
        var controller = new CapturingController
        {
            ThrowOnRead = true
        };
        var vm = CreateReadyVm(controller);

        await vm.RefreshUpgradesAsync();

        Assert.Equal(UnitUpgradeState.Error, vm.State);
        Assert.Contains("刷新升级列表失败", vm.StatusMessage);
    }

    [Fact]
    public async Task ExceptionGrant_CollapsesToErrorState()
    {
        var controller = new CapturingController
        {
            SnapshotToReturn = new SelectedUnitUpgradesSnapshot(42, 0x12345678, 1,
                new uint[] { 0xAABBCCDD }),
            ThrowOnGrant = true
        };
        var vm = CreateReadyVm(controller);
        await vm.RefreshUpgradesAsync();

        await vm.GrantUpgradeAsync(vm.AvailableUpgrades[0]);

        Assert.Equal(UnitUpgradeState.Error, vm.State);
        Assert.Contains("授予升级失败", vm.StatusMessage);
    }

    // --- Helpers ---

    private static UnitUpgradeViewModel CreateVm(
        CapturingController? controller = null,
        FeatureCapabilityState capabilityState = FeatureCapabilityState.Ready,
        string reasonCode = "READY",
        string reason = "就绪")
    {
        var cap = new FeatureCapabilitySnapshot(
            UnitUpgradeFeature.RawName,
            UnitUpgradeFeature.DisplayName,
            "其他",
            capabilityState,
            reasonCode,
            reason);

        return new UnitUpgradeViewModel(
            () => controller ?? new CapturingController(),
            _ => cap);
    }

    private static UnitUpgradeViewModel CreateReadyVm(CapturingController controller)
    {
        return CreateVm(controller, FeatureCapabilityState.Ready, "READY", "就绪");
    }

    /// <summary>
    /// Replaces the capability snapshot returned by the VM's capability getter
    /// by creating a new VM. (The VM doesn't expose a setter for the delegate.)
    /// </summary>
    private static void SetCapability(UnitUpgradeViewModel vm, FeatureCapabilityState state,
        string reasonCode = "READY", string reason = "就绪")
    {
        // Use reflection to swap out the _getCapability delegate.
        // This is the same pattern used by ReflectionHelper in TestDoubles.cs.
        var field = typeof(UnitUpgradeViewModel).GetField(
            "_getCapability",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        ArgumentNullException.ThrowIfNull(field);

        var cap = new FeatureCapabilitySnapshot(
            UnitUpgradeFeature.RawName,
            UnitUpgradeFeature.DisplayName,
            "其他",
            state,
            reasonCode,
            reason);
        field.SetValue(vm, (Func<TrainerFeature, FeatureCapabilitySnapshot>)(_ => cap));
    }

    /// <summary>
    /// Controller stub that records calls and returns configurable results.
    /// </summary>
    private sealed class CapturingController : ITrainerFeatureController
    {
        public int ReadCallCount { get; private set; }
        public int GrantCallCount { get; private set; }
        public SelectedUnitUpgradesSnapshot SnapshotToReturn { get; set; } = SelectedUnitUpgradesSnapshot.Empty;
        public GameApiDispatchStatus GrantResult { get; set; } = GameApiDispatchStatus.Completed;
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnGrant { get; set; }

        public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades()
        {
            ReadCallCount++;
            if (ThrowOnRead) throw new InvalidOperationException("模拟读取失败");
            return SnapshotToReturn;
        }

        public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null)
        {
            GrantCallCount++;
            if (ThrowOnGrant) throw new InvalidOperationException("模拟授予失败");
            return GrantResult;
        }

        public void SetToggle(TrainerFeature feature, bool enabled) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature feature) => throw new NotImplementedException();
        public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings) => throw new NotImplementedException();
        public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
            TrainerFeature feature, ReinforcementSettings? reinforcementSettings = null,
            TimeSpan? timeout = null, TimeSpan? pollInterval = null,
            Action? onDispatched = null, CancellationToken cancellationToken = default,
            TimeSpan? pausedGracePeriod = null, Action<DispatchWaitStatus>? onWaitStatusChanged = null)
            => throw new NotImplementedException();
        public void WriteReinforcementSettings(ReinforcementSettings settings) => throw new NotImplementedException();
        public void WriteResourceValues(ResourceValueSettings settings) => throw new NotImplementedException();
        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings) => throw new NotImplementedException();
        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings) => throw new NotImplementedException();
        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings) => throw new NotImplementedException();
        public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotImplementedException();
        public void PulseAutoRepair() => throw new NotImplementedException();
        public void ClearAutoRepairPulse() => throw new NotImplementedException();
        public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f) => throw new NotImplementedException();
        public uint ReadSelectedUnitCode() => throw new NotImplementedException();
        public byte ReadActionDispatch() => throw new NotImplementedException();
        public uint ReadGameThreadTick() => throw new NotImplementedException();
        public int ReadGameMode() => throw new NotImplementedException();
        public void Reset(TrainerFeature feature) => throw new NotImplementedException();
        public bool? ReadToggleState(TrainerFeature feature) => throw new NotImplementedException();
        public bool? ReadPulseFired(TrainerFeature feature) => throw new NotImplementedException();
        public bool IsPulseFeature(TrainerFeature feature) => false;
        public Task<FeatureStatesResponse> RefreshRuntimeStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new FeatureStatesResponse(AgentStatusCode.Ok, AgentProtocol.Version, Array.Empty<FeatureStateEntry>()));
    }
}
