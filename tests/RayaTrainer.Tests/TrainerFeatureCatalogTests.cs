using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerFeatureCatalogTests
{
    [Fact]
    public void CreateUiFeaturesUsesSourceTrainerNamesAndHotkeys()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        Assert.Contains(features, feature =>
            feature.DisplayName == "增加玩家战场资金" &&
            feature.Hotkey == "Ctrl+F1");
        var power = Assert.Single(features, feature =>
            feature.DisplayName == "无限电力" &&
            feature.Hotkey == "Ctrl+F2");
        Assert.Null(power.ValueHint);
        Assert.Contains(features, feature =>
            feature.DisplayName == "选择的单位快速升级" &&
            feature.Hotkey == "P");
        var playerGodMode = Assert.Single(features, feature => feature.RawName == "Player God Mode");
        Assert.Equal("玩家全建筑/单位无敌", playerGodMode.DisplayName);
        Assert.Equal("Ctrl+F11", playerGodMode.Hotkey);
        Assert.Equal(new[] { "Player God Mode" }, playerGodMode.EnableFlags);
        Assert.Null(playerGodMode.ValueHint);
        var playerOneKillMode = Assert.Single(features, feature => feature.RawName == "Player One Kill Mode");
        Assert.Equal("一击必杀敌方建筑物/单位", playerOneKillMode.DisplayName);
        Assert.Equal("Ctrl+F12", playerOneKillMode.Hotkey);
        Assert.Equal(new[] { "Player One Kill Mode" }, playerOneKillMode.EnableFlags);
        Assert.Null(playerOneKillMode.ValueHint);
        Assert.Contains(features, feature =>
            feature.DisplayName == "选择的单位高速移动" &&
            feature.Hotkey == "-");
        Assert.Contains(features, feature =>
            feature.DisplayName == "选择的单位缓慢移动" &&
            feature.Hotkey == "=");
        var captureUnit = Assert.Single(features, feature => feature.RawName == "Select Unit Change ID");
        Assert.Equal("俘虏选择的建筑物/单位", captureUnit.DisplayName);
        Assert.Equal("O", captureUnit.Hotkey);
        Assert.Null(captureUnit.DispatchTarget);
        Assert.Equal("0x09", captureUnit.ValueHint);
        Assert.Contains(features, feature =>
            feature.DisplayName == "摧毁选择的建筑物/单位" &&
            feature.Hotkey == "Delete");
        // 矿脉储量重置的默认快捷键已主动清除（用户要求），UI feature 列表里 Hotkey 为 null。
        Assert.Contains(features, feature =>
            feature.DisplayName == "选择的矿脉恢复采集矿量" &&
            feature.Hotkey is null);
        Assert.Contains(features, feature =>
            feature.DisplayName == "威胁等级归零" &&
            feature.Hotkey == ".");
        Assert.DoesNotContain(features, feature => feature.DisplayName == "威胁等级最高");
        Assert.Contains(features, feature =>
            feature.DisplayName == "建筑物可随地建造" &&
            feature.Hotkey == "L");
        Assert.Contains(features, feature =>
            feature.DisplayName == "给玩家基地车" &&
            feature.Hotkey == "K");
        Assert.Contains(features, feature =>
            feature.DisplayName == "呼叫战场增援" &&
            feature.Hotkey == "J");
        Assert.Contains(features, feature =>
            feature.DisplayName == "选择的建筑物/单位设置伪装状态" &&
            feature.Hotkey is null);
        Assert.Contains(features, feature =>
            feature.DisplayName == "复制选择的建筑物/单位给玩家" &&
            feature.Hotkey == "I");
        var dependencyBypass = Assert.Single(features, feature => feature.RawName == "Secret Protocol Dependency Bypass");
        Assert.Equal("秘密协议忽略基地需求", dependencyBypass.DisplayName);
        Assert.Null(dependencyBypass.Hotkey);
        Assert.Equal(new[] { "Secret Protocol Dependency Bypass" }, dependencyBypass.EnableFlags);
        Assert.Null(dependencyBypass.DispatchTarget);
        Assert.Null(dependencyBypass.ValueHint);
    }

    [Fact]
    public void UiFeaturesExcludeHiddenAmmoToggleAndIncludeFillResetActions()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        // Old ammo toggle pair is hidden (Hide=true) — neither should appear.
        Assert.DoesNotContain(features, f => f.RawName == "Select Unit Ammo MAX");
        Assert.DoesNotContain(features, f => f.RawName == "Select Unit Ammo MAX 2");

        // New fill and reset actions should be present.
        var fill = Assert.Single(features, f => f.RawName == "Fill Selected Unit Ammo");
        Assert.Equal("选择的单位弹药填满", fill.DisplayName);
        Assert.Null(fill.Hotkey);
        Assert.Equal("0x17", fill.ValueHint);
        Assert.Null(fill.DispatchTarget);

        var reset = Assert.Single(features, f => f.RawName == "Reset Selected Unit Ammo");
        Assert.Equal("选择的单位弹药归1", reset.DisplayName);
        Assert.Null(reset.Hotkey);
        Assert.Equal("0x18", reset.ValueHint);
        Assert.Null(reset.DispatchTarget);
    }

    [Fact]
    public void CreateUiFeaturesMapsFreeBuildToContextHookToggle()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        var freeBuild = Assert.Single(features, feature => feature.RawName == "Free Build");
        Assert.Equal(new[] { "Free Build" }, freeBuild.EnableFlags);
        Assert.Null(freeBuild.ValueHint);
    }

    [Fact]
    public void CreateUiFeaturesDoesNotUseUnfilteredBytePatchForIgnorePrerequisites()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        var ignorePrerequisites = Assert.Single(features, feature => feature.RawName == "Ignore Prerequisites");
        Assert.Equal("忽略建造前置条件", ignorePrerequisites.DisplayName);
        Assert.Equal(new[] { "Ignore Prerequisites" }, ignorePrerequisites.EnableFlags);
    }

    [Fact]
    public void CreateUiFeaturesKeepsExpectedCountAfterInstanceEffectsAdded()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        // Lock the merged manifest and panel-action projection against accidental UI drift.
        Assert.Equal(TestAssets.CurrentUiFeatureCount, features.Count);
    }

    [Fact]
    public void CreateUiFeaturesAddsSetUnitStateAction()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        var setUnitState = Assert.Single(features, feature => feature.RawName == "Set Unit Support State");
        Assert.Equal("选择的建筑物/单位设置伪装状态", setUnitState.DisplayName);
        Assert.Null(setUnitState.Hotkey);
        Assert.Null(setUnitState.DispatchTarget);
        Assert.Equal("0x0E", setUnitState.ValueHint);
    }

    [Fact]
    public void CreateGridFeaturesExcludesHiddenDiagnosticActions()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features);

        Assert.DoesNotContain(features, feature => feature.RawName == "Secret Protocol Binding Probe");
        Assert.DoesNotContain(features, feature => feature.RawName == "Soviet Orbital Refuse Rank 1 Probe");
    }

    [Fact]
    public void CreateGridFeaturesExposesRunInBackgroundAsEnableFlagToggle()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features);

        var runInBackground = Assert.Single(features, feature => feature.RawName == "Run In Background");
        Assert.Equal("允许后台响应（失焦时修改器仍可响应，仅限单机/遭遇战）", runInBackground.DisplayName);
        Assert.Null(runInBackground.Hotkey);
        Assert.Equal(new[] { "Run In Background" }, runInBackground.EnableFlags);
        Assert.Null(runInBackground.DispatchTarget);
        Assert.Null(runInBackground.ValueHint);
    }

    [Fact]
    public void CreateGridFeaturesExposesRa3112FrameRateUnlockAsPatchSetOnly()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);

        var frameRateUnlock = Assert.Single(features, feature => feature.RawName == "Frame Rate Unlock 60fps");
        Assert.Equal("60fps 帧率解锁", frameRateUnlock.DisplayName);
        Assert.Equal(new[] { "Frame Rate Unlock 60fps" }, frameRateUnlock.EnableFlags);
        Assert.Equal(new[] { "ra3_1.12" }, frameRateUnlock.SupportedProfileIds);
        Assert.Equal("渲染与性能", TrainerFeatureGroupCatalog.GetGroupName(frameRateUnlock));
    }

    [Fact]
    public void CreateGridFeaturesExposesProductionQueueAgentActions()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);

        var expand = Assert.Single(features, feature => feature.RawName == "Expand Production Queue");
        var restore = Assert.Single(features, feature => feature.RawName == "Restore Production Queue");
        Assert.Equal("扩展选中建筑建造队列", expand.DisplayName);
        Assert.Equal("恢复选中建筑建造队列", restore.DisplayName);
        Assert.True(expand.RequiresDirectGameApi);
        Assert.True(restore.RequiresDirectGameApi);
        Assert.Equal("0x19", expand.ValueHint);
        Assert.Equal("0x19", restore.ValueHint);
    }

    [Fact]
    public void CreateGridFeaturesExposesSelectedUnitTeleportAgentAction()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);

        var teleport = Assert.Single(
            features,
            feature => feature.RawName == "Teleport Selected Units To Mouse");
        Assert.Equal("移动选中单位到鼠标位置", teleport.DisplayName);
        Assert.True(teleport.RequiresDirectGameApi);
        Assert.Equal("0x1A", teleport.ValueHint);
        // 选中单位分组已移至独立选项卡，GetGroupName fallback 到"秘密协议与扩展操作"
        Assert.Equal("秘密协议与扩展操作", TrainerFeatureGroupCatalog.GetGroupName(teleport));
    }

    [Fact]
    public void CreateGridFeaturesExcludesDedicatedPanelActions()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features);
        var uiFeatures = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        Assert.DoesNotContain(features, feature => feature.RawName == "Grant Secret Protocol");
        Assert.DoesNotContain(features, feature => feature.RawName == "Grant Selected Object Upgrade");
        Assert.DoesNotContain(features, feature => feature.RawName == "Replace Template Model");
        Assert.DoesNotContain(features, feature => feature.RawName == "Replace Template Weapon");
        Assert.Equal(features.Select(feature => feature.RawName), uiFeatures.Select(feature => feature.RawName));
    }

    [Fact]
    public void FeatureToggleGroupsExcludeSelectedUnitGroups()
    {
        var groups = TrainerFeatureGroupCatalog.Groups;

        Assert.Equal(4, groups.Count);
        Assert.DoesNotContain(groups, g => g.Name.Contains("选中单位"));
        Assert.DoesNotContain(groups, g => g.Name == "伤害与无敌");
    }

    [Fact]
    public void SelectedUnitGroupingNamesContainsExpectedDisplayNames()
    {
        var names = TrainerFeatureGroupCatalog.SelectedUnitGroupingNames;

        Assert.Contains("选择的建筑物/单位无限生命值", names);
        Assert.Contains("摧毁选择的建筑物/单位", names);
        Assert.Contains("玩家全建筑/单位无敌", names);
        Assert.Contains("清空满攻速单位", names);
        Assert.Contains("清空无限射程单位", names);
        Assert.Equal(21, names.Count);
    }

    [Fact]
    public void CreateUiFeaturesHidesPlayerAutoRepairToggle()
    {
        var manifest = TestAssets.LoadManifest();

        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        Assert.DoesNotContain(features, feature => feature.RawName == "Player Auto Repair");
        Assert.DoesNotContain(features, feature => feature.DisplayName == "己方单位/建筑自动修复");
    }

    [Fact]
    public void CreateDefaultHotkeysUsesRawNames()
    {
        var manifest = TestAssets.LoadManifest();
        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);

        var hotkeys = TrainerFeatureCatalog.CreateDefaultHotkeys(features);

        // 配置字典的 key 必须是稳定的 RawName，而非可变的中文 DisplayName。
        Assert.Equal("Ctrl+F1", hotkeys["Money"]);
        Assert.Equal("O", hotkeys["Select Unit Change ID"]);
        // 矿脉储量重置与起义时刻两个功能的默认快捷键已主动清除（用户要求），不再生成默认值。
        Assert.DoesNotContain("Restore Select Ore Mine", hotkeys.Keys);
        Assert.DoesNotContain("Challenge Money", hotkeys.Keys);
        Assert.DoesNotContain("Challenge Time", hotkeys.Keys);
        Assert.DoesNotContain("Set Unit Support State", hotkeys.Keys);
        Assert.DoesNotContain("Secret Protocol Binding Probe", hotkeys.Keys);
        Assert.DoesNotContain("Soviet Orbital Refuse Rank 1 Probe", hotkeys.Keys);
        Assert.DoesNotContain("Secret Protocol Dependency Bypass", hotkeys.Keys);
        Assert.Equal("L", hotkeys["Free Build"]);
        Assert.Equal("K", hotkeys["Get Me Base"]);
        Assert.Equal("J", hotkeys["We Need Back"]);
        Assert.Equal("I", hotkeys["Select Unit Copy For Me"]);
        Assert.Equal(";", hotkeys["Toggle Selected Unit Attack Speed"]);
        Assert.DoesNotContain("Toggle Selected Unit Attack Range", hotkeys.Keys);
        Assert.DoesNotContain("Fill Selected Unit Ammo", hotkeys.Keys);
        // 旧版以 DisplayName 作 key 的契约已废弃，确保不再混入。
        Assert.DoesNotContain("增加玩家战场资金", hotkeys.Keys);
    }

    [Fact]
    public void ApplyHotkeyOverridesUsesRawNamesAndDisablesInvalidHotkeys()
    {
        var manifest = TestAssets.LoadManifest();
        var features = TrainerFeatureCatalog.CreateUiFeatures(manifest.Features);
        var hotkeys = new Dictionary<string, string>
        {
            ["Money"] = "Alt+F1",
            ["Power"] = "不是快捷键"
        };

        var configured = TrainerFeatureCatalog.ApplyHotkeyOverrides(features, hotkeys);

        var money = Assert.Single(configured, feature => feature.DisplayName == "增加玩家战场资金");
        var power = Assert.Single(configured, feature => feature.DisplayName == "无限电力");
        var scPoint = Assert.Single(configured, feature => feature.DisplayName == "无限秘密协议点数");
        Assert.Equal("Alt+F1", money.Hotkey);
        Assert.Null(power.Hotkey);
        Assert.Equal("Ctrl+F3", scPoint.Hotkey);
    }

    [Fact]
    public void SecretProtocolGrantFeatureUsesActionDispatchSlot()
    {
        var feature = TrainerFeatureCatalog.SecretProtocolGrantFeature;

        Assert.Equal("Grant Secret Protocol", feature.RawName);
        Assert.Equal("授予秘密协议", feature.DisplayName);
        Assert.Null(feature.DispatchTarget);
        Assert.Equal("0x11", feature.ValueHint);
    }

    [Fact]
    public void CreatePanelActionsReturnsDedicatedPanelActions()
    {
        var actions = TrainerFeatureCatalog.CreatePanelActions();

        Assert.Contains(actions, feature => feature.RawName == "Grant Secret Protocol");
        Assert.Contains(actions, feature => feature.RawName == "Grant Selected Object Upgrade");
        Assert.Contains(actions, feature => feature.RawName == "Replace Template Model");
        Assert.Contains(actions, feature => feature.RawName == "Replace Template Weapon");
        Assert.DoesNotContain(actions, feature => feature.RawName == "Clear Player Tech Locks");
    }

    [Fact]
    public void SelectedObjectUpgradeGrantFeatureUsesActionDispatchSlot()
    {
        var feature = TrainerFeatureCatalog.SelectedObjectUpgradeGrantFeature;

        Assert.Equal("Grant Selected Object Upgrade", feature.RawName);
        Assert.Equal("授予选中建筑 Upgrade", feature.DisplayName);
        Assert.Null(feature.DispatchTarget);
        Assert.Equal("0x12", feature.ValueHint);
    }

    [Fact]
    public void ClearPlayerTechLocksFeatureUsesActionDispatchSlot()
    {
        var features = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);

        var feature = Assert.Single(features, feature => feature.RawName == "Clear Player Tech Locks");

        Assert.Equal("清除玩家科技锁", feature.DisplayName);
        Assert.Null(feature.Hotkey);
        Assert.Null(feature.DispatchTarget);
        Assert.Equal("0x13", feature.ValueHint);
    }

    [Fact]
    public void TemplateModelReplacementFeatureUsesActionDispatchSlot()
    {
        var feature = TrainerFeatureCatalog.TemplateModelReplacementFeature;

        Assert.Equal("Replace Template Model", feature.RawName);
        Assert.Equal("替换单位模板模型", feature.DisplayName);
        Assert.Null(feature.DispatchTarget);
        Assert.Equal("0x14", feature.ValueHint);
    }

    [Fact]
    public void TemplateWeaponReplacementFeatureUsesActionDispatchSlot()
    {
        var feature = TrainerFeatureCatalog.TemplateWeaponReplacementFeature;

        Assert.Equal("Replace Template Weapon", feature.RawName);
        Assert.Equal("替换单位模板武器", feature.DisplayName);
        Assert.Null(feature.DispatchTarget);
        Assert.Equal("0x15", feature.ValueHint);
    }

    [Fact]
    public void SelectedUnitAttackSpeedToggleIsInstanceScopedAndVersionGated()
    {
        var features = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);
        var toggle = Assert.Single(features, feature => feature.RawName == "Toggle Selected Unit Attack Speed");

        Assert.Contains("切换", toggle.DisplayName, StringComparison.Ordinal);
        Assert.Equal(";", toggle.Hotkey);
        Assert.Equal("0x19", toggle.ValueHint);
        Assert.DoesNotContain(features, feature => feature.RawName == "Restore Selected Unit Attack Speed");
        Assert.True(toggle.SupportsProfile("ra3_1.12"));
        Assert.True(toggle.SupportsProfile("ra3_1.13"));
        Assert.True(toggle.SupportsProfile("ra3_uprising_1.0"));
        Assert.True(toggle.SupportsProfile("ra3_uprising_1.1"));
    }

    [Fact]
    public void ToggleSelectedUnitAttackRangeHasNoDefaultHotkey()
    {
        var features = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);
        var toggle = Assert.Single(features, feature => feature.RawName == "Toggle Selected Unit Attack Range");

        Assert.Contains("无限射程", toggle.DisplayName, StringComparison.Ordinal);
        Assert.Contains("索敌", toggle.DisplayName, StringComparison.Ordinal);
        Assert.Null(toggle.Hotkey);
        Assert.Equal("0x1B", toggle.ValueHint);
        Assert.True(toggle.SupportsProfile("ra3_1.12"));
        Assert.True(toggle.SupportsProfile("ra3_1.13"));
        Assert.True(toggle.SupportsProfile("ra3_uprising_1.0"));
        Assert.True(toggle.SupportsProfile("ra3_uprising_1.1"));
        Assert.Equal(SelectionExecutionMode.Apply, toggle.SelectionMode);
    }

    [Fact]
    public void SecretProtocolCatalogContainsAllVanillaPurchasableTechsAndUpgradeBindings()
    {
        var protocols = SecretProtocolCatalog.LoadBuiltIn();

        Assert.True(protocols.Count >= 45);
        Assert.Contains(protocols, protocol =>
            protocol.PlayerTech == "PlayerTech_Allied_AirPower" &&
            protocol.PlayerTechId == 0xDD6C4C5B &&
            protocol.Upgrade == "Upgrade_AlliedAirPower" &&
            protocol.UpgradeId == 0x33D87C97);
        Assert.Contains(protocols, protocol =>
            protocol.PlayerTech == "PlayerTech_Japan_EnhancedKamikaze" &&
            protocol.PlayerTechId == 0xFBE46678 &&
            protocol.Upgrade == "Upgrade_JapanEnhancedKamikaze" &&
            protocol.UpgradeId == 0x5F7C162F);
        Assert.Contains(protocols, protocol =>
            protocol.PlayerTech == "PlayerTech_Soviet_OrbitalRefuse_Rank1" &&
            protocol.PlayerTechId == 0x3A7E2F69 &&
            protocol.Upgrade is null);
        Assert.Contains(protocols, protocol =>
            protocol.Mod == "日冕" &&
            protocol.Faction == "神州" &&
            protocol.Name == "超导电枢" &&
            protocol.PlayerTech is null &&
            protocol.Upgrade == "Upgrade_CelestialSupplyElectricitySystem" &&
            protocol.CanGrant);
        Assert.Contains(protocols, protocol =>
            protocol.Mod == "日冕" &&
            protocol.Faction == "神州" &&
            protocol.Name == "彻甲惊雷（电磁炮）" &&
            protocol.PlayerTech == "PlayerTech_Celestial_ElectromagneticGun" &&
            protocol.PlayerTechId == 0x1A858C6C &&
            protocol.Upgrade == "Upgrade_CelestialElectromagneticGun" &&
            protocol.UpgradeId == 0xE11E7985 &&
            protocol.SpecialPower == "SpecialPower_Celestial_ElectromagneticGun" &&
            protocol.CanGrant);
    }

    [Fact]
    public void SelectedUnitWeaponEffectsAreExposedInUi()
    {
        var features = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);
        Assert.Contains(features, feature => feature.RawName == "Toggle Selected Unit Attack Speed");
        Assert.Contains(features, feature => feature.RawName == "Toggle Selected Unit Attack Range");
        Assert.Contains(features, feature => feature.RawName == "Clear Selected Attack Speed Effects");
        Assert.Contains(features, feature => feature.RawName == "Clear Selected Attack Range Effects");

        Assert.Contains("选择的单位满攻速（切换，仅当前实例）", TrainerFeatureGroupCatalog.SelectedUnitGroupingNames);
        Assert.Contains("选择的单位无限射程与索敌（切换，仅当前实例）", TrainerFeatureGroupCatalog.SelectedUnitGroupingNames);
        Assert.Contains("选择的单位弹药填满", TrainerFeatureGroupCatalog.SelectedUnitGroupingNames);
    }

    [Fact]
    public void KillUnitFeatureHasSingleTargetSelectionMode()
    {
        var features = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);
        var kill = Assert.Single(features, feature => feature.RawName == "Destory Select Unit");
        Assert.Equal(SelectionExecutionMode.SingleTarget, kill.SelectionMode);
    }

    [Fact]
    public void HealthMaxFeatureHasApplySelectionMode()
    {
        var features = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);
        var healthMax = Assert.Single(features, feature => feature.RawName == "Select Unit HP MAX");
        Assert.Equal(SelectionExecutionMode.Apply, healthMax.SelectionMode);
    }

    [Fact]
    public void AllSelectionBasedFeaturesHaveSelectionMode()
    {
        var uiFeatures = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);
        var panelActions = TrainerFeatureCatalog.CreatePanelActions();
        var features = uiFeatures.Concat(panelActions).ToList();

        var selectionRawNames = new[]
        {
            "Select Unit HP MAX",
            "Select Unit HP MIN",
            "Restore Select Unit Normal HP",
            "Set Selected Unit Target Health",
            "Select Unit Super Speed",
            "Select Unit Slow Speed",
            "Select Unit Freeze",
            "Restore Select Unit Speed",
            "Fill Selected Unit Ammo",
            "Reset Selected Unit Ammo",
            "Select Unit Level UP",
            "Select Unit Change ID",
            "Destory Select Unit",
            "Select Unit Copy For Me",
            "Toggle Selected Unit Attack Speed",
            "Toggle Selected Unit Attack Range",
            "Clear Selected Attack Speed Effects",
            "Clear Selected Attack Range Effects",
            "Teleport Selected Units To Mouse",
            "Expand Production Queue",
            "Restore Production Queue",
            "Set Unit Support State",
            "Grant Selected Object Upgrade",
        };
        foreach (var rawName in selectionRawNames)
        {
            var feature = Assert.Single(features, f => f.RawName == rawName);
            Assert.NotNull(feature.SelectionMode);
        }
    }

    [Fact]
    public void ClearSelectedAttackSpeedEffectFeatureIsDirectGameApiAction()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);

        var clear = Assert.Single(features, f => f.RawName == "Clear Selected Attack Speed Effects");
        Assert.Equal("清空满攻速单位", clear.DisplayName);
        Assert.Null(clear.Hotkey);
        Assert.True(clear.RequiresDirectGameApi);
        Assert.Null(clear.DispatchTarget);
        Assert.True(clear.SupportsProfile("ra3_1.12"));
        Assert.True(clear.SupportsProfile("ra3_1.13"));
        Assert.True(clear.SupportsProfile("ra3_uprising_1.0"));
        Assert.True(clear.SupportsProfile("ra3_uprising_1.1"));
        Assert.Equal(SelectionExecutionMode.Apply, clear.SelectionMode);
    }

    [Fact]
    public void ClearSelectedAttackRangeEffectFeatureIsDirectGameApiAction()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);

        var clear = Assert.Single(features, f => f.RawName == "Clear Selected Attack Range Effects");
        Assert.Equal("清空无限射程单位", clear.DisplayName);
        Assert.Null(clear.Hotkey);
        Assert.True(clear.RequiresDirectGameApi);
        Assert.Null(clear.DispatchTarget);
        Assert.True(clear.SupportsProfile("ra3_1.12"));
        Assert.True(clear.SupportsProfile("ra3_1.13"));
        Assert.True(clear.SupportsProfile("ra3_uprising_1.0"));
        Assert.True(clear.SupportsProfile("ra3_uprising_1.1"));
        Assert.Equal(SelectionExecutionMode.Apply, clear.SelectionMode);
    }

    [Fact]
    public void SelectedUnitObjectUpgradeFeatureExistsWithCorrectProperties()
    {
        var feature = TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature;

        Assert.Equal("Selected Unit Object Upgrade", feature.RawName);
        Assert.Equal("单位升级", feature.DisplayName);
        Assert.Null(feature.Hotkey);
        Assert.Empty(feature.EnableFlags);
        Assert.Null(feature.DispatchTarget);
        Assert.Null(feature.ValueHint);
        Assert.Equal(new[] { "ra3_1.12" }, feature.SupportedProfileIds);
        Assert.True(feature.RequiresDirectGameApi);
        Assert.Null(feature.SelectionMode);
    }

    [Fact]
    public void SelectedUnitObjectUpgradeFeatureNotInGridFeatures()
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features);

        Assert.DoesNotContain(features, f => f.RawName == TrainerFeatureIds.SelectedUnitObjectUpgrade);
    }

    [Fact]
    public void SelectedUnitObjectUpgradeFeatureNotInPanelActions()
    {
        var actions = TrainerFeatureCatalog.CreatePanelActions();

        Assert.DoesNotContain(actions, f => f.RawName == TrainerFeatureIds.SelectedUnitObjectUpgrade);
    }

    [Fact]
    public void SelectedUnitObjectUpgradeFeatureNotInUiFeatures()
    {
        var features = TrainerFeatureCatalog.CreateUiFeatures(TestAssets.LoadManifest().Features);

        Assert.DoesNotContain(features, f => f.RawName == TrainerFeatureIds.SelectedUnitObjectUpgrade);
    }

}
