using RayaTrainer.App.Services;
using RayaTrainer.App.ViewModels;
using RayaTrainer.App.Web;
using RayaTrainer.App.Web.State;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerApiHandlerTests
{
    [Fact]
    public void GetStatusReportsSharedSessionState()
    {
        var session = new FakeTrainerSessionService
        {
            ArePatchesInstalledValue = true,
            CanUseFeaturesValue = true,
            FeatureControllerValue = new FakeFeatureController(),
            TargetProcessIdValue = 1234,
            InstalledHookCountValue = 35
        };
        var handler = new TrainerApiHandler(session, new GameApiCommandQueue(), CreateFeatures());

        var status = handler.GetStatus();

        Assert.True(status.PatchesInstalled);
        Assert.False(status.AgentReady);
        Assert.Equal(1234, status.TargetProcessId);
        Assert.Equal(35, status.InstalledHookCount);
    }

    [Fact]
    public void GetDiagnosticsReturnsStructuredSnapshotAndNewestFiftyEvents()
    {
        var session = new TrainerSessionManager();
        for (var index = 1; index <= 60; index++)
        {
            session.RecordDiagnosticEvent(DiagnosticEventSeverity.Info, $"test.{index}", $"事件 {index}");
        }
        var handler = new TrainerApiHandler(session, new GameApiCommandQueue(), CreateFeatures());

        var snapshot = handler.GetDiagnostics();

        Assert.Equal(TrainerDiagnosticHealth.Offline, snapshot.Health);
        Assert.Equal(50, snapshot.RecentEvents.Count);
        Assert.Equal("test.11", snapshot.RecentEvents[0].Code);
        Assert.Equal("test.60", snapshot.RecentEvents[^1].Code);
    }

    [Fact]
    public async Task SetToggleWritesFeatureToggle()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.SetToggleAsync(new TrainerToggleRequest("Zoom", true));

        Assert.True(result.Success);
        Assert.Equal("Zoom", controller.LastToggleFeature?.RawName);
        Assert.True(controller.LastToggleEnabled);
    }

    [Fact]
    public async Task SetToggleAsync_ViaCoordinator_UpdatesDesiredState()
    {
        var (handler, controller, coordinator, items) = CreateHandlerWithCoordinator();

        var result = await handler.SetToggleAsync(new TrainerToggleRequest("Zoom", true));

        Assert.True(result.Success);
        Assert.True(items["Zoom"].DesiredEnabled);
        Assert.True(items["Zoom"].ObservedEnabled);
    }

    [Fact]
    public async Task WriteResourcesWritesRuntimeValues()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.WriteResourcesAsync(new TrainerResourceRequest(123456, 234567, 9));

        Assert.True(result.Success);
        Assert.Equal(new ResourceValueSettings(123456, 234567, 9), controller.LastResourceValues);
    }

    [Fact]
    public async Task WriteResourcesRejectsOutOfRangeValues()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.WriteResourcesAsync(new TrainerResourceRequest(0, 100000, 15));

        Assert.False(result.Success);
        Assert.Contains("资源参数无效", result.Message, StringComparison.Ordinal);
        Assert.Null(controller.LastResourceValues);
    }

    [Fact]
    public async Task ExecuteReinforcementWritesSettingsAndDispatchesAction()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.ExecuteReinforcementAsync(new TrainerReinforcementRequest(0x6586A5A0, 8, 3));

        Assert.True(result.Success);
        Assert.Equal(new ReinforcementSettings(0x6586A5A0, 8, 3), controller.LastReinforcementSettings);
        Assert.Equal("We Need Back", controller.LastActionFeature?.RawName);
    }

    [Fact]
    public async Task ExecuteReinforcementRejectsInvalidSettings()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.ExecuteReinforcementAsync(new TrainerReinforcementRequest(0, 8, 3));

        Assert.False(result.Success);
        Assert.Contains("增援参数无效", result.Message, StringComparison.Ordinal);
        Assert.Null(controller.LastActionFeature);
    }

    [Fact]
    public async Task GrantSecretProtocolWritesSettingsAndDispatchesPanelAction()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.GrantSecretProtocolAsync(new TrainerSecretProtocolRequest(0xDD6C4C5B, 0x33D87C97));

        Assert.True(result.Success);
        Assert.Equal(new SecretProtocolGrantSettings(0xDD6C4C5B, 0x33D87C97), controller.LastSecretProtocolSettings);
        Assert.Equal("Grant Secret Protocol", controller.LastActionFeature?.RawName);
    }

    [Fact]
    public async Task GrantSecretProtocolRejectsEmptyProtocolIds()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.GrantSecretProtocolAsync(new TrainerSecretProtocolRequest(0, 0));

        Assert.False(result.Success);
        Assert.Contains("秘密协议参数无效", result.Message, StringComparison.Ordinal);
        Assert.Null(controller.LastActionFeature);
    }

    [Fact]
    public void GetPresetsReadsSavedTrainerAppSettings()
    {
        var settingsStore = CreateSettingsStore(
            [
                new ReinforcementPreset("两波增援",
                [
                    new ReinforcementPresetEntry("4架双刃", 0x4B816FC8, 4, 0),
                    new ReinforcementPresetEntry("2辆MCV", 0xAF4C0DA5, 2, 1)
                ])
            ],
            [
                new SecretProtocolQueuePreset("盟军空战",
                [
                    new SecretProtocolPresetEntry("原版 RA3", "盟军", "先进航空学", 0xDD6C4C5B, 0x33D87C97),
                    new SecretProtocolPresetEntry("原版 RA3", "盟军", "高科技", 0x7A9E4201, 0x3AC47A99)
                ])
            ]);
        var handler = CreateConnectedHandler(new FakeFeatureController(), settingsStore);

        var presets = handler.GetPresets();

        var reinforcement = Assert.Single(presets.ReinforcementPresets);
        Assert.Equal("两波增援", reinforcement.Name);
        Assert.Equal(2, reinforcement.Entries.Count);
        Assert.Equal("4架双刃", reinforcement.Entries[0].Name);
        Assert.Equal("0x4B816FC8", reinforcement.Entries[0].UnitIdText);
        Assert.Equal(4, reinforcement.Entries[0].Count);
        Assert.Equal(0, reinforcement.Entries[0].Rank);
        Assert.Equal("2辆MCV", reinforcement.Entries[1].Name);
        Assert.Equal("0xAF4C0DA5", reinforcement.Entries[1].UnitIdText);

        var secretProtocol = Assert.Single(presets.SecretProtocolPresets);
        Assert.Equal("盟军空战", secretProtocol.Name);
        Assert.Equal(2, secretProtocol.Entries.Count);
        Assert.Equal("先进航空学", secretProtocol.Entries[0].Name);
        Assert.Equal("0xDD6C4C5B", secretProtocol.Entries[0].PlayerTechIdText);
        Assert.Equal("0x33D87C97", secretProtocol.Entries[0].UpgradeIdText);
    }

    [Fact]
    public void GetFeaturesIncludesEveryFeatureWithCapability()
    {
        var features = CreateFeatures()
            .Concat(
            [
                new TrainerFeature("Replace Template Model", "替换模板模型", null, [], "MustCode2+D00", "0x21"),
                new TrainerFeature("Replace Template Weapon", "替换模板武器", null, [], "MustCode2+D00", "0x22")
            ])
            .ToArray();
        var handler = new TrainerApiHandler(
            new FakeTrainerSessionService(),
            new GameApiCommandQueue(),
            features);

        var response = handler.GetFeatures();

        Assert.Contains(response.Features, feature => feature.Id == "Replace Template Model" && feature.RequiresParameters);
        Assert.Contains(response.Features, feature => feature.Id == "Replace Template Weapon" && feature.RequiresParameters);
        Assert.All(response.Features, feature =>
        {
            Assert.Equal("Waiting", feature.CapabilityState);
            Assert.Equal("NO_TARGET", feature.CapabilityReasonCode);
            Assert.NotEmpty(feature.CapabilityReason);
        });
    }

    [Fact]
    public void GetPresetsUsesLiveDesktopPresetSourceBeforeSettingsFile()
    {
        var settingsStore = CreateSettingsStore([], []);
        var presetSource = new FakeTrainerPresetSource(
            [
                new ReinforcementPreset("当前桌面增援", 0x6586A5A0, 8, 3)
            ],
            [
                new SecretProtocolQueuePreset("当前桌面协议",
                [
                    new SecretProtocolPresetEntry("原版 RA3", "盟军", "先进航空学", 0xDD6C4C5B, 0x33D87C97)
                ])
            ]);
        var handler = CreateConnectedHandler(new FakeFeatureController(), settingsStore, presetSource);

        var presets = handler.GetPresets();

        Assert.Equal("当前桌面增援", Assert.Single(presets.ReinforcementPresets).Name);
        Assert.Equal("当前桌面协议", Assert.Single(presets.SecretProtocolPresets).Name);
    }

    [Fact]
    public void GetPresetsMergesLiveDesktopPresetsWithSavedSettingsPresets()
    {
        var settingsStore = CreateSettingsStore(
            [
                new ReinforcementPreset("保存的桌面增援", 0x4B816FC8, 4, 0)
            ],
            [
                new SecretProtocolQueuePreset("保存的桌面协议",
                [
                    new SecretProtocolPresetEntry("原版 RA3", "苏联", "轨道垃圾", 0x11111111, 0)
                ])
            ]);
        var presetSource = new FakeTrainerPresetSource(
            [
                new ReinforcementPreset("当前桌面增援", 0x6586A5A0, 8, 3)
            ],
            [
                new SecretProtocolQueuePreset("当前桌面协议",
                [
                    new SecretProtocolPresetEntry("原版 RA3", "盟军", "先进航空学", 0xDD6C4C5B, 0x33D87C97)
                ])
            ]);
        var handler = CreateConnectedHandler(new FakeFeatureController(), settingsStore, presetSource);

        var presets = handler.GetPresets();

        Assert.Equal(
            ["当前桌面增援", "保存的桌面增援"],
            presets.ReinforcementPresets.Select(preset => preset.Name).ToArray());
        Assert.Equal(
            ["当前桌面协议", "保存的桌面协议"],
            presets.SecretProtocolPresets.Select(preset => preset.Name).ToArray());
    }

    [Fact]
    public void GetPresetsMergesAdditionalSavedDesktopSettingsPresets()
    {
        var settingsStore = CreateSettingsStore([], []);
        var savedSettingsSource = new FakeTrainerSavedPresetSource(
            [
                new TrainerAppSettings(
                    LauncherPath: string.Empty,
                    LauncherArguments: "-ui",
                    AttachTimeoutSeconds: 30,
                    ResourceValues: ResourceValueSettings.Default,
                    ReinforcementPresets:
                    [
                        new ReinforcementPreset("另一路保存增援", 0x4B816FC8, 4, 0)
                    ],
                    Hotkeys: new Dictionary<string, string>(),
                    ModsRootPath: string.Empty,
                    SelectedModSkudefPath: string.Empty,
                    SecretProtocolPresets:
                    [
                        new SecretProtocolQueuePreset("另一路保存协议",
                        [
                            new SecretProtocolPresetEntry("原版 RA3", "盟军", "先进航空学", 0xDD6C4C5B, 0x33D87C97)
                        ])
                    ])
            ]);
        var handler = CreateConnectedHandler(
            new FakeFeatureController(),
            settingsStore,
            presetSource: null,
            savedPresetSource: savedSettingsSource);

        var presets = handler.GetPresets();

        Assert.Equal("另一路保存增援", Assert.Single(presets.ReinforcementPresets).Name);
        Assert.Equal("另一路保存协议", Assert.Single(presets.SecretProtocolPresets).Name);
    }

    [Fact]
    public async Task ExecuteReinforcementQueueExecutesMobileQueueEntriesInOrder()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.ExecuteReinforcementQueueAsync(new TrainerReinforcementQueueRequest(
        [
            new TrainerReinforcementRequest(0x6586A5A0, 8, 3),
            new TrainerReinforcementRequest(0x4B816FC8, 4, 0)
        ]));

        Assert.True(result.Success);
        Assert.Equal("We Need Back", controller.LastActionFeature?.RawName);
        Assert.Equal(
            [
                new ReinforcementSettings(0x6586A5A0, 8, 3),
                new ReinforcementSettings(0x4B816FC8, 4, 0)
            ],
            controller.ReinforcementSettingsHistory);
        Assert.NotNull(result.Items);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, r => Assert.Equal("Executed", r.Status));
    }

    [Fact]
    public async Task ExecuteReinforcementQueueRejectsInvalidEntriesBeforeDispatch()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.ExecuteReinforcementQueueAsync(new TrainerReinforcementQueueRequest(
        [
            new TrainerReinforcementRequest(0, 8, 3)
        ]));

        Assert.False(result.Success);
        Assert.Contains("增援队列参数无效", result.Message, StringComparison.Ordinal);
        Assert.Null(controller.LastActionFeature);
    }

    [Fact]
    public async Task GrantSecretProtocolQueueExecutesMobileQueueEntriesInOrder()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.GrantSecretProtocolQueueAsync(new TrainerSecretProtocolQueueRequest(
        [
            new TrainerSecretProtocolRequest(0xDD6C4C5B, 0x33D87C97),
            new TrainerSecretProtocolRequest(0x7A9E4201, 0x3AC47A99)
        ]));

        Assert.True(result.Success);
        Assert.Equal("Grant Secret Protocol", controller.LastActionFeature?.RawName);
        Assert.Equal(
            [
                new SecretProtocolGrantSettings(0xDD6C4C5B, 0x33D87C97),
                new SecretProtocolGrantSettings(0x7A9E4201, 0x3AC47A99)
            ],
            controller.SecretProtocolSettingsHistory);
        Assert.NotNull(result.Items);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, r => Assert.Equal("Executed", r.Status));
    }

    [Fact]
    public async Task GrantSecretProtocolQueueRejectsEmptyProtocolEntriesBeforeDispatch()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.GrantSecretProtocolQueueAsync(new TrainerSecretProtocolQueueRequest(
        [
            new TrainerSecretProtocolRequest(0, 0)
        ]));

        Assert.False(result.Success);
        Assert.Contains("秘密协议队列参数无效", result.Message, StringComparison.Ordinal);
        Assert.Null(controller.LastActionFeature);
    }

    [Fact]
    public async Task ExecuteReinforcementQueueBroadcastsPauseWaitStatus()
    {
        var controller = new FakeFeatureController { DispatchStatusToReport = DispatchWaitStatus.PausedWaiting };
        var broadcaster = new FakeGameStateBroadcaster();
        var handler = CreateConnectedHandler(controller, broadcaster: broadcaster);

        await handler.ExecuteReinforcementQueueAsync(new TrainerReinforcementQueueRequest(
        [
            new TrainerReinforcementRequest(0x6586A5A0, 8, 3)
        ]));

        Assert.Contains(
            broadcaster.Messages,
            message => message.Type == "status" && message.Message.Contains("等待恢复", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GrantSecretProtocolQueueBroadcastsPauseWaitStatus()
    {
        var controller = new FakeFeatureController { DispatchStatusToReport = DispatchWaitStatus.PausedWaiting };
        var broadcaster = new FakeGameStateBroadcaster();
        var handler = CreateConnectedHandler(controller, broadcaster: broadcaster);

        await handler.GrantSecretProtocolQueueAsync(new TrainerSecretProtocolQueueRequest(
        [
            new TrainerSecretProtocolRequest(0xDD6C4C5B, 0x33D87C97)
        ]));

        Assert.Contains(
            broadcaster.Messages,
            message => message.Type == "status" && message.Message.Contains("等待恢复", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteActionRejectsParameterizedActionsWithoutDedicatedEndpoint()
    {
        var controller = new FakeFeatureController();
        var handler = CreateConnectedHandler(controller);

        var result = await handler.ExecuteActionAsync(
            "We Need Back",
            new TrainerActionRequest(null, null, null, null, null, null, null, null, null));

        Assert.False(result.Success);
        Assert.Contains("专用", result.Message, StringComparison.Ordinal);
        Assert.Null(controller.LastActionFeature);
    }

    [Fact]
    public async Task CommandsFailWhenPatchesAreNotReady()
    {
        var handler = new TrainerApiHandler(new FakeTrainerSessionService(), new GameApiCommandQueue(), CreateFeatures());

        var result = await handler.SetToggleAsync(new TrainerToggleRequest("Zoom", true));

        Assert.False(result.Success);
        Assert.Contains("待连接", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToggleCommandUsesCapabilityReasonAsTheSingleFailureSource()
    {
        var session = new FakeTrainerSessionService
        {
            CapabilityReason = "相关 Hook 已安全跳过。"
        };
        var handler = new TrainerApiHandler(session, new GameApiCommandQueue(), CreateFeatures());

        var result = await handler.SetToggleAsync(new TrainerToggleRequest("Zoom", true));

        Assert.False(result.Success);
        Assert.Equal("相关 Hook 已安全跳过。", result.Message);
    }

    [Fact]
    public void GetReinforcementCatalogReturnsEntries()
    {
        var handler = new TrainerApiHandler(new FakeTrainerSessionService(), new GameApiCommandQueue(), CreateFeatures());

        var catalog = handler.GetReinforcementCatalog();

        Assert.NotNull(catalog.Entries);
        Assert.NotEmpty(catalog.Entries);
        Assert.All(catalog.Entries, entry =>
        {
            Assert.False(string.IsNullOrEmpty(entry.Name));
            Assert.False(string.IsNullOrEmpty(entry.Faction));
            Assert.False(string.IsNullOrEmpty(entry.CodeText));
        });
    }

    [Fact]
    public void GetSecretProtocolCatalogReturnsEntries()
    {
        var handler = new TrainerApiHandler(new FakeTrainerSessionService(), new GameApiCommandQueue(), CreateFeatures());

        var catalog = handler.GetSecretProtocolCatalog();

        Assert.NotNull(catalog.Entries);
        Assert.NotEmpty(catalog.Entries);
        Assert.All(catalog.Entries, entry =>
        {
            Assert.False(string.IsNullOrEmpty(entry.Name));
            Assert.False(string.IsNullOrEmpty(entry.Faction));
        });
        // 原版 RA3 至少有可授予的协议
        Assert.Contains(catalog.Entries, e => e.CanGrant);
    }

    // ── 单位升级（能力门控 + 响应映射） ──

    [Fact]
    public async Task GrantObjectUpgradeRejectsWhenCapabilityNotReady()
    {
        var controller = new FakeFeatureController { GrantObjectUpgradeResult = GameApiDispatchStatus.Completed };
        var session = new FakeTrainerSessionService
        {
            ArePatchesInstalledValue = true,
            CanUseFeaturesValue = true,
            FeatureControllerValue = controller,
            CapabilityReason = "该功能在当前版本下不可用。"
        };
        var handler = new TrainerApiHandler(session, new GameApiCommandQueue(), CreateFeatures());

        var result = await handler.GrantObjectUpgradeOnSelectedSameTypeAsync(0x12345678);

        Assert.False(result.Success);
        Assert.Equal("PROFILE_OR_HOOK_UNAVAILABLE", result.ReasonCode);
        Assert.Null(controller.LastGrantedUpgradeHash);
    }

    [Fact]
    public async Task GrantObjectUpgradeRejectsZeroHash()
    {
        var handler = CreateUnitUpgradeReadyHandler(new FakeFeatureController
            { GrantObjectUpgradeResult = GameApiDispatchStatus.Completed });

        var result = await handler.GrantObjectUpgradeOnSelectedSameTypeAsync(0);

        Assert.False(result.Success);
        Assert.Equal("INVALID_UPGRADE_HASH", result.ReasonCode);
    }

    [Fact]
    public async Task GrantObjectUpgradeMapsCompletedToSuccess()
    {
        var controller = new FakeFeatureController { GrantObjectUpgradeResult = GameApiDispatchStatus.Completed };
        var handler = CreateUnitUpgradeReadyHandler(controller);

        var result = await handler.GrantObjectUpgradeOnSelectedSameTypeAsync(0x12345678);

        Assert.True(result.Success);
        Assert.Equal("升级已授予。", result.Message);
        Assert.Null(result.ReasonCode);
        Assert.Equal(0x12345678u, controller.LastGrantedUpgradeHash);
    }

    [Fact]
    public async Task GrantObjectUpgradeMapsDisabledToFailure()
    {
        var controller = new FakeFeatureController { GrantObjectUpgradeResult = GameApiDispatchStatus.Disabled };
        var handler = CreateUnitUpgradeReadyHandler(controller);

        var result = await handler.GrantObjectUpgradeOnSelectedSameTypeAsync(0x12345678);

        Assert.False(result.Success);
        Assert.Equal("GRANT_DISABLED", result.ReasonCode);
        Assert.Equal(0x12345678u, controller.LastGrantedUpgradeHash);
    }

    [Fact]
    public async Task GrantObjectUpgradeMapsTimedOutToFailure()
    {
        var controller = new FakeFeatureController { GrantObjectUpgradeResult = GameApiDispatchStatus.TimedOut };
        var handler = CreateUnitUpgradeReadyHandler(controller);

        var result = await handler.GrantObjectUpgradeOnSelectedSameTypeAsync(0x12345678);

        Assert.False(result.Success);
        Assert.Equal("GRANT_TIMEOUT", result.ReasonCode);
    }

    [Fact]
    public async Task GrantObjectUpgradeMapsFailedToFailure()
    {
        var controller = new FakeFeatureController { GrantObjectUpgradeResult = GameApiDispatchStatus.Failed };
        var handler = CreateUnitUpgradeReadyHandler(controller);

        var result = await handler.GrantObjectUpgradeOnSelectedSameTypeAsync(0x12345678);

        Assert.False(result.Success);
        Assert.Equal("GRANT_FAILED", result.ReasonCode);
    }

    [Fact]
    public async Task GrantObjectUpgradeForwardsAnyHashToControllerWithoutPreFilter()
    {
        var controller = new FakeFeatureController { GrantObjectUpgradeResult = GameApiDispatchStatus.Completed };
        var handler = CreateUnitUpgradeReadyHandler(controller);

        // A forged PLAYER-type hash (0x00000001) — the Web layer must forward it
        // without checking against the GET history. The native layer rejects it.
        var result = await handler.GrantObjectUpgradeOnSelectedSameTypeAsync(0x00000001);

        Assert.True(result.Success); // Completed from the controller stub; native would reject
        Assert.Equal(0x00000001u, controller.LastGrantedUpgradeHash);
    }

    [Fact]
    public void ReadSelectedUnitUpgradesReturnsNullWhenNoController()
    {
        var handler = new TrainerApiHandler(
            new FakeTrainerSessionService(), new GameApiCommandQueue(), CreateFeatures());

        Assert.Null(handler.ReadSelectedUnitUpgrades());
    }

    [Fact]
    public void ReadSelectedUnitUpgradesShowsGuidanceWhenNoSelection()
    {
        var snapshot = new SelectedUnitUpgradesSnapshot(0, 0, 0, Array.Empty<uint>());
        var controller = new FakeFeatureController { ReadSelectedUnitUpgradesResult = snapshot };
        var handler = CreateConnectedHandler(controller);

        var result = handler.ReadSelectedUnitUpgrades();

        Assert.NotNull(result);
        Assert.Equal(0u, result.UnitTypeId);
        Assert.Empty(result.Upgrades);
        Assert.Equal("请先在游戏中选中一个单位", result.Message);
    }

    [Fact]
    public void ReadSelectedUnitUpgradesShowsGuidanceWhenEmptyList()
    {
        var snapshot = new SelectedUnitUpgradesSnapshot(0x12345678, 0x1000, 0, Array.Empty<uint>());
        var controller = new FakeFeatureController { ReadSelectedUnitUpgradesResult = snapshot };
        var handler = CreateConnectedHandler(controller);

        var result = handler.ReadSelectedUnitUpgrades();

        Assert.NotNull(result);
        Assert.Equal(0x12345678u, result.UnitTypeId);
        Assert.Empty(result.Upgrades);
        Assert.Equal("当前单位没有可授予的对象级升级", result.Message);
    }

    [Fact]
    public void ReadSelectedUnitUpgradesResolvesNameViaResolver()
    {
        // 0x33D87C97 is a known upgrade hash in the embedded UpgradeNames.json
        var hashes = new uint[] { 0x33D87C97 };
        var snapshot = new SelectedUnitUpgradesSnapshot(0x12345678, 0x1000, 1, hashes);
        var controller = new FakeFeatureController { ReadSelectedUnitUpgradesResult = snapshot };
        var handler = CreateConnectedHandler(controller);

        var result = handler.ReadSelectedUnitUpgrades();

        Assert.NotNull(result);
        var upgrade = Assert.Single(result.Upgrades);
        Assert.Equal(0x33D87C97u, upgrade.Hash);
        Assert.NotEmpty(upgrade.Name);
        Assert.DoesNotContain("0x", upgrade.Name);
    }

    [Fact]
    public void ReadSelectedUnitUpgradesFallsBackToHexForUnknownHash()
    {
        var hashes = new uint[] { 0xDEADBEEF };
        var snapshot = new SelectedUnitUpgradesSnapshot(0x12345678, 0x1000, 1, hashes);
        var controller = new FakeFeatureController { ReadSelectedUnitUpgradesResult = snapshot };
        var handler = CreateConnectedHandler(controller);

        var result = handler.ReadSelectedUnitUpgrades();

        Assert.NotNull(result);
        var upgrade = Assert.Single(result.Upgrades);
        Assert.Equal(0xDEADBEEFu, upgrade.Hash);
        Assert.Equal("升级 #0xDEADBEEF", upgrade.Name);
        Assert.Empty(upgrade.Description);
    }

    private static TrainerApiHandler CreateUnitUpgradeReadyHandler(
        FakeFeatureController controller)
    {
        return new TrainerApiHandler(
            new FakeTrainerSessionService
            {
                ArePatchesInstalledValue = true,
                CanUseFeaturesValue = true,
                FeatureControllerValue = controller,
                DirectGameApiReadyOverride = true
            },
            new GameApiCommandQueue(),
            CreateFeatures());
    }

    private static TrainerApiHandler CreateConnectedHandler(
        FakeFeatureController controller,
        TrainerAppSettingsStore? settingsStore = null,
        ITrainerPresetSource? presetSource = null,
        ITrainerSavedPresetSource? savedPresetSource = null,
        IGameStateBroadcaster? broadcaster = null)
    {
        return new TrainerApiHandler(
            new FakeTrainerSessionService
            {
                ArePatchesInstalledValue = true,
                CanUseFeaturesValue = true,
                FeatureControllerValue = controller
            },
            new GameApiCommandQueue(),
            CreateFeatures(),
            settingsStore: settingsStore,
            presetSource: presetSource,
            savedPresetSource: savedPresetSource,
            broadcaster: broadcaster);
    }

    private static TrainerAppSettingsStore CreateSettingsStore(
        IReadOnlyList<ReinforcementPreset> reinforcementPresets,
        IReadOnlyList<SecretProtocolQueuePreset> secretProtocolPresets)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new TrainerAppSettingsStore(path);
        store.Save(new TrainerAppSettings(
            LauncherPath: string.Empty,
            LauncherArguments: "-ui",
            AttachTimeoutSeconds: 30,
            ResourceValues: ResourceValueSettings.Default,
            ReinforcementPresets: reinforcementPresets,
            Hotkeys: new Dictionary<string, string>(),
            ModsRootPath: string.Empty,
            SelectedModSkudefPath: string.Empty,
            SecretProtocolPresets: secretProtocolPresets));
        return store;
    }

    private static IReadOnlyList<TrainerFeature> CreateFeatures()
    {
        return
        [
            new TrainerFeature("Zoom", "无限缩放", null, ["iEnable+F"], null, null),
            new TrainerFeature("We Need Back", "呼叫战场增援", null, [], "MustCode2+B00", "0x0C"),
            TrainerFeatureCatalog.SecretProtocolGrantFeature
        ];
    }

    private static (TrainerApiHandler Handler, FakeFeatureController Controller, FeatureStateCoordinator Coordinator, Dictionary<string, FeatureItemViewModel> Items) CreateHandlerWithCoordinator()
    {
        var controller = new FakeFeatureController();
        var features = CreateFeatures();
        var items = new Dictionary<string, FeatureItemViewModel>();
        var host = new FakeFeatureHost(controller);

        foreach (var f in features)
        {
            var item = new FeatureItemViewModel(f, host);
            items[f.RawName] = item;
        }

        var coordinator = new FeatureStateCoordinator(
            () => items.Values,
            () => controller,
            f => host.GetFeatureCapability(f),
            Array.Empty<IFeatureParameterProvider>());

        var handler = new TrainerApiHandler(
            new FakeTrainerSessionService
            {
                ArePatchesInstalledValue = true,
                CanUseFeaturesValue = true,
                FeatureControllerValue = controller
            },
            new GameApiCommandQueue(),
            features,
            featureStateCoordinator: coordinator);

        return (handler, controller, coordinator, items);
    }

    private sealed class FakeFeatureHost : IFeatureHost
    {
        private readonly FakeFeatureController _controller;

        public FakeFeatureHost(FakeFeatureController controller) => _controller = controller;

        public bool ArePatchesInstalled => true;
        public ITrainerFeatureController? FeatureController => _controller;
        public string StatusMessage { set => throw new NotSupportedException(); }

        public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature) =>
            new(feature.RawName, feature.DisplayName, string.Empty, FeatureCapabilityState.Ready, null, null);

        public void WriteResourceValuesIfNeeded(TrainerFeature feature) { }
        public void WriteTargetHealthIfNeeded(TrainerFeature feature) { }
        public void OnFeatureToggleChanged(TrainerFeature feature, bool enabled) { }
        public void CompleteActionIfNeeded(TrainerFeature feature, ActionDispatchResult result) { }
        public ReinforcementSettings GetReinforcementSettings() => throw new NotSupportedException();
        public void OpenHotkeySettings() { }
        public void ClearHotkey(TrainerFeature feature) { }
    }

    private sealed class FakeTrainerSessionService : ITrainerSessionService
    {
        public ITrainerFeatureController? FeatureController => FeatureControllerValue;

        public bool ArePatchesInstalled => ArePatchesInstalledValue;

        public int? TargetProcessId => TargetProcessIdValue;

        public bool CanUseFeatures => CanUseFeaturesValue;

        public int InstalledHookCount => InstalledHookCountValue;

        public string RemoteSymbolSummary => RemoteSymbolSummaryValue;

        public ITrainerFeatureController? FeatureControllerValue { get; init; }

        public bool ArePatchesInstalledValue { get; init; }

        public int? TargetProcessIdValue { get; init; }

        public bool CanUseFeaturesValue { get; init; }

        public int InstalledHookCountValue { get; init; }

        public string RemoteSymbolSummaryValue { get; init; } = "远程符号未分配。";

        public string? CapabilityReason { get; init; }

        /// <summary>
        /// When set, overrides the DirectGameApiReady check so tests can test
        /// features requiring Direct GameApi without a full IAgentFeatureController.
        /// </summary>
        public bool? DirectGameApiReadyOverride { get; init; }

        public AttachResult AttachTarget(TrainerManifest manifest, TrainerTarget target) => throw new NotSupportedException();

        public SessionInstallOutcome InstallPatches(TrainerManifest manifest, string diagnosticsDir) => throw new NotSupportedException();

        public void ResetPatchesState()
        {
        }

        public void MarkTargetOffline() => ResetPatchesState();

        public bool IsTargetGameForeground() => false;

        public FeatureCapabilitySnapshot GetFeatureCapability(TrainerFeature feature) =>
            TrainerFeatureCapabilityEvaluator.Evaluate(
                feature,
                new TrainerFeatureCapabilityContext(
                    TargetProcessId is not null || (ArePatchesInstalled && FeatureController is not null),
                    CanUseFeatures || FeatureController is not null,
                    ArePatchesInstalled,
                    true,
                    DirectGameApiReadyOverride ?? FeatureController is IAgentFeatureController { SupportsDirectGameApi: true },
                    CapabilityReason));

        public void Dispose()
        {
        }
    }

    private sealed class FakeFeatureController : ITrainerFeatureController
    {
        public TrainerFeature? LastToggleFeature { get; private set; }
        public bool? LastToggleEnabled { get; private set; }
        public TrainerFeature? LastActionFeature { get; private set; }
        public ReinforcementSettings? LastReinforcementSettings { get; private set; }
        public ResourceValueSettings? LastResourceValues { get; private set; }
        public SecretProtocolGrantSettings? LastSecretProtocolSettings { get; private set; }
        public List<ReinforcementSettings> ReinforcementSettingsHistory { get; } = [];
        public List<SecretProtocolGrantSettings> SecretProtocolSettingsHistory { get; } = [];
        public DispatchWaitStatus? DispatchStatusToReport { get; init; }

        // Unit upgrade test controls
        public SelectedUnitUpgradesSnapshot? ReadSelectedUnitUpgradesResult { get; init; }
        public GameApiDispatchStatus GrantObjectUpgradeResult { get; init; } = GameApiDispatchStatus.Disabled;
        public uint? LastGrantedUpgradeHash { get; private set; }

        public void SetToggle(TrainerFeature feature, bool enabled)
        {
            LastToggleFeature = feature;
            LastToggleEnabled = enabled;
        }

        public void TriggerAction(TrainerFeature feature)
        {
            LastActionFeature = feature;
        }

        public void TriggerAction(TrainerFeature feature, ReinforcementSettings? reinforcementSettings)
        {
            LastActionFeature = feature;
            LastReinforcementSettings = reinforcementSettings;
            if (reinforcementSettings is not null)
            {
                ReinforcementSettingsHistory.Add(reinforcementSettings);
            }
        }

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
            LastActionFeature = feature;
            LastReinforcementSettings = reinforcementSettings;
            if (reinforcementSettings is not null)
            {
                ReinforcementSettingsHistory.Add(reinforcementSettings);
            }
            onDispatched?.Invoke();
            if (DispatchStatusToReport is { } status)
            {
                onWaitStatusChanged?.Invoke(status);
            }
            return Task.FromResult(ActionDispatchResult.Consumed);
        }

        public void WriteReinforcementSettings(ReinforcementSettings settings)
        {
            LastReinforcementSettings = settings;
            ReinforcementSettingsHistory.Add(settings);
        }

        public void WriteResourceValues(ResourceValueSettings settings)
        {
            LastResourceValues = settings;
        }

        public void WriteSecretProtocolGrantSettings(SecretProtocolGrantSettings settings)
        {
            LastSecretProtocolSettings = settings;
            SecretProtocolSettingsHistory.Add(settings);
        }

        public void WriteTemplateModelReplacementSettings(TemplateModelReplacementSettings settings)
        {
        }

        public void WriteTemplateWeaponReplacementSettings(TemplateWeaponReplacementSettings settings)
        {
        }

        public SecretProtocolBindingProbeResult ReadSecretProtocolBindingProbeResult() => throw new NotSupportedException();

        public void PulseAutoRepair()
        {
        }

        public void ClearAutoRepairPulse()
        {
        }

        public void WriteTargetHealthValue(float targetHealth, float targetMaxHealth = 0f)
        {
        }

        public uint ReadSelectedUnitCode() => 0;
        public SelectedUnitUpgradesSnapshot ReadSelectedUnitUpgrades() =>
            ReadSelectedUnitUpgradesResult ?? SelectedUnitUpgradesSnapshot.Empty;
        public GameApiDispatchStatus GrantObjectUpgradeOnSelectedSameType(uint upgradeHash, TimeSpan? timeout = null)
        {
            LastGrantedUpgradeHash = upgradeHash;
            return GrantObjectUpgradeResult;
        }

        public byte ReadActionDispatch() => 0;
        public uint ReadGameThreadTick() => 1;

        public int ReadGameMode() => 0;

        public void Reset(TrainerFeature feature)
        {
        }

        public bool? ReadToggleState(TrainerFeature feature) => false;
        public bool? ReadPulseFired(TrainerFeature feature) => null;
        public bool IsPulseFeature(TrainerFeature feature) => false;
        public Task<FeatureStatesResponse> RefreshRuntimeStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new FeatureStatesResponse(AgentStatusCode.Ok, AgentProtocol.Version, Array.Empty<FeatureStateEntry>()));
    }

    private sealed class FakeTrainerPresetSource : ITrainerPresetSource
    {
        private readonly IReadOnlyList<ReinforcementPreset> _reinforcementPresets;
        private readonly IReadOnlyList<SecretProtocolQueuePreset> _secretProtocolPresets;

        public FakeTrainerPresetSource(
            IReadOnlyList<ReinforcementPreset> reinforcementPresets,
            IReadOnlyList<SecretProtocolQueuePreset> secretProtocolPresets)
        {
            _reinforcementPresets = reinforcementPresets;
            _secretProtocolPresets = secretProtocolPresets;
        }

        public IReadOnlyList<ReinforcementPreset> GetReinforcementPresets() => _reinforcementPresets;

        public IReadOnlyList<SecretProtocolQueuePreset> GetSecretProtocolPresets() => _secretProtocolPresets;

        public IReadOnlyList<FeaturePreset> GetFeaturePresets() => Array.Empty<FeaturePreset>();

        public void SaveFeaturePreset(string name, FeatureStateSnapshot snapshot) { }

        public bool DeleteFeaturePreset(string name) => false;
    }

    private sealed class FakeTrainerSavedPresetSource : ITrainerSavedPresetSource
    {
        private readonly IReadOnlyList<TrainerAppSettings> _settings;

        public FakeTrainerSavedPresetSource(IReadOnlyList<TrainerAppSettings> settings)
        {
            _settings = settings;
        }

        public IReadOnlyList<TrainerAppSettings> LoadSavedSettings() => _settings;
    }

    private sealed class FakeGameStateBroadcaster : IGameStateBroadcaster
    {
        public List<TrainerWebStateMessage> Messages { get; } = [];

        public System.Threading.Channels.ChannelReader<TrainerWebStateMessage> Subscribe(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Publish(TrainerWebStateMessage message)
        {
            Messages.Add(message);
        }

        public void StartPolling(
            Func<TrainerGameStateResponse?> gameStateProvider,
            Func<TrainerSelectedUnitResponse?> selectedUnitProvider,
            Func<TrainerFeaturesResponse?> featuresProvider)
        {
        }

        public void StopPolling()
        {
        }
    }
}
