using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TemplateReplacementPanelViewModelTests
{
    private static readonly TrainerFeature ModelFeature =
        new("Replace Template Model", "替换单位模板模型", null, [], "MustCode+2200", "0x14");

    private static readonly TrainerFeature WeaponFeature =
        new("Replace Template Weapon", "替换单位模板武器", null, [], "MustCode+2400", "0x15");

    [Fact]
    public void SharedTemplateUnitFieldsParseModelAndWeaponSettings()
    {
        var panel = CreatePanel(new FakeFeatureController());

        panel.TargetUnitIdText = "0x11111111";
        panel.DonorUnitIdText = "22222222";

        var model = panel.GetTemplateModelReplacementSettings();
        var weapon = panel.GetTemplateWeaponReplacementSettings();

        Assert.Equal(0x11111111u, model.TargetUnitId);
        Assert.Equal(0x22222222u, model.DonorUnitId);
        Assert.Equal(0x11111111u, weapon.TargetUnitId);
        Assert.Equal(0x22222222u, weapon.DonorUnitId);
        Assert.Contains("被替换模板", panel.TargetUnitIdHelpText);
        Assert.Contains("来源模板", panel.DonorUnitIdHelpText);
        Assert.Contains("不复制武器", panel.ReplaceTemplateModelHelpText);
        Assert.Contains("WeaponSetUpdate", panel.ReplaceTemplateWeaponHelpText);
    }

    [Fact]
    public async Task ReplaceModelAsyncWritesModelSettingsAndDispatchesModelFeature()
    {
        var controller = new FakeFeatureController { DispatchResult = ActionDispatchResult.Consumed };
        var status = string.Empty;
        var panel = CreatePanel(controller, message => status = message);
        panel.TargetUnitIdText = "0x11111111";
        panel.DonorUnitIdText = "0x22222222";

        await panel.ReplaceModelAsync();

        Assert.Equal(0x11111111u, controller.ModelSettings!.TargetUnitId);
        Assert.Equal(0x22222222u, controller.ModelSettings.DonorUnitId);
        Assert.Equal(ModelFeature, controller.DispatchedFeature);
        Assert.Contains("模板模型替换", status);
        Assert.Contains("0x11111111", status);
        Assert.Contains("0x22222222", status);
    }

    [Fact]
    public async Task ReplaceWeaponAsyncWritesWeaponSettingsAndReportsTimeout()
    {
        var controller = new FakeFeatureController { DispatchResult = ActionDispatchResult.TimedOut };
        var status = string.Empty;
        var panel = CreatePanel(controller, message => status = message);
        panel.TargetUnitIdText = "0x33333333";
        panel.DonorUnitIdText = "0x44444444";

        await panel.ReplaceWeaponAsync();

        Assert.Equal(0x33333333u, controller.WeaponSettings!.TargetUnitId);
        Assert.Equal(0x44444444u, controller.WeaponSettings.DonorUnitId);
        Assert.Equal(WeaponFeature, controller.DispatchedFeature);
        Assert.Equal("模板武器替换动作已写入，但尚未被游戏循环消费。", status);
    }

    [Fact]
    public void CommandsReflectPanelAvailability()
    {
        var panel = CreatePanel(new FakeFeatureController(), canExecute: () => false);

        Assert.False(panel.ReplaceModelCommand.CanExecute(null));
        Assert.False(panel.ReplaceWeaponCommand.CanExecute(null));
    }

    private static TemplateReplacementPanelViewModel CreatePanel(
        FakeFeatureController controller,
        Action<string>? setStatus = null,
        Func<bool>? canExecute = null)
    {
        return new TemplateReplacementPanelViewModel(
            ModelFeature,
            WeaponFeature,
            () => controller,
            canExecute ?? (() => true),
            setStatus ?? (_ => { }));
    }

    private sealed class FakeFeatureController : ITrainerFeatureController
    {
        public ActionDispatchResult DispatchResult { get; init; } = ActionDispatchResult.Consumed;

        public TemplateModelReplacementSettings? ModelSettings { get; private set; }

        public TemplateWeaponReplacementSettings? WeaponSettings { get; private set; }

        public TrainerFeature? DispatchedFeature { get; private set; }

        public void SetToggle(TrainerFeature feature, bool enabled) => throw new NotImplementedException();

        public void TriggerAction(TrainerFeature feature) => throw new NotImplementedException();

        public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings) => throw new NotImplementedException();

        public Task<ActionDispatchResult> TriggerActionAndWaitForConsumptionAsync(
            TrainerFeature feature,
            ReinforcementSettings? reinforcementSettings = null,
            TimeSpan? timeout = null,
            TimeSpan? pollInterval = null,
            Action? onDispatched = null,
            CancellationToken cancellationToken = default,
            TimeSpan? pausedGracePeriod = null,
            Action<DispatchWaitStatus>? onWaitStatusChanged = null)
        {
            DispatchedFeature = feature;
            onDispatched?.Invoke();
            return Task.FromResult(DispatchResult);
        }

        public void WriteReinforcementSettings(ReinforcementSettings settings) => throw new NotImplementedException();

        public void WriteResourceValues(ResourceValueSettings settings) => throw new NotImplementedException();

        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings) => throw new NotImplementedException();

        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings)
        {
            ModelSettings = settings;
        }

        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings)
        {
            WeaponSettings = settings;
        }

        public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotImplementedException();

        public void PulseAutoRepair() => throw new NotImplementedException();

        public void ClearAutoRepairPulse() => throw new NotImplementedException();

        public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f) => throw new NotImplementedException();

        public uint ReadSelectedUnitCode() => throw new NotImplementedException();
        public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades() => SelectedUnitUpgradesSnapshot.Empty;
        public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null) => GameApiDispatchStatus.Disabled;

        public byte ReadActionDispatch() => throw new NotImplementedException();
        public uint ReadGameThreadTick() => throw new NotImplementedException();

        public int ReadGameMode() => throw new NotImplementedException();

        public GameApiDispatchStatus TriggerLevelUp(uint count = 1, uint rank = 0, uint flags = 0, TimeSpan? timeout = null) => throw new NotImplementedException();

        public uint CreateUnit(uint thingClassAddress, float posX, float posY, float posZ, TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus KillUnit(TimeSpan? timeout = null) => throw new NotImplementedException();

        public uint CopyForMe(TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus GetMeBase(TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus WeNeedBack(TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus SetUnitState(uint stateFlags, TimeSpan? timeout = null) => throw new NotImplementedException();

        public uint GetCurrentPlayer(TimeSpan? timeout = null) => throw new NotImplementedException();

        public uint LookupScienceByHash(uint hash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus GrantPlayerTech(uint techHash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus GrantUpgradeToPlayer(uint upgradeHash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public bool HasUpgrade(uint upgradeHash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public uint LookupTemplateByHash(uint hash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public uint LookupUpgradeByHash(uint hash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus GrantSecretProtocol(uint techHash, uint upgradeHash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus GrantSelectedUpgrade(uint upgradeHash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus ClearPlayerTechLocks(TimeSpan? timeout = null) => throw new NotImplementedException();

        public uint SecretProtocolBindingProbe(TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus ReplaceTemplateModel(uint targetHash, uint donorHash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public GameApiDispatchStatus ReplaceTemplateWeapon(uint targetHash, uint donorHash, TimeSpan? timeout = null) => throw new NotImplementedException();

        public void Reset(TrainerFeature feature) => throw new NotImplementedException();

        public bool ReadToggleState(TrainerFeature feature) => false;
    }
}
