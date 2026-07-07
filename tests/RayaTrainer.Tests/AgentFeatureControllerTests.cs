using RayaTrainer.Core.Agent;
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
    public void SetToggleSendsEnableFlagsAndDirectBytePatchesToAgent()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            "Free Build",
            "建筑物可随地建造",
            "L",
            ["iEnable+F"],
            null,
            null,
            [new TrainerFeatureBytePatch("MustCode+1201", [0xEB, 0x0C], [0x75, 0x0C])]);

        controller.SetToggle(feature, true);

        Assert.Equal(AgentCommand.SetToggle, client.LastWriteCommand);
        Assert.Collection(
            client.LastWriteRequest!.Writes,
            write =>
            {
                Assert.Equal(0x70010Fu, write.Address);
                Assert.Equal(AgentMemoryAddressMode.Direct, write.AddressMode);
                Assert.Equal([0x01], write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x701401u, write.Address);
                Assert.Equal(AgentMemoryAddressMode.Direct, write.AddressMode);
                Assert.Equal([0xEB, 0x0C], write.Bytes);
            });
    }

    [Fact]
    public void SetToggleSendsIndirectBytePatchAsAgentDereferenceWrite()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            "Free Build",
            "建筑物可随地建造",
            "L",
            [],
            null,
            null,
            [new TrainerFeatureBytePatch("[MustCode+1201]", [0xEB, 0x0C], [0x75, 0x0C])]);

        controller.SetToggle(feature, true);

        var write = Assert.Single(client.LastWriteRequest!.Writes);
        Assert.Equal(0x701401u, write.Address);
        Assert.Equal(AgentMemoryAddressMode.DereferenceUInt32, write.AddressMode);
        Assert.Equal([0xEB, 0x0C], write.Bytes);
    }

    [Fact]
    public void TriggerActionSendsReinforcementSettingsBeforeDispatch()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature("We Need Back", "呼叫战场增援", "J", [], "MustCode2+B00", "0x0C");

        controller.TriggerAction(feature, new ReinforcementSettings(0x12345678, 12, 2));

        Assert.Equal(AgentCommand.TriggerAction, client.LastWriteCommand);
        Assert.Collection(
            client.LastWriteRequest!.Writes,
            write =>
            {
                Assert.Equal(0x700124u, write.Address);
                Assert.Equal(BitConverter.GetBytes(0x12345678u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700128u, write.Address);
                Assert.Equal(BitConverter.GetBytes(12u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x70012Cu, write.Address);
                Assert.Equal(BitConverter.GetBytes(2u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700120u, write.Address);
                Assert.Equal([0x0C], write.Bytes);
            });
    }

    [Fact]
    public void WriteResourceValuesSendsRuntimeParameters()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteResourceValues(new ResourceValueSettings(123456, 234567, 9));

        Assert.Equal(AgentCommand.WriteResourceValues, client.LastWriteCommand);
        Assert.Collection(
            client.LastWriteRequest!.Writes,
            write =>
            {
                Assert.Equal(0x700130u, write.Address);
                Assert.Equal(BitConverter.GetBytes(123456u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700134u, write.Address);
                Assert.Equal(BitConverter.GetBytes(234567u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700138u, write.Address);
                Assert.Equal(BitConverter.GetBytes(9u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x7001DCu, write.Address);
                Assert.Equal(BitConverter.GetBytes(9999999.0f), write.Bytes);
            });
    }

    [Fact]
    public void WriteSecretProtocolGrantSettingsSendsRuntimeParameters()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteSecretProtocolGrantSettings(new SecretProtocolGrantSettings(0xDD6C4C5B, 0x33D87C97));

        Assert.Equal(AgentCommand.TriggerAction, client.LastWriteCommand);
        Assert.Collection(
            client.LastWriteRequest!.Writes,
            write =>
            {
                Assert.Equal(0x70015Cu, write.Address);
                Assert.Equal(BitConverter.GetBytes(0xDD6C4C5Bu), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700160u, write.Address);
                Assert.Equal(BitConverter.GetBytes(0x33D87C97u), write.Bytes);
            });
    }

    [Fact]
    public void WriteTemplateModelReplacementSettingsSendsRuntimeParametersAndClearsStatus()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteTemplateModelReplacementSettings(new TemplateModelReplacementSettings(0x11111111, 0x22222222));

        Assert.Equal(AgentCommand.TriggerAction, client.LastWriteCommand);
        Assert.Collection(
            client.LastWriteRequest!.Writes,
            write =>
            {
                Assert.Equal(0x70016Cu, write.Address);
                Assert.Equal(BitConverter.GetBytes(0x11111111u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700170u, write.Address);
                Assert.Equal(BitConverter.GetBytes(0x22222222u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700174u, write.Address);
                Assert.Equal(BitConverter.GetBytes(0u), write.Bytes);
            });
    }

    [Fact]
    public void WriteTemplateWeaponReplacementSettingsSendsRuntimeParametersAndClearsStatus()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteTemplateWeaponReplacementSettings(new TemplateWeaponReplacementSettings(0x11111111, 0x22222222));

        Assert.Equal(AgentCommand.TriggerAction, client.LastWriteCommand);
        Assert.Collection(
            client.LastWriteRequest!.Writes,
            write =>
            {
                Assert.Equal(0x700178u, write.Address);
                Assert.Equal(BitConverter.GetBytes(0x11111111u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x70017Cu, write.Address);
                Assert.Equal(BitConverter.GetBytes(0x22222222u), write.Bytes);
            },
            write =>
            {
                Assert.Equal(0x700180u, write.Address);
                Assert.Equal(BitConverter.GetBytes(0u), write.Bytes);
            });
    }

    [Fact]
    public void ReadSecretProtocolBindingProbeResultReadsRuntimeDiagnosticsThroughAgent()
    {
        var client = new FakeAgentClient();
        client.EnqueueRead(0x700140, BitConverter.GetBytes(0x20000000u));
        client.EnqueueRead(0x700144, BitConverter.GetBytes(0x20001320u));
        client.EnqueueRead(0x700148, BitConverter.GetBytes(0x12345678u));
        client.EnqueueRead(0x70014C, BitConverter.GetBytes((uint)SecretProtocolBindingItemStatus.TechAndUpgradeGranted));
        client.EnqueueRead(0x700150, BitConverter.GetBytes(0x23456789u));
        client.EnqueueRead(0x700154, BitConverter.GetBytes((uint)SecretProtocolBindingItemStatus.TechGrantedUpgradeManuallyGranted));
        client.EnqueueRead(0x700158, BitConverter.GetBytes((uint)SecretProtocolBindingProbeStatus.Completed));
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        var result = controller.ReadSecretProtocolBindingProbeResult();

        Assert.Equal(0x20000000u, result.PlayerAddress);
        Assert.Equal(0x20001320u, result.ScienceManagerAddress);
        Assert.Equal(0x12345678u, result.AirPowerTechAddress);
        Assert.Equal(SecretProtocolBindingItemStatus.TechAndUpgradeGranted, result.AirPowerStatus);
        Assert.Equal(0x23456789u, result.EnhancedKamikazeTechAddress);
        Assert.Equal(SecretProtocolBindingItemStatus.TechGrantedUpgradeManuallyGranted, result.EnhancedKamikazeStatus);
        Assert.Equal(SecretProtocolBindingProbeStatus.Completed, result.Status);
        Assert.Equal(7, client.ReadRequests.Count);
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
    public void WriteTargetHealthValueUsesSetSelectedUnitHealthGameApi()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);

        controller.WriteTargetHealthValue(123f, 456f);

        Assert.Null(client.LastWriteCommand);
        Assert.Equal(1u, client.LastSetSelectedUnitHealthRequest!.Mode);
        Assert.Equal(123f, client.LastSetSelectedUnitHealthRequest.Health);
        Assert.Equal(456f, client.LastSetSelectedUnitHealthRequest.MaxHealth);
        Assert.Equal(5000u, client.LastSetSelectedUnitHealthRequest.TimeoutMilliseconds);
        Assert.Equal(TimeSpan.FromSeconds(8), client.LastSetSelectedUnitHealthPipeTimeout);
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
    public async Task TriggerActionAndWaitForConsumptionPollsAgentUntilDispatchClears()
    {
        var client = new FakeAgentClient();
        client.EnqueueRead(0x700120, [0x08]);
        client.EnqueueRead(0x700120, [0x00]);
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature("Select Unit Level UP", "选择的单位快速升级", "P", [], "MustCode2+700", "0x08");

        var result = await controller.TriggerActionAndWaitForConsumptionAsync(
            feature,
            timeout: TimeSpan.FromMilliseconds(100),
            pollInterval: TimeSpan.FromMilliseconds(1));

        Assert.Equal(ActionDispatchResult.Consumed, result);
        Assert.Equal(AgentCommand.TriggerAction, client.LastWriteCommand);
        Assert.Equal([0x08], Assert.Single(client.LastWriteRequest!.Writes).Bytes);
        Assert.Equal(2, client.ReadRequests.Count);
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
        public AgentGameApiExpandProductionQueueRequest? LastExpandProductionQueueRequest { get; private set; }
        public TimeSpan LastExpandProductionQueuePipeTimeout { get; private set; }
        public AgentGameApiTeleportSelectedUnitsToMouseRequest? LastTeleportSelectedUnitsToMouseRequest { get; private set; }
        public TimeSpan LastTeleportSelectedUnitsToMousePipeTimeout { get; private set; }
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

        public Task<AgentCommandResultPayload> SetToggleAsync(int processId, AgentMemoryWriteRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            RecordWrite(AgentCommand.SetToggle, request);

        public Task<AgentCommandResultPayload> TriggerActionAsync(int processId, AgentMemoryWriteRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            RecordWrite(AgentCommand.TriggerAction, request);

        public Task<AgentCommandResultPayload> WriteResourceValuesAsync(int processId, AgentMemoryWriteRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            RecordWrite(AgentCommand.WriteResourceValues, request);

        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId, AgentMemoryReadRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ReadRequests.Add(request);
            var payload = _readPayloads.Dequeue();
            Assert.Equal(request.Address, payload.Address);
            return Task.FromResult(payload);
        }

        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId, IReadOnlyList<uint> rvas, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentMismatchDiagnosticsPayload(AgentStatusCode.InvalidCommand, AgentProtocol.Version, 0, [], [], []));

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

        private Task<AgentCommandResultPayload> RecordWrite(AgentCommand command, AgentMemoryWriteRequest request)
        {
            LastWriteCommand = command;
            LastWriteRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 24));
        }
    }
}
