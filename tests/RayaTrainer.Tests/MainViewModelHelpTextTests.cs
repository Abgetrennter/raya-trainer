using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Hashing;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainViewModelHelpTextTests
{
    [Fact]
    public void GroupsExposeMergedCombatAndSpecialOperationFeatures()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();
        var groupedFeatures = viewModel.FeatureToggle.Groups.SelectMany(group => group.Features).ToArray();

        var damage = Assert.Single(viewModel.FeatureToggle.Groups, group => group.Name == "伤害与无敌");
        Assert.Contains(damage.Features, feature => feature.DisplayName == "玩家全建筑/单位无敌");
        Assert.Contains(damage.Features, feature => feature.DisplayName == "一击必杀敌方建筑物/单位");
        Assert.DoesNotContain(damage.Features, feature => feature.DisplayName == "秘密协议忽略基地需求");
        var build = Assert.Single(viewModel.FeatureToggle.Groups, group => group.Name == "建造与地图");
        Assert.Contains(build.Features, feature => feature.DisplayName == "清除玩家科技锁");
        Assert.Contains(build.Features, feature => feature.DisplayName == "扩展选中建筑建造队列");
        Assert.Contains(build.Features, feature => feature.DisplayName == "恢复选中建筑建造队列");
        var other = Assert.Single(viewModel.FeatureToggle.Groups, group => group.Name == "其他操作");
        Assert.Contains(other.Features, feature => feature.DisplayName == "威胁等级归零");
        Assert.DoesNotContain(other.Features, feature => feature.DisplayName == "威胁等级最高");
        Assert.DoesNotContain(viewModel.FeatureToggle.Groups, group => group.Name == "诊断");
        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "秘密协议技能与超级武器快速冷却");
        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "秘密协议忽略基地需求");
        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "禁止使用技能");
        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "秘密协议绑定验证");
        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "授予苏联轨道垃圾1级协议");
    }

    [Fact]
    public void SelectedUnitOtherGroupContainsSingleAttackSpeedToggle()
    {
        var viewModel = LoadViewModel();
        var group = Assert.Single(viewModel.FeatureToggle.Groups, group => group.Name == "选中单位 · 其他");

        Assert.Contains(group.Features, item => item.Feature.RawName == "Toggle Selected Unit Attack Speed");
        Assert.DoesNotContain(group.Features, item => item.Feature.RawName == "Restore Selected Unit Attack Speed");
    }

    [Fact]
    public void ReadSelectedUnitCodeHelpTextExplainsReinforcementFieldUsage()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        Assert.Contains("当前选中单位的单位代码", viewModel.Reinforcement.ReadSelectedUnitCodeHelpText);
        Assert.Contains("写入增援单位ID文本框", viewModel.Reinforcement.ReadSelectedUnitCodeHelpText);
        Assert.Contains("呼叫战场增援", viewModel.Reinforcement.ReadSelectedUnitCodeHelpText);
        Assert.Contains("当前入队", viewModel.Reinforcement.ReadSelectedUnitCodeHelpText);
    }

    [Fact]
    public void ReinforcementButtonHelpTextsDescribeConcreteActions()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        Assert.Contains("单位代码列表", viewModel.Reinforcement.OpenReinforcementUnitPickerHelpText);
        Assert.Contains("写入增援单位ID文本框", viewModel.Reinforcement.OpenReinforcementUnitPickerHelpText);
        Assert.Contains("玩家基地车", viewModel.Reinforcement.GetMeBaseHelpText);
        Assert.Contains("当前增援单位ID", viewModel.Reinforcement.ExecuteReinforcementHelpText);
        Assert.Contains("当前选中单位", viewModel.Reinforcement.CopySelectedUnitHelpText);
        Assert.Contains("当前增援队列", viewModel.Reinforcement.SaveReinforcementPresetHelpText);
        Assert.Contains("同名预设会被覆盖", viewModel.Reinforcement.SaveReinforcementPresetHelpText);
        Assert.Contains("恢复选中预设的全部条目", viewModel.Reinforcement.ApplyReinforcementPresetHelpText);
        Assert.Contains("全部条目追加到当前增援队列", viewModel.Reinforcement.AppendReinforcementPresetHelpText);
        Assert.Contains("当前输入框", viewModel.Reinforcement.AddCurrentReinforcementToQueueHelpText);
        Assert.Contains("无效项会标记跳过", viewModel.Reinforcement.ExecuteReinforcementQueueHelpText);
        Assert.Contains("不删除已保存预设", viewModel.Reinforcement.ClearReinforcementQueueHelpText);
    }

    [Fact]
    public void SecretProtocolPanelExposesBuiltInProtocolsAndConcreteHelpText()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        Assert.Collection(
            viewModel.SecretProtocol.SecretProtocolToggles,
            feature => Assert.Equal("秘密协议技能与超级武器快速冷却", feature.DisplayName),
            feature => Assert.Equal("秘密协议忽略基地需求", feature.DisplayName),
            feature => Assert.Equal("禁止使用技能", feature.DisplayName));
        Assert.Contains("打开官方和 MOD 秘密协议列表", viewModel.SecretProtocol.OpenSecretProtocolPickerHelpText);
        Assert.Contains("当前秘密协议授予项名称", viewModel.SecretProtocol.SecretProtocolNameHelpText);
        Assert.Contains("PlayerTech 哈希 ID", viewModel.SecretProtocol.SecretProtocolPlayerTechIdHelpText);
        Assert.Contains("Upgrade 哈希 ID", viewModel.SecretProtocol.SecretProtocolUpgradeIdHelpText);
        Assert.Contains("PlayerTech_ 或 Upgrade_", viewModel.SecretProtocol.SecretProtocolHashSourceHelpText);
        Assert.Contains("当前秘密协议授予栏", viewModel.SecretProtocol.AddCurrentSecretProtocolToQueueHelpText);
        Assert.Contains("当前秘密协议授予栏", viewModel.SecretProtocol.GrantCurrentSecretProtocolHelpText);
        Assert.Contains("选中建筑", viewModel.SecretProtocol.GrantSelectedObjectUpgradeHelpText);
        Assert.Contains("Upgrade ID", viewModel.SecretProtocol.GrantSelectedObjectUpgradeHelpText);
        Assert.Contains("写入 PlayerTech", viewModel.SecretProtocol.GrantSecretProtocolQueueHelpText);
    }

    [Fact]
    public void SecretProtocolToggleCommandWritesToggleState()
    {
        var controller = new ResourceWriteFeatureController();
        var sessionManager = ReflectionHelper.ConnectedSessionManager(controller);
        var viewModel = LoadViewModel(sessionManager: sessionManager);
        var dependencyBypass = viewModel.SecretProtocol.SecretProtocolToggles.Single(feature =>
            feature.DisplayName == "秘密协议忽略基地需求");

        dependencyBypass.Command.Execute(null);

        Assert.Equal("Secret Protocol Dependency Bypass", controller.LastToggleFeature?.RawName);
        Assert.True(controller.LastToggleEnabled);
    }

    [Fact]
    public void TemplateModelReplacementFieldsParseTemplateUnitIds()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();
        var panel = viewModel.Reinforcement.TemplateReplacement;

        panel.TargetUnitIdText = "0x11111111";
        panel.DonorUnitIdText = "22222222";

        var settings = panel.GetTemplateModelReplacementSettings();

        Assert.Equal(0x11111111u, settings.TargetUnitId);
        Assert.Equal(0x22222222u, settings.DonorUnitId);
        Assert.Contains("被替换模板", panel.TargetUnitIdHelpText);
        Assert.Contains("来源模板", panel.DonorUnitIdHelpText);
        Assert.Contains("不复制武器", panel.ReplaceTemplateModelHelpText);

        var weaponSettings = panel.GetTemplateWeaponReplacementSettings();

        Assert.Equal(0x11111111u, weaponSettings.TargetUnitId);
        Assert.Equal(0x22222222u, weaponSettings.DonorUnitId);
        Assert.Contains("武器模块", panel.ReplaceTemplateWeaponHelpText);
        Assert.Contains("后续创建", panel.ReplaceTemplateWeaponHelpText);
    }

    [Fact]
    public void SecretProtocolHashSourceFillsMatchingIdField()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        viewModel.SecretProtocol.SecretProtocolHashSourceText = "PlayerTech_Soviet_OrbitalRefuse_Rank1";
        viewModel.SecretProtocol.HashSecretProtocolSourceCommand.Execute(null);

        Assert.Equal(
            $"0x{Ra3InstanceIdHash.Compute("PlayerTech_Soviet_OrbitalRefuse_Rank1"):X8}",
            viewModel.SecretProtocol.SecretProtocolPlayerTechIdText);
        Assert.Equal("0x00000000", viewModel.SecretProtocol.SecretProtocolUpgradeIdText);

        viewModel.SecretProtocol.SecretProtocolHashSourceText = "Upgrade_AlliedAirPower";
        viewModel.SecretProtocol.HashSecretProtocolSourceCommand.Execute(null);

        Assert.Equal(
            $"0x{Ra3InstanceIdHash.Compute("Upgrade_AlliedAirPower"):X8}",
            viewModel.SecretProtocol.SecretProtocolUpgradeIdText);
    }

    [Fact]
    public void SecretProtocolOptionCanFillGrantFieldsAndQueue()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();
        var airPower = SecretProtocolCatalog.LoadBuiltIn()
            .Single(protocol => protocol.PlayerTech == "PlayerTech_Allied_AirPower");

        viewModel.SecretProtocol.ApplySecretProtocolFromPicker(airPower);

        Assert.Equal("先进航空学", viewModel.SecretProtocol.SecretProtocolNameText);
        Assert.Equal("0xDD6C4C5B", viewModel.SecretProtocol.SecretProtocolPlayerTechIdText);
        Assert.Equal("0x33D87C97", viewModel.SecretProtocol.SecretProtocolUpgradeIdText);
    }

    [Fact]
    public void ManualSecretProtocolGrantFieldsCanBeAddedToQueue()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        viewModel.SecretProtocol.SecretProtocolNameText = "手动先进航空学";
        viewModel.SecretProtocol.SecretProtocolPlayerTechIdText = "0xDD6C4C5B";
        viewModel.SecretProtocol.SecretProtocolUpgradeIdText = "0x33D87C97";
        viewModel.SecretProtocol.AddCurrentSecretProtocolToQueueCommand.Execute(null);

        var item = Assert.Single(viewModel.SecretProtocol.SecretProtocolQueue);
        Assert.Equal("自定义", item.Faction);
        Assert.Equal("手动先进航空学", item.Name);
        Assert.Equal("0xDD6C4C5B", item.PlayerTechIdText);
        Assert.Equal("0x33D87C97", item.UpgradeText);
    }

    [Fact]
    public void LauncherAndPatchHelpTextsDescribeConcreteActions()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        Assert.Contains("立刻扫描 RA3 进程", viewModel.RefreshProcessHelpText);
        Assert.Contains("hook 写入当前 RA3 进程", viewModel.InstallPatchesHelpText);
        Assert.Contains("还原已写入的 hook", viewModel.RestorePatchesHelpText);
        Assert.Contains("选择游戏程序路径", viewModel.GameLaunch.BrowseLauncherHelpText);
        Assert.Contains("保存 RA3 路径、启动参数", viewModel.SaveLauncherSettingsHelpText);
        Assert.Contains("最终参数", viewModel.LaunchAndLoadHelpText);
        Assert.Contains("-ui", viewModel.LaunchAndLoadHelpText);
        Assert.Contains("自动安装 patch", viewModel.LaunchAndLoadHelpText);
        Assert.Contains("自定义 Mods 根目录", viewModel.GameLaunch.BrowseModsRootHelpText);
        Assert.Contains("扫描 MOD", viewModel.GameLaunch.RefreshModsHelpText);
        Assert.Contains("窗口模式", viewModel.GameLaunch.LaunchResolutionHelpText);
        Assert.Contains("最终参数", viewModel.GameLaunch.GenerateLauncherArgumentsHelpText);
        Assert.Contains("-modConfig", viewModel.GameLaunch.GenerateLauncherArgumentsHelpText);
        Assert.Contains("无 MOD 不写入", viewModel.GameLaunch.GenerateLauncherArgumentsHelpText);
    }

    [Fact]
    public async Task CheckForUpdatesReportsAvailableStableRelease()
    {
        var viewModel = LoadViewModel(
            new StubUpdateChecker(UpdateCheckResult.Success(
                "v0.1.13",
                "v0.1.14",
                "RA3 Trainer v0.1.14",
                "https://github.com/Abgetrennter/Ra3-trainer-refine/releases/tag/v0.1.14",
                new DateTimeOffset(2026, 6, 10, 1, 59, 54, TimeSpan.Zero),
                Array.Empty<UpdateReleaseAsset>())),
            new StubVersionProvider("v0.1.13"));

        Assert.Equal("当前版本：v0.1.13", viewModel.Tools.CurrentVersionText);
        Assert.Contains("GitHub", viewModel.Tools.CheckForUpdatesHelpText);

        await viewModel.Tools.CheckForUpdatesAsync();

        Assert.Contains("发现新版 v0.1.14", viewModel.StatusMessage);
        Assert.Contains("GitHub Release", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CheckForUpdatesReportsRateLimitFailure()
    {
        var viewModel = LoadViewModel(
            new StubUpdateChecker(UpdateCheckResult.Failure("v0.1.13", "检查更新失败：GitHub 请求受限，请稍后再试。")),
            new StubVersionProvider("v0.1.13"));

        await viewModel.Tools.CheckForUpdatesAsync();

        Assert.Contains("GitHub 请求受限", viewModel.StatusMessage);
    }

    [Fact]
    public void MobileRemoteGeneratesRemoteUrlAndQrCode()
    {
        var viewModel = LoadViewModel(
            mobileRemoteLinkProvider: new StubMobileRemoteLinkProvider("http://192.168.1.10:8787/"),
            qrCodeImageFactory: new StubQrCodeImageFactory());

        Assert.Contains("扫码", viewModel.Tools.MobileRemote.GenerateQrCodeHelpText);

        viewModel.Tools.MobileRemote.GenerateQrCodeCommand.Execute(null);

        Assert.Equal("http://192.168.1.10:8787/", viewModel.Tools.MobileRemote.RemoteUrl);
        Assert.NotNull(viewModel.Tools.MobileRemote.QrCodeImage);
        Assert.Contains("手机遥控二维码已生成", viewModel.StatusMessage);
    }

    [Fact]
    public void MobileRemoteDoesNotGenerateQrCodeWhenWebHostIsUnavailable()
    {
        var viewModel = LoadViewModel(
            mobileRemoteLinkProvider: new StubMobileRemoteLinkProvider("http://192.168.1.10:8787/"),
            qrCodeImageFactory: new StubQrCodeImageFactory(),
            mobileRemoteAvailability: new StubMobileRemoteAvailability(false, "端口 8787 已被占用。"));

        Assert.False(viewModel.Tools.MobileRemote.GenerateQrCodeCommand.CanExecute(null));
        viewModel.Tools.MobileRemote.GenerateQrCodeCommand.Execute(null);

        Assert.Equal("尚未生成", viewModel.Tools.MobileRemote.RemoteUrl);
        Assert.Null(viewModel.Tools.MobileRemote.QrCodeImage);
        Assert.Contains("端口 8787 已被占用", viewModel.StatusMessage);
    }

    [Fact]
    public void GenerateLauncherArgumentsComposesStructuredOptionsAndSelectedMod()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        viewModel.GameLaunch.LaunchUseRa3LauncherUi = false;
        viewModel.GameLaunch.LaunchWindowed = true;
        viewModel.GameLaunch.LaunchResolutionXText = "1280";
        viewModel.GameLaunch.LaunchResolutionYText = "720";
        viewModel.GameLaunch.LaunchWindowPositionXText = "12";
        viewModel.GameLaunch.LaunchWindowPositionYText = "34";
        viewModel.GameLaunch.LaunchNoAudio = true;
        viewModel.GameLaunch.SelectedModLaunchEntry = new Ra3ModEntry(
            "Demo",
            "1.0",
            "C:\\Mods\\Demo Mod\\Demo.skudef",
            "1.12");

        viewModel.GameLaunch.GenerateLauncherArgumentsCommand.Execute(null);

        Assert.Equal("-win -xres 1280 -yres 720 -xpos 12 -ypos 34 -noaudio -modConfig \"C:\\Mods\\Demo Mod\\Demo.skudef\"", viewModel.GameLaunch.LauncherArguments);
    }

    [Fact]
    public void GenerateLauncherArgumentsOmitsSizeAndPositionWhenWindowedIsUnchecked()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        viewModel.GameLaunch.LaunchUseRa3LauncherUi = true;
        viewModel.GameLaunch.LaunchWindowed = false;
        viewModel.GameLaunch.LaunchResolutionXText = "1280";
        viewModel.GameLaunch.LaunchResolutionYText = "720";
        viewModel.GameLaunch.LaunchWindowPositionXText = "12";
        viewModel.GameLaunch.LaunchWindowPositionYText = "34";
        viewModel.GameLaunch.LaunchNoAudio = true;

        viewModel.GameLaunch.GenerateLauncherArgumentsCommand.Execute(null);

        Assert.Equal("-ui -noaudio", viewModel.GameLaunch.LauncherArguments);
    }

    [Fact]
    public void StructuredLauncherControlsDoNotOverwriteManualFinalArgumentsUntilGenerate()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        viewModel.GameLaunch.LauncherArguments = "-custom";
        viewModel.GameLaunch.LaunchWindowed = true;
        viewModel.GameLaunch.LaunchResolutionXText = "1280";

        Assert.Equal("-custom", viewModel.GameLaunch.LauncherArguments);

        viewModel.GameLaunch.GenerateLauncherArgumentsCommand.Execute(null);

        Assert.Equal("-ui -win -xres 1280", viewModel.GameLaunch.LauncherArguments);
    }

    [Fact]
    public void BorderlessFullscreenToggleComposesWindowedFullscreenArgumentsWhenGenerated()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        viewModel.GameLaunch.LaunchUseRa3LauncherUi = false;
        viewModel.GameLaunch.LaunchBorderlessFullscreen = true;
        viewModel.GameLaunch.LaunchResolutionXText = "1920";
        viewModel.GameLaunch.LaunchResolutionYText = "1080";
        viewModel.GameLaunch.GenerateLauncherArgumentsCommand.Execute(null);

        Assert.True(viewModel.GameLaunch.LaunchWindowed);
        Assert.True(viewModel.GameLaunch.LaunchFullscreen);
        Assert.Equal("-win -fullscreen -xres 1920 -yres 1080", viewModel.GameLaunch.LauncherArguments);
    }

    [Fact]
    public void ManualFinalArgumentsCanRouteWithoutMatchingStructuredLauncherUiToggle()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        viewModel.GameLaunch.LauncherArguments = "-win";
        viewModel.GameLaunch.LaunchUseRa3LauncherUi = true;

        Assert.Equal("-win", viewModel.GameLaunch.LauncherArguments);
    }

    [Fact]
    public void LauncherGuideTextExplainsVanillaCoronaAndModArgumentFlows()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        Assert.Contains("原版游戏", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("RA3.exe", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("装载并启动", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("日冕", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("立刻检测进程", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("-ui", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("最终参数", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("无 MOD", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("直接启动原版", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("启动器界面", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("跳过游戏程序", viewModel.GameLaunch.LauncherGuideText);
        Assert.Contains("自定义 Mods 根目录", viewModel.GameLaunch.LauncherGuideText);
    }

    [Fact]
    public void ResourceAndReinforcementInputHelpTextsDescribeHowValuesAreUsed()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        Assert.Contains("每次执行时增加的金额", viewModel.FeatureToggle.MoneyAmountHelpText);
        Assert.Contains("无限电力开启后写入", viewModel.FeatureToggle.PowerValueHelpText);
        Assert.Contains("范围 0-15", viewModel.FeatureToggle.ScPointValueHelpText);
        Assert.Contains("增援要生成的单位代码", viewModel.Reinforcement.ReinforcementUnitIdHelpText);
        Assert.Contains("每次呼叫增援生成的单位数量", viewModel.Reinforcement.ReinforcementCountHelpText);
        Assert.Contains("增援生成后的经验等级", viewModel.Reinforcement.ReinforcementRankHelpText);
        Assert.Contains("当前入队项名称由单位ID对应的单位名称决定", viewModel.Reinforcement.PresetNameHelpText);
        Assert.Contains("清空并恢复", viewModel.Reinforcement.ReinforcementPresetHelpText);
        Assert.Contains("追加到当前队列", viewModel.Reinforcement.ReinforcementPresetHelpText);
    }

    [Fact]
    public void SelectedUnitAndSecretProtocolPresetHelpTextsAreNonEmpty()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();

        Assert.NotEmpty(viewModel.SecretProtocol.SecretProtocolPresetNameHelpText);
        Assert.NotEmpty(viewModel.SecretProtocol.SecretProtocolPresetHelpText);
        Assert.NotEmpty(viewModel.SecretProtocol.SaveSecretProtocolPresetHelpText);
        Assert.NotEmpty(viewModel.SecretProtocol.ApplySecretProtocolPresetHelpText);
        Assert.NotEmpty(viewModel.SecretProtocol.AppendSecretProtocolPresetHelpText);
    }

    [Fact]
    public void QueueItemRemoveHelpTextExplainsItDoesNotDeletePreset()
    {
        var item = new ReinforcementQueueItemViewModel(
            "MCV",
            "0xAF4C0DA5",
            "1",
            "0",
            _ => { },
            () => true);

        Assert.Contains("从增援队列移除此项", item.RemoveQueueItemHelpText);
        Assert.Contains("不删除已保存预设", item.RemoveQueueItemHelpText);
    }

    [Fact]
    public void MigratedReinforcementActionsStayOutOfFeatureGroups()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();
        var groupedFeatures = viewModel.FeatureToggle.Groups.SelectMany(group => group.Features).ToArray();

        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "给玩家基地车");
        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "呼叫战场增援");
        Assert.DoesNotContain(groupedFeatures, feature => feature.DisplayName == "复制选择的建筑物/单位给玩家");
        Assert.Equal("给玩家基地车 (K)", viewModel.Reinforcement.GetMeBaseButtonText);
        Assert.Equal("呼叫战场增援 (J)", viewModel.Reinforcement.ExecuteReinforcementButtonText);
        Assert.Equal("复制选中单位 (I)", viewModel.Reinforcement.CopySelectedUnitButtonText);
    }

    [Fact]
    public void ExecuteReinforcementCommandDispatchesMigratedActionWithCurrentSettings()
    {
        var controller = new ResourceWriteFeatureController { DispatchResult = ActionDispatchResult.Consumed };
        var sessionManager = ReflectionHelper.ConnectedSessionManager(controller);
        var viewModel = LoadViewModel(sessionManager: sessionManager);

        viewModel.Reinforcement.ReinforcementUnitIdText = "0x6586A5A0";
        viewModel.Reinforcement.ReinforcementCountText = "3";
        viewModel.Reinforcement.ReinforcementRankText = "2";
        viewModel.Reinforcement.ExecuteReinforcementCommand.Execute(null);

        Assert.Equal("We Need Back", controller.LastActionFeature?.RawName);
        Assert.Equal(new ReinforcementSettings(0x6586A5A0, 3, 2), controller.LastReinforcementSettings);
        Assert.Contains("呼叫战场增援", viewModel.StatusMessage);
    }

    [Fact]
    public void ExecuteReinforcementQueueUsesMigratedActionEvenWhenHiddenFromGroups()
    {
        var controller = new ResourceWriteFeatureController { DispatchResult = ActionDispatchResult.Consumed };
        var sessionManager = ReflectionHelper.ConnectedSessionManager(controller);
        var viewModel = LoadViewModel(sessionManager: sessionManager);

        viewModel.Reinforcement.ReinforcementUnitIdText = "0x6586A5A0";
        viewModel.Reinforcement.ReinforcementCountText = "1";
        viewModel.Reinforcement.ReinforcementRankText = "0";
        viewModel.Reinforcement.AddCurrentReinforcementToQueueCommand.Execute(null);
        viewModel.Reinforcement.ExecuteReinforcementQueueCommand.Execute(null);

        Assert.Equal("We Need Back", controller.LastActionFeature?.RawName);
        Assert.Equal(new ReinforcementSettings(0x6586A5A0, 1, 0), controller.LastReinforcementSettings);
        Assert.Contains("成功 1", viewModel.StatusMessage);
    }

    [Fact]
    public void ReinforcementPresetSavesAppliesAndAppendsEntireQueue()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();
        var reinforcement = viewModel.Reinforcement;

        reinforcement.PresetNameText = "不应作为单位名称";
        reinforcement.ReinforcementUnitIdText = "0x6586A5A0";
        reinforcement.ReinforcementCountText = "2";
        reinforcement.ReinforcementRankText = "1";
        reinforcement.AddCurrentReinforcementToQueueCommand.Execute(null);
        reinforcement.PresetNameText = "仍不应作为单位名称";
        reinforcement.ReinforcementUnitIdText = "0xAF4C0DA5";
        reinforcement.ReinforcementCountText = "4";
        reinforcement.ReinforcementRankText = "3";
        reinforcement.AddCurrentReinforcementToQueueCommand.Execute(null);

        reinforcement.PresetNameText = "两波增援";
        reinforcement.SaveReinforcementPresetCommand.Execute(null);

        var preset = Assert.Single(reinforcement.ReinforcementPresets);
        Assert.Equal("两波增援", preset.Name);
        Assert.Collection(
            preset.Entries,
            entry =>
            {
                Assert.Equal("奥米茄百合子", entry.Name);
                Assert.Equal(0x6586A5A0u, entry.UnitId);
                Assert.Equal(2, entry.Count);
                Assert.Equal(1, entry.Rank);
            },
            entry =>
            {
                Assert.Equal("MCV", entry.Name);
                Assert.Equal(0xAF4C0DA5u, entry.UnitId);
                Assert.Equal(4, entry.Count);
                Assert.Equal(3, entry.Rank);
            });

        reinforcement.ClearReinforcementQueueCommand.Execute(null);
        reinforcement.ApplyReinforcementPresetCommand.Execute(null);
        Assert.Equal(2, reinforcement.ReinforcementQueue.Count);
        Assert.Equal("0x6586A5A0", reinforcement.ReinforcementQueue[0].UnitIdText);
        Assert.Equal("0xAF4C0DA5", reinforcement.ReinforcementQueue[1].UnitIdText);

        reinforcement.AppendReinforcementPresetCommand.Execute(null);
        Assert.Equal(4, reinforcement.ReinforcementQueue.Count);
        Assert.Equal("奥米茄百合子", reinforcement.ReinforcementQueue[2].Name);
        Assert.Equal("MCV", reinforcement.ReinforcementQueue[3].Name);
    }

    [Fact]
    public void SecretProtocolPresetSavesAppliesAndAppendsEntireQueue()
    {
        var viewModel = SharedTestDoubles.LoadDefaultViewModel();
        var secretProtocol = viewModel.SecretProtocol;

        secretProtocol.SecretProtocolNameText = "先进航空学";
        secretProtocol.SecretProtocolPlayerTechIdText = "0xDD6C4C5B";
        secretProtocol.SecretProtocolUpgradeIdText = "0x33D87C97";
        secretProtocol.AddCurrentSecretProtocolToQueueCommand.Execute(null);
        secretProtocol.SecretProtocolNameText = "高科技";
        secretProtocol.SecretProtocolPlayerTechIdText = "0x7A9E4201";
        secretProtocol.SecretProtocolUpgradeIdText = "0x3AC47A99";
        secretProtocol.AddCurrentSecretProtocolToQueueCommand.Execute(null);

        secretProtocol.SecretProtocolPresetNameText = "盟军组合";
        secretProtocol.SaveSecretProtocolPresetCommand.Execute(null);

        var preset = Assert.Single(secretProtocol.SecretProtocolPresets);
        Assert.Equal("盟军组合", preset.Name);
        Assert.Collection(
            preset.Entries,
            entry =>
            {
                Assert.Equal("先进航空学", entry.Name);
                Assert.Equal(0xDD6C4C5Bu, entry.PlayerTechId);
                Assert.Equal(0x33D87C97u, entry.UpgradeId);
            },
            entry =>
            {
                Assert.Equal("高科技", entry.Name);
                Assert.Equal(0x7A9E4201u, entry.PlayerTechId);
                Assert.Equal(0x3AC47A99u, entry.UpgradeId);
            });

        secretProtocol.ClearSecretProtocolQueueCommand.Execute(null);
        secretProtocol.ApplySecretProtocolPresetCommand.Execute(null);
        Assert.Equal(2, secretProtocol.SecretProtocolQueue.Count);
        Assert.Equal("先进航空学", secretProtocol.SecretProtocolQueue[0].Name);
        Assert.Equal("高科技", secretProtocol.SecretProtocolQueue[1].Name);

        secretProtocol.AppendSecretProtocolPresetCommand.Execute(null);
        Assert.Equal(4, secretProtocol.SecretProtocolQueue.Count);
        Assert.Equal("先进航空学", secretProtocol.SecretProtocolQueue[2].Name);
        Assert.Equal("高科技", secretProtocol.SecretProtocolQueue[3].Name);
    }

    [Fact]
    public void SecretProtocolQueueStopsAfterPauseAbort()
    {
        var controller = new ResourceWriteFeatureController { DispatchResult = ActionDispatchResult.AbortedDueToPause };
        var viewModel = LoadViewModel(sessionManager: ReflectionHelper.ConnectedSessionManager(controller));
        var secretProtocol = viewModel.SecretProtocol;

        foreach (var (name, playerTechId) in new[]
        {
            ("先进航空学", "0xDD6C4C5B"),
            ("高科技", "0x7A9E4201"),
            ("精确空袭", "0xAABBCCDD")
        })
        {
            secretProtocol.SecretProtocolNameText = name;
            secretProtocol.SecretProtocolPlayerTechIdText = playerTechId;
            secretProtocol.SecretProtocolUpgradeIdText = "0x00000000";
            secretProtocol.AddCurrentSecretProtocolToQueueCommand.Execute(null);
        }

        secretProtocol.GrantSecretProtocolQueueCommand.Execute(null);

        Assert.Equal(1, controller.ActionDispatchCount);
        Assert.All(secretProtocol.SecretProtocolQueue, item => Assert.Equal("已放弃（游戏暂停）", item.Status));
    }

    private static MainViewModel LoadViewModel(
        IUpdateChecker? updateChecker = null,
        IApplicationVersionProvider? versionProvider = null,
        ITrainerSessionService? sessionManager = null,
        IMobileRemoteLinkProvider? mobileRemoteLinkProvider = null,
        IQrCodeImageFactory? qrCodeImageFactory = null,
        IMobileRemoteAvailability? mobileRemoteAvailability = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "settings.json");
        return MainViewModel.Load(
            TestAssets.LoadManifest(),
            new TrainerAppSettingsStore(settingsPath),
            updateChecker,
            versionProvider,
            sessionManager,
            mobileRemoteLinkProvider,
            qrCodeImageFactory,
            mobileRemoteAvailability);
    }

    private sealed class StubUpdateChecker : IUpdateChecker
    {
        private readonly UpdateCheckResult _result;

        public StubUpdateChecker(UpdateCheckResult result)
        {
            _result = result;
        }

        public Task<UpdateCheckResult> CheckLatestStableReleaseAsync(string currentVersion, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class StubVersionProvider : IApplicationVersionProvider
    {
        public StubVersionProvider(string currentVersion)
        {
            CurrentVersion = currentVersion;
        }

        public string CurrentVersion { get; }
    }

    private sealed class StubMobileRemoteLinkProvider(string remoteUrl) : IMobileRemoteLinkProvider
    {
        public string CreateRemoteUrl() => remoteUrl;
        public IReadOnlyList<LanAddressEntry> GetAvailableAddresses() =>
            [new LanAddressEntry("192.168.1.10（测试）", "192.168.1.10")];
        public string CreateRemoteUrl(string ip) => $"http://{ip}:8787/";
    }

    private sealed class StubQrCodeImageFactory : IQrCodeImageFactory
    {
        public ImageSource Create(string content)
        {
            var image = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 255, 255, 255, 255 },
                4);
            image.Freeze();
            return image;
        }
    }

    private sealed class StubMobileRemoteAvailability(bool isAvailable, string unavailableReason) : IMobileRemoteAvailability
    {
        public bool IsAvailable => isAvailable;

        public string UnavailableReason => unavailableReason;
    }
}
