using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class FeatureItemViewModelHelpTextTests
{
    [Fact]
    public void HelpTextDescribesToggleFeature()
    {
        var feature = LoadFeature("建筑物可随地建造");

        var helpText = feature.HelpText;

        Assert.Contains("建筑物可随地建造", helpText);
        Assert.Contains("快捷键：L", helpText);
        Assert.Contains("类型：开关功能", helpText);
        Assert.Contains("取消建筑放置位置校验，可在通常不可建造的位置落建筑", helpText);
        Assert.DoesNotContain("需要先检测进程并安装 patch", helpText);
    }

    [Fact]
    public void FeatureEnabledStateStartsClosedForToggleAndActionFeatures()
    {
        var toggle = LoadFeature("建筑物可随地建造");
        var action = LoadFeature("选择的单位快速升级");

        Assert.False(toggle.IsFeatureEnabled);
        Assert.False(action.IsFeatureEnabled);
    }

    [Fact]
    public void HelpTextDescribesSelectedUnitActionWithoutObviousPrerequisites()
    {
        var feature = LoadFeature("选择的建筑物/单位设置伪装状态");

        var helpText = feature.HelpText;

        Assert.Contains("选择的建筑物/单位设置伪装状态", helpText);
        Assert.Contains("快捷键：未分配", helpText);
        Assert.Contains("类型：一次性功能", helpText);
        Assert.Contains("把目标状态写成伪装标记，用于触发游戏内伪装/潜伏状态", helpText);
        Assert.DoesNotContain("使用前需在游戏中选中目标单位/建筑", helpText);
    }

    [Fact]
    public void HelpTextDescribesReinforcementFeature()
    {
        var feature = LoadFeature("呼叫战场增援");

        var helpText = feature.HelpText;

        Assert.Contains("呼叫战场增援", helpText);
        Assert.Contains("快捷键：J", helpText);
        Assert.Contains("按支援面板里的单位代码、数量和等级发起一次增援请求", helpText);
    }

    [Fact]
    public void HelpTextExplainsProductionQueueActionsAreFactionScoped()
    {
        var expand = LoadFeature("扩展选中建筑建造队列");
        var restore = LoadFeature("恢复选中建筑建造队列");

        Assert.Contains("当前阵营", expand.HelpText);
        Assert.Contains("999", expand.HelpText);
        Assert.Contains("DLL Agent", expand.HelpText);
        Assert.False(expand.Command.CanExecute(null));
        Assert.Contains("当前阵营", restore.HelpText);
        Assert.Contains("恢复为 1", restore.HelpText);
    }

    [Fact]
    public void HelpTextDescribesSecretProtocolBindingProbeAsFixedAction()
    {
        var feature = CreateFeature(new TrainerFeature(
            "Secret Protocol Binding Probe",
            "秘密协议绑定验证",
            null,
            [],
            "MustCode2+E00",
            "0x0F"));

        var helpText = feature.HelpText;

        Assert.Contains("秘密协议绑定验证", helpText);
        Assert.Contains("快捷键：未分配", helpText);
        Assert.Contains("类型：一次性功能", helpText);
        Assert.Contains("盟军 AirPower", helpText);
        Assert.Contains("日本 EnhancedKamikaze", helpText);
        Assert.Contains("跨阵营", helpText);
        Assert.DoesNotContain("增援单位ID", helpText);
    }

    [Fact]
    public void HelpTextDescribesOrbitalRefuseRankOneProbeAsFixedAction()
    {
        var feature = CreateFeature(new TrainerFeature(
            "Soviet Orbital Refuse Rank 1 Probe",
            "授予苏联轨道垃圾1级协议",
            null,
            [],
            "MustCode+1300",
            "0x10"));

        var helpText = feature.HelpText;

        Assert.Contains("苏联轨道垃圾1级协议", helpText);
        Assert.Contains("PlayerTech_Soviet_OrbitalRefuse_Rank1", helpText);
        Assert.Contains("快捷键：未分配", helpText);
        Assert.Contains("类型：一次性功能", helpText);
    }


    [Fact]
    public void HelpTextInfersDescriptionsFromActionNames()
    {
        Assert.Contains(
            "把目标经验/等级推进到升级状态",
            LoadFeature("选择的单位快速升级").HelpText);
        Assert.Contains(
            "把目标生命值写到最低生存值，方便捕获或快速击毁",
            LoadFeature("选择的建筑物/单位生命值变为1").HelpText);
        Assert.Contains(
            "重置矿点剩余采集量，让矿车继续采集",
            LoadFeature("选择的矿脉恢复采集矿量").HelpText);
    }

    [Fact]
    public void HelpTextOmitsGenericPrerequisitesForEveryFeature()
    {
        var features = LoadFeatures();

        foreach (var feature in features)
        {
            Assert.DoesNotContain("使用前需在游戏中选中目标单位/建筑", feature.HelpText);
            Assert.DoesNotContain("需要先检测进程并安装 patch", feature.HelpText);
            Assert.DoesNotContain("执行时向游戏写入一次动作指令", feature.HelpText);
            Assert.DoesNotContain("开启后持续生效，再次执行关闭", feature.HelpText);
        }
    }

    private static FeatureItemViewModel LoadFeature(string displayName)
    {
        var visibleFeature = LoadFeatures().SingleOrDefault(feature => feature.DisplayName == displayName);
        if (visibleFeature is not null)
        {
            return visibleFeature;
        }

        var viewModel = LoadViewModel();
        var catalogFeature = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features)
            .Single(feature => feature.DisplayName == displayName);
        return new FeatureItemViewModel(catalogFeature, viewModel);
    }

    private static FeatureItemViewModel CreateFeature(TrainerFeature feature)
    {
        return new FeatureItemViewModel(feature, LoadViewModel());
    }

    private static IReadOnlyList<FeatureItemViewModel> LoadFeatures()
    {
        return LoadViewModel().FeatureToggle.Groups
            .SelectMany(group => group.Features)
            .ToArray();
    }

    private static MainViewModel LoadViewModel()
    {
        return SharedTestDoubles.LoadDefaultViewModel();
    }
}
