using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentFeatureControllerTests
{
    private static readonly AgentStatusPayload AgentStatus = new(
        AgentStatusCode.Ok,
        AgentProtocol.Version,
        ProcessId: 1234,
        ModuleBase: 0x400000,
        InstalledHookCount: 24);

    [Fact]
    public void DirectGameApiMethodsRejectProfilesWithoutDirectSupport()
    {
        var controller = new AgentFeatureController(
            new FakeAgentClient(),
            1234,
            AgentStatus,
            supportsDirectGameApi: false);

        Assert.False(controller.SupportsDirectGameApi);
        Assert.Throws<NotSupportedException>(() => controller.GetThingClass(1));
        Assert.Throws<NotSupportedException>(() => controller.ReadSelectedUnitCode());
    }

    [Fact]
    public void ReadSelectedUnitCodeUsesGameApiMailboxThroughAgent()
    {
        var client = new FakeAgentClient();
        client.EnqueueRead(0xCE9838, [0xEF, 0xBE, 0xAD, 0xDE]);
        client.EnqueueSelectedUnitSnapshotViaGameApi(0x6586A5A0, 0x0D7DF7E0);
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        var code = controller.ReadSelectedUnitCode();

        Assert.Equal(0x6586A5A0u, code);
        Assert.Empty(client.ReadRequests);
        Assert.Equal(1, client.ReadSelectedUnitSnapshotApiCallCount);
    }

    [Fact]
    public void SetSelectedStatusBitClampsMailboxTimeoutToAgentLimit()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        var result = controller.SetSelectedStatusBit(
            (uint)StatusBitDomain.ObjectStatus,
            17,
            1);

        Assert.Equal(GameApiDispatchStatus.Completed, result);
        Assert.Equal(5000u, client.LastSetSelectedStatusBitRequest!.TimeoutMilliseconds);
        Assert.Equal(TimeSpan.FromSeconds(8), client.LastSetSelectedStatusBitPipeTimeout);
    }

    [Fact]
    public void ExpandProductionQueueUsesVersionIndependentGameApiContract()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        var result = controller.ExpandProductionQueue(999);

        Assert.Equal(GameApiDispatchStatus.Completed, result);
        Assert.Equal(999u, client.LastExpandProductionQueueRequest!.MaxQueueEntries);
        Assert.Equal(5000u, client.LastExpandProductionQueueRequest.TimeoutMilliseconds);
        Assert.Equal(TimeSpan.FromSeconds(8), client.LastExpandProductionQueuePipeTimeout);
    }

    [Theory]
    [InlineData("Expand Production Queue", 999u)]
    [InlineData("Restore Production Queue", 1u)]
    public async Task TriggerActionRoutesProductionQueueActionsToGameApi(
        string rawName,
        uint expectedMaxQueueEntries)
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            rawName,
            rawName,
            null,
            [],
            null,
            "0x19",
            RequiresDirectGameApi: true);

        var result = await controller.TriggerActionAndWaitForConsumptionAsync(feature);

        Assert.Equal(ActionDispatchResult.Consumed, result);
        Assert.Null(client.LastWriteCommand);
        Assert.Equal(expectedMaxQueueEntries, client.LastExpandProductionQueueRequest!.MaxQueueEntries);
    }

    [Fact]
    public async Task TriggerActionRoutesSelectedUnitTeleportToGameApi()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            "Teleport Selected Units To Mouse",
            "移动选中单位到鼠标位置",
            null,
            [],
            null,
            "0x1A",
            RequiresDirectGameApi: true);

        var result = await controller.TriggerActionAndWaitForConsumptionAsync(feature);

        Assert.Equal(ActionDispatchResult.Consumed, result);
        Assert.Null(client.LastWriteCommand);
        Assert.True(client.LastTeleportSelectedUnitsToMouseRequest!.EnableDirectGameApi);
        Assert.Equal(5000u, client.LastTeleportSelectedUnitsToMouseRequest.TimeoutMilliseconds);
        Assert.Equal(TimeSpan.FromSeconds(8), client.LastTeleportSelectedUnitsToMousePipeTimeout);
    }

    [Fact]
    public void ReadSelectedUnitUpgradesRejectsCountOverTwenty()
    {
        // Defense-in-depth: the native handler caps Count at 20, but a corrupted/forged payload
        // must not drive an unbounded allocation. Count=21 must throw InvalidDataException.
        var client = new FakeAgentClient
        {
            SelectedUnitUpgradesPayload = new AgentGameApiSelectedUnitUpgradesPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                UnitTypeId: 0x1111u,
                ThingTemplateAddress: 0x2222u,
                Count: 21u,
                UpgradeHash0: 0xAu, UpgradeHash1: 0, UpgradeHash2: 0, UpgradeHash3: 0,
                UpgradeHash4: 0, UpgradeHash5: 0, UpgradeHash6: 0, UpgradeHash7: 0,
                UpgradeHash8: 0, UpgradeHash9: 0, UpgradeHash10: 0, UpgradeHash11: 0,
                UpgradeHash12: 0, UpgradeHash13: 0, UpgradeHash14: 0, UpgradeHash15: 0,
                UpgradeHash16: 0, UpgradeHash17: 0, UpgradeHash18: 0, UpgradeHash19: 0,
                DispatchStatus: GameApiDispatchStatus.Completed,
                RequestId: 7u,
                GameThreadTickBefore: 0u,
                GameThreadTickAfter: 0u)
        };
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        Assert.Throws<InvalidDataException>(() => controller.ReadSelectedUnitUpgrades());
    }

    [Theory]
    [InlineData("Clear Selected Attack Speed Effects", "清除所有单位的满攻速效果")]
    [InlineData("Clear Selected Attack Range Effects", "清除所有单位的无限射程效果")]
    public async Task TriggerActionRoutesClearSelectedAttackEffectsToGameApi(
        string rawName,
        string displayName)
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            rawName,
            displayName,
            null,
            [],
            null,
            null,
            RequiresDirectGameApi: true,
            SelectionMode: SelectionExecutionMode.Apply);

        var result = await controller.TriggerActionAndWaitForConsumptionAsync(feature);

        Assert.Equal(ActionDispatchResult.Consumed, result);
        Assert.Null(client.LastWriteCommand);
        if (rawName == "Clear Selected Attack Speed Effects")
        {
            Assert.NotNull(client.LastClearSelectedAttackSpeedEffectsRequest);
            Assert.True(client.LastClearSelectedAttackSpeedEffectsRequest!.EnableDirectGameApi);
            Assert.Equal(5000u, client.LastClearSelectedAttackSpeedEffectsRequest.TimeoutMilliseconds);
            Assert.Equal(TimeSpan.FromSeconds(8), client.LastClearSelectedAttackSpeedEffectsPipeTimeout);
        }
        else
        {
            Assert.NotNull(client.LastClearSelectedAttackRangeEffectsRequest);
            Assert.True(client.LastClearSelectedAttackRangeEffectsRequest!.EnableDirectGameApi);
            Assert.Equal(5000u, client.LastClearSelectedAttackRangeEffectsRequest.TimeoutMilliseconds);
            Assert.Equal(TimeSpan.FromSeconds(8), client.LastClearSelectedAttackRangeEffectsPipeTimeout);
        }
    }

    [Theory]
    [InlineData("Select Unit HP MAX", "MustCode2+400", "0x05", 2u)]
    [InlineData("Select Unit HP MIN", "MustCode2+500", "0x06", 3u)]
    [InlineData("Restore Select Unit Normal HP", "MustCode2+600", "0x07", 4u)]
    public void TriggerActionRoutesSelectedUnitHealthActionsToGameApi(
        string rawName,
        string dispatchTarget,
        string valueHint,
        uint expectedMode)
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(rawName, rawName, null, [], dispatchTarget, valueHint);

        controller.TriggerAction(feature);

        Assert.Null(client.LastWriteCommand);
        Assert.Equal(expectedMode, client.LastSetSelectedUnitHealthRequest!.Mode);
    }

    [Fact]
    public async Task TriggerActionAndWaitForConsumptionReturnsTimedOutWhenHealthGameApiTimesOut()
    {
        var client = new FakeAgentClient { SetSelectedUnitHealthDispatchStatus = GameApiDispatchStatus.TimedOut };
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature("Select Unit HP MAX", "Select Unit HP MAX", "#219", [], "MustCode2+400", "0x05");

        var result = await controller.TriggerActionAndWaitForConsumptionAsync(
            feature,
            timeout: TimeSpan.FromMilliseconds(100),
            pollInterval: TimeSpan.FromMilliseconds(1));

        Assert.Equal(ActionDispatchResult.TimedOut, result);
        Assert.Null(client.LastWriteCommand);
        Assert.Empty(client.ReadRequests);
        Assert.Equal(2u, client.LastSetSelectedUnitHealthRequest!.Mode);
    }

    [Fact]
    public void ReadGameModeUsesSemanticAgentCommandWithoutMemoryReads()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        var gameMode = controller.ReadGameMode();

        Assert.Equal(GameRuntimeConstants.GameModeShell, gameMode);
        Assert.Equal(1, client.GameModeCallCount);
        Assert.Empty(client.ReadRequests);
    }

    [Fact]
    public void WriteResourceValuesSendsNativeFeatureStateEntries()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteResourceValues(new ResourceValueSettings(123456, 234567, 9));

        // L4: SetFeatureStatesAsync should be called with resource value entries
        Assert.Equal(AgentCommand.SetFeatureStates, client.LastWriteCommand);
        Assert.True(client.LastSetFeatureStatesRequest.HasValue);
        var resourceStates = client.LastSetFeatureStatesRequest.Value.States;
        Assert.Contains(resourceStates,
            s => s.StateId == (uint)NativeFeatureStateId.MoneyAmount && s.Value == 123456);
        Assert.Contains(resourceStates,
            s => s.StateId == (uint)NativeFeatureStateId.PowerValue && s.Value == 234567);
        Assert.Contains(resourceStates,
            s => s.StateId == (uint)NativeFeatureStateId.SecretProtocolPointValue && s.Value == 9);
    }

    [Fact]
    public void SetToggleWritesNativeFeatureStateEntryForMappedFeature()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature("Player God Mode", "无敌", null, [], null, null);

        controller.SetToggle(feature, true);

        // L4: SetFeatureStatesAsync should be called with GodMode=1
        Assert.Equal(AgentCommand.SetFeatureStates, client.LastWriteCommand);
        Assert.True(client.LastSetFeatureStatesRequest.HasValue);
        var toggleStates = client.LastSetFeatureStatesRequest.Value.States;
        Assert.Single(toggleStates);
        Assert.Equal((uint)NativeFeatureStateId.GodMode, toggleStates[0].StateId);
        Assert.Equal(1u, toggleStates[0].Value);
    }

    [Fact]
    public void SetToggle_FrameRateUnlock_CallsSetRuntimePatchSet()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            "Frame Rate Unlock 60fps",
            "60fps 帧率解锁",
            null,
            ["Frame Rate Unlock 60fps"],
            null,
            null);

        controller.SetToggle(feature, true);

        // L5: state write (cmd 5) + PatchSet enable (cmd 6)
        Assert.Equal(AgentCommand.SetFeatureStates, client.LastWriteCommand);
        Assert.Equal((uint)NativeRuntimePatchSetId.FrameRateUnlock, client.LastSetRuntimePatchSetId);
        Assert.True(client.LastSetRuntimePatchSetEnable!.Value);

        controller.SetToggle(feature, false);

        Assert.Equal((uint)NativeRuntimePatchSetId.FrameRateUnlock, client.LastSetRuntimePatchSetId);
        Assert.False(client.LastSetRuntimePatchSetEnable!.Value);
    }

    [Fact]
    public void SetToggle_FrameRateUnlock_WhenPatchSetFails_Throws()
    {
        var client = new FakeAgentClient
        {
            LastSetRuntimePatchSetResult = new AgentCommandResultPayload(AgentStatusCode.InternalError, AgentProtocol.Version, 0)
        };
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            "Frame Rate Unlock 60fps",
            "60fps 帧率解锁",
            null,
            ["Frame Rate Unlock 60fps"],
            null,
            null);

        var ex = Assert.Throws<InvalidOperationException>(() => controller.SetToggle(feature, true));
        Assert.Contains("SetRuntimePatchSet", ex.Message);
        Assert.Contains("InternalError", ex.Message);
    }

    [Fact]
    public void TriggerActionRoutesLevelUpToGameApi()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature("Select Unit Level UP", "选择的单位快速升级", null, [], null, null);

        controller.TriggerAction(feature);

        Assert.Null(client.LastWriteCommand);
        Assert.NotNull(client.LastLevelUpRequest);
        Assert.Equal(1u, client.LastLevelUpRequest!.Count);
        Assert.Equal(0u, client.LastLevelUpRequest.Rank);
        Assert.Equal(0u, client.LastLevelUpRequest.Flags);
        Assert.True(client.LastLevelUpRequest.EnableDirectGameApi);
        Assert.Equal(5000u, client.LastLevelUpRequest.TimeoutMilliseconds);
    }

    [Fact]
    public void TriggerActionRoutesReinforcementToGameApiUsingCachedSettings()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        controller.WriteReinforcementSettings(new ReinforcementSettings(0x11112222, 5, 3));
        var feature = new TrainerFeature(TrainerFeatureIds.Reinforcement, "We Need Back", null, [], null, null);

        controller.TriggerAction(feature);

        Assert.Null(client.LastWriteCommand);
        Assert.NotNull(client.LastWeNeedBackRequest);
        Assert.Equal(0x11112222u, client.LastWeNeedBackRequest!.UnitTypeId);
        Assert.Equal(5u, client.LastWeNeedBackRequest.Count);
        Assert.Equal(3u, client.LastWeNeedBackRequest.Rank);
        Assert.True(client.LastWeNeedBackRequest.EnableDirectGameApi);
    }

    [Fact]
    public void WriteSecretProtocolGrantSettingsFeedsTriggerAction()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteSecretProtocolGrantSettings(new SecretProtocolGrantSettings(0xAAAABBBB, 0xCCCCDDDD));
        Assert.Null(client.LastWriteCommand);

        var feature = new TrainerFeature(TrainerFeatureIds.GrantSecretProtocol, "Grant Secret Protocol", null, [], null, null);
        controller.TriggerAction(feature);

        Assert.NotNull(client.LastGrantSecretProtocolRequest);
        Assert.Equal(0xAAAABBBBu, client.LastGrantSecretProtocolRequest!.TechHash);
        Assert.Equal(0xCCCCDDDDu, client.LastGrantSecretProtocolRequest.UpgradeHash);
        Assert.True(client.LastGrantSecretProtocolRequest.EnableDirectGameApi);
    }

    [Fact]
    public void WriteTemplateReplacementSettingsFeedTriggerAction()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        // Model replacement
        controller.WriteTemplateModelReplacementSettings(new TemplateModelReplacementSettings(0x11111111, 0x22222222));
        var modelFeature = new TrainerFeature(TrainerFeatureIds.ReplaceTemplateModel, "Replace Template Model", null, [], null, null);
        controller.TriggerAction(modelFeature);
        Assert.NotNull(client.LastReplaceTemplateModelRequest);
        Assert.Equal(0x11111111u, client.LastReplaceTemplateModelRequest!.TargetHash);
        Assert.Equal(0x22222222u, client.LastReplaceTemplateModelRequest.DonorHash);
        Assert.True(client.LastReplaceTemplateModelRequest.EnableDirectGameApi);

        // Weapon replacement
        controller.WriteTemplateWeaponReplacementSettings(new TemplateWeaponReplacementSettings(0x33333333, 0x44444444));
        var weaponFeature = new TrainerFeature(TrainerFeatureIds.ReplaceTemplateWeapon, "Replace Template Weapon", null, [], null, null);
        controller.TriggerAction(weaponFeature);
        Assert.NotNull(client.LastReplaceTemplateWeaponRequest);
        Assert.Equal(0x33333333u, client.LastReplaceTemplateWeaponRequest!.TargetHash);
        Assert.Equal(0x44444444u, client.LastReplaceTemplateWeaponRequest.DonorHash);
        Assert.True(client.LastReplaceTemplateWeaponRequest.EnableDirectGameApi);
    }

    [Fact]
    public void WriteTargetHealthValueFeedsTriggerActionDispatch()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteTargetHealthValue(123f, 456f);
        Assert.Null(client.LastWriteCommand);

        var feature = new TrainerFeature(TrainerFeatureIds.SetSelectedUnitTargetHealth, "Set Selected Unit Target Health", null, [], null, null);
        controller.TriggerAction(feature);

        Assert.Null(client.LastWriteCommand);
        Assert.NotNull(client.LastSetSelectedUnitHealthRequest);
        Assert.Equal(1u, client.LastSetSelectedUnitHealthRequest!.Mode);
        Assert.Equal(123f, client.LastSetSelectedUnitHealthRequest.Health);
        Assert.Equal(456f, client.LastSetSelectedUnitHealthRequest.MaxHealth);
        Assert.True(client.LastSetSelectedUnitHealthRequest.EnableDirectGameApi);
    }

    private sealed class FakeAgentClient : IAgentClient
    {
        private readonly Queue<AgentMemoryReadPayload> _readPayloads = new();
        private readonly Queue<AgentGameApiSelectedUnitSnapshotPayload> _selectedUnitSnapshotPayloads = new();

        public AgentCommand? LastWriteCommand { get; private set; }
        public AgentMemoryWriteRequest? LastWriteRequest { get; private set; }
        public AgentGameApiSetSelectedStatusBitRequest? LastSetSelectedStatusBitRequest { get; private set; }
        public TimeSpan LastSetSelectedStatusBitPipeTimeout { get; private set; }
        public AgentGameApiSetSelectedUnitHealthRequest? LastSetSelectedUnitHealthRequest { get; private set; }
        public TimeSpan LastSetSelectedUnitHealthPipeTimeout { get; private set; }
        public GameApiDispatchStatus SetSelectedUnitHealthDispatchStatus { get; init; } = GameApiDispatchStatus.Completed;
        public AgentGameApiSelectedUnitUpgradesPayload SelectedUnitUpgradesPayload { get; init; } = BuildEmptyUpgradesPayload();
        public AgentGameApiExpandProductionQueueRequest? LastExpandProductionQueueRequest { get; private set; }
        public TimeSpan LastExpandProductionQueuePipeTimeout { get; private set; }
        public AgentGameApiTeleportSelectedUnitsToMouseRequest? LastTeleportSelectedUnitsToMouseRequest { get; private set; }
        public TimeSpan LastTeleportSelectedUnitsToMousePipeTimeout { get; private set; }
        public AgentGameApiLevelUpSelectedRequest? LastLevelUpRequest { get; private set; }
        public AgentGameApiWeNeedBackRequest? LastWeNeedBackRequest { get; private set; }
        public AgentGameApiGrantSecretProtocolRequest? LastGrantSecretProtocolRequest { get; private set; }
        public AgentGameApiReplaceTemplateModelRequest? LastReplaceTemplateModelRequest { get; private set; }
        public AgentGameApiReplaceTemplateWeaponRequest? LastReplaceTemplateWeaponRequest { get; private set; }
        public List<AgentMemoryReadRequest> ReadRequests { get; } = [];
        public int ReadSelectedUnitSnapshotApiCallCount { get; private set; }
        public int GameModeCallCount { get; private set; }

        public void EnqueueRead(uint expectedAddress, byte[] bytes)
        {
            _readPayloads.Enqueue(new AgentMemoryReadPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                expectedAddress,
                bytes));
        }

        public void EnqueueSelectedUnitSnapshotViaGameApi(uint unitCode, uint thingClassAddress)
        {
            _selectedUnitSnapshotPayloads.Enqueue(new AgentGameApiSelectedUnitSnapshotPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                unitCode,
                thingClassAddress,
                GameApiDispatchStatus.Completed,
                RequestId: 11,
                GameThreadTickBefore: 100,
                GameThreadTickAfter: 101));
        }

        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId, AgentInstallPatchesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // L4: wire to cmd 5 SetFeatureStates
        public SetFeatureStatesRequest? LastSetFeatureStatesRequest { get; private set; }
        public Task<AgentCommandResultPayload> SetFeatureStatesAsync(int processId, SetFeatureStatesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastWriteCommand = AgentCommand.SetFeatureStates;
            LastSetFeatureStatesRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        // L5: wire to cmd 6 SetRuntimePatchSet
        public AgentCommandResultPayload? LastSetRuntimePatchSetResult { get; set; }
        public uint? LastSetRuntimePatchSetId { get; private set; }
        public bool? LastSetRuntimePatchSetEnable { get; private set; }

        public Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(int processId, uint patchSetId, bool enable, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastSetRuntimePatchSetId = patchSetId;
            LastSetRuntimePatchSetEnable = enable;
            return Task.FromResult(LastSetRuntimePatchSetResult ?? new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        // L4: wire to cmd 7 GetFeatureStates
        public Task<FeatureStatesResponse> GetFeatureStatesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FeatureStatesResponse(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                Array.Empty<FeatureStateEntry>()));
        }

        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId, AgentMemoryReadRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ReadRequests.Add(request);
            var payload = _readPayloads.Dequeue();
            Assert.Equal(request.Address, payload.Address);
            return Task.FromResult(payload);
        }

        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId, IReadOnlyList<uint> rvas, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentMismatchDiagnosticsPayload(AgentStatusCode.InvalidCommand, AgentProtocol.Version, 0, [], [], [], MismatchKind.Hook, 0));

        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSignatureScanPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0, 0, new Dictionary<string, uint>()));

        public Task<AgentGameModePayload> GetGameModeAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ++GameModeCallCount;
            return Task.FromResult(new AgentGameModePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameRuntimeConstants.GameModeShell));
        }

        public Task<AgentGameApiGetThingClassPayload> SmokeGetThingClassAsync(
            int processId,
            AgentGameApiGetThingClassRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentGameApiSelectedUnitSnapshotPayload> ReadSelectedUnitSnapshotViaGameApiAsync(
            int processId,
            AgentGameApiReadSelectedUnitCodeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ReadSelectedUnitSnapshotApiCallCount++;
            return Task.FromResult(_selectedUnitSnapshotPayloads.Dequeue());
        }

        public Task<AgentGameApiLevelUpSelectedPayload> LevelUpSelectedAsync(
            int processId,
            AgentGameApiLevelUpSelectedRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastLevelUpRequest = request;
            return Task.FromResult(new AgentGameApiLevelUpSelectedPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiCreateUnitPayload> CreateUnitAsync(
            int processId,
            AgentGameApiCreateUnitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiCreateUnitPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0x12345678,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiKillUnitPayload> KillUnitAsync(
            int processId,
            AgentGameApiKillUnitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiKillUnitPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiCopyForMePayload> CopyForMeAsync(
            int processId,
            AgentGameApiCopyForMeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiCopyForMePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0x12345678,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiGetMeBasePayload> GetMeBaseAsync(
            int processId,
            AgentGameApiGetMeBaseRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiGetMeBasePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiWeNeedBackPayload> WeNeedBackAsync(
            int processId,
            AgentGameApiWeNeedBackRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastWeNeedBackRequest = request;
            return Task.FromResult(new AgentGameApiWeNeedBackPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiSetUnitStatePayload> SetUnitStateAsync(
            int processId,
            AgentGameApiSetUnitStateRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiSetUnitStatePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiGetCurrentPlayerPayload> GetCurrentPlayerAsync(
            int processId,
            AgentGameApiGetCurrentPlayerRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiGetCurrentPlayerPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0x12345678,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiLookupScienceByHashPayload> LookupScienceByHashAsync(
            int processId,
            AgentGameApiLookupScienceByHashRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiLookupScienceByHashPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0x12345678,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiGrantPlayerTechPayload> GrantPlayerTechAsync(
            int processId,
            AgentGameApiGrantPlayerTechRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiGrantPlayerTechPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiGrantUpgradeToPlayerPayload> GrantUpgradeToPlayerAsync(
            int processId,
            AgentGameApiGrantUpgradeToPlayerRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiGrantUpgradeToPlayerPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiHasUpgradePayload> HasUpgradeAsync(
            int processId,
            AgentGameApiHasUpgradeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiHasUpgradePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                1,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiLookupTemplateByHashPayload> LookupTemplateByHashAsync(
            int processId,
            AgentGameApiLookupTemplateByHashRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiLookupTemplateByHashPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0x12345678,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiLookupUpgradeByHashPayload> LookupUpgradeByHashAsync(
            int processId,
            AgentGameApiLookupUpgradeByHashRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiLookupUpgradeByHashPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0x12345678,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiGrantSecretProtocolPayload> GrantSecretProtocolAsync(
            int processId,
            AgentGameApiGrantSecretProtocolRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastGrantSecretProtocolRequest = request;
            return Task.FromResult(new AgentGameApiGrantSecretProtocolPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiGrantSelectedUpgradePayload> GrantSelectedUpgradeAsync(
            int processId,
            AgentGameApiGrantSelectedUpgradeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiGrantSelectedUpgradePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiClearPlayerTechLocksPayload> ClearPlayerTechLocksAsync(
            int processId,
            AgentGameApiClearPlayerTechLocksRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiClearPlayerTechLocksPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiSecretProtocolBindingProbePayload> SecretProtocolBindingProbeAsync(
            int processId,
            AgentGameApiSecretProtocolBindingProbeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiSecretProtocolBindingProbePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                0,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiReplaceTemplateModelPayload> ReplaceTemplateModelAsync(
            int processId,
            AgentGameApiReplaceTemplateModelRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastReplaceTemplateModelRequest = request;
            return Task.FromResult(new AgentGameApiReplaceTemplateModelPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiReplaceTemplateWeaponPayload> ReplaceTemplateWeaponAsync(
            int processId,
            AgentGameApiReplaceTemplateWeaponRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastReplaceTemplateWeaponRequest = request;
            return Task.FromResult(new AgentGameApiReplaceTemplateWeaponPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiSetSelectedStatusBitPayload> SetSelectedStatusBitAsync(
            int processId,
            AgentGameApiSetSelectedStatusBitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastSetSelectedStatusBitRequest = request;
            LastSetSelectedStatusBitPipeTimeout = timeout;
            return Task.FromResult(new AgentGameApiSetSelectedStatusBitPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiSetSelectedUnitHealthPayload> SetSelectedUnitHealthAsync(
            int processId,
            AgentGameApiSetSelectedUnitHealthRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastSetSelectedUnitHealthRequest = request;
            LastSetSelectedUnitHealthPipeTimeout = timeout;
            return Task.FromResult(new AgentGameApiSetSelectedUnitHealthPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                SetSelectedUnitHealthDispatchStatus,
                1,
                0,
                0));
        }

        public Task<AgentGameApiExpandProductionQueuePayload> ExpandProductionQueueAsync(
            int processId,
            AgentGameApiExpandProductionQueueRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastExpandProductionQueueRequest = request;
            LastExpandProductionQueuePipeTimeout = timeout;
            return Task.FromResult(new AgentGameApiExpandProductionQueuePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                2,
                0));
        }

        public Task<AgentGameApiSetSelectedUnitSpeedPayload> SetSelectedUnitSpeedAsync(
            int processId, AgentGameApiSetSelectedUnitSpeedRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiCaptureSelectedUnitsPayload> CaptureSelectedUnitsAsync(
            int processId, AgentGameApiCaptureSelectedUnitsRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiSetSelectedUnitAmmoPayload> SetSelectedUnitAmmoAsync(
            int processId, AgentGameApiSetSelectedUnitAmmoRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiToggleSelectedAttackSpeedPayload> ToggleSelectedAttackSpeedAsync(
            int processId, AgentGameApiToggleSelectedAttackSpeedRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentGameApiToggleSelectedAttackRangePayload> ToggleSelectedAttackRangeAsync(
            int processId, AgentGameApiToggleSelectedAttackRangeRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public AgentGameApiClearSelectedAttackSpeedEffectsRequest? LastClearSelectedAttackSpeedEffectsRequest { get; private set; }
        public TimeSpan LastClearSelectedAttackSpeedEffectsPipeTimeout { get; private set; }
        public AgentGameApiClearSelectedAttackRangeEffectsRequest? LastClearSelectedAttackRangeEffectsRequest { get; private set; }
        public TimeSpan LastClearSelectedAttackRangeEffectsPipeTimeout { get; private set; }

        public Task<AgentGameApiClearSelectedAttackSpeedEffectsPayload> ClearSelectedAttackSpeedEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackSpeedEffectsRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastClearSelectedAttackSpeedEffectsRequest = request;
            LastClearSelectedAttackSpeedEffectsPipeTimeout = timeout;
            return Task.FromResult(new AgentGameApiClearSelectedAttackSpeedEffectsPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiClearSelectedAttackRangeEffectsPayload> ClearSelectedAttackRangeEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackRangeEffectsRequest request, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastClearSelectedAttackRangeEffectsRequest = request;
            LastClearSelectedAttackRangeEffectsPipeTimeout = timeout;
            return Task.FromResult(new AgentGameApiClearSelectedAttackRangeEffectsPayload(
                AgentStatusCode.Ok, AgentProtocol.Version, GameApiDispatchStatus.Completed, 1, 0, 0));
        }

        public Task<AgentGameApiTeleportSelectedUnitsToMousePayload> TeleportSelectedUnitsToMouseAsync(
            int processId,
            AgentGameApiTeleportSelectedUnitsToMouseRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastTeleportSelectedUnitsToMouseRequest = request;
            LastTeleportSelectedUnitsToMousePipeTimeout = timeout;
            return Task.FromResult(new AgentGameApiTeleportSelectedUnitsToMousePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        public Task<AgentGameApiSelectedUnitUpgradesPayload> GetSelectedUnitUpgradesAsync(
            int processId,
            AgentGameApiGetSelectedUnitUpgradesRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SelectedUnitUpgradesPayload);
        }

        private static AgentGameApiSelectedUnitUpgradesPayload BuildEmptyUpgradesPayload() =>
            new AgentGameApiSelectedUnitUpgradesPayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                UnitTypeId: 0,
                ThingTemplateAddress: 0,
                Count: 0,
                UpgradeHash0: 0, UpgradeHash1: 0, UpgradeHash2: 0, UpgradeHash3: 0,
                UpgradeHash4: 0, UpgradeHash5: 0, UpgradeHash6: 0, UpgradeHash7: 0,
                UpgradeHash8: 0, UpgradeHash9: 0, UpgradeHash10: 0, UpgradeHash11: 0,
                UpgradeHash12: 0, UpgradeHash13: 0, UpgradeHash14: 0, UpgradeHash15: 0,
                UpgradeHash16: 0, UpgradeHash17: 0, UpgradeHash18: 0, UpgradeHash19: 0,
                DispatchStatus: GameApiDispatchStatus.Completed,
                RequestId: 1,
                GameThreadTickBefore: 0,
                GameThreadTickAfter: 0);

        public Task<AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload> GrantObjectUpgradeOnSelectedSameTypeAsync(
            int processId,
            AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload(
                AgentStatusCode.Ok,
                AgentProtocol.Version,
                GameApiDispatchStatus.Completed,
                1,
                0,
                0));
        }

        private Task<AgentCommandResultPayload> RecordWrite(AgentCommand command, AgentMemoryWriteRequest request)
        {
            LastWriteCommand = command;
            LastWriteRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 24));
        }
    }
}
