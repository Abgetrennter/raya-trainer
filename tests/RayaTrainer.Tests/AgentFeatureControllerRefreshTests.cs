using System.Linq;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentFeatureControllerRefreshTests
{
    private static readonly AgentStatusPayload AgentStatus = new(
        AgentStatusCode.Ok,
        AgentProtocol.Version,
        ProcessId: 1234,
        ModuleBase: 0x400000,
        InstalledHookCount: 24);

    /// <summary>
    /// Base stub for IAgentGameApiClient + IAgentClient that throws NotSupportedException
    /// for all GameApi methods. Override only the methods needed for refresh tests.
    /// </summary>
    private abstract class StubAgentClient : IAgentClient
    {
        public abstract Task<AgentCommandResultPayload> SetFeatureStatesAsync(
            int processId, SetFeatureStatesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default);
        public abstract Task<FeatureStatesResponse> GetFeatureStatesAsync(
            int processId, TimeSpan timeout, CancellationToken cancellationToken = default);

        public Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(int processId, uint patchSetId, bool enable, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId, AgentInstallPatchesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId, AgentMemoryReadRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId, IReadOnlyList<uint> rvas, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameModePayload> GetGameModeAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        // IAgentGameApiClient stubs
        public Task<AgentGameApiGetThingClassPayload> SmokeGetThingClassAsync(int processId, AgentGameApiGetThingClassRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSelectedUnitSnapshotPayload> ReadSelectedUnitSnapshotViaGameApiAsync(int processId, AgentGameApiReadSelectedUnitCodeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiLevelUpSelectedPayload> LevelUpSelectedAsync(int processId, AgentGameApiLevelUpSelectedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiCreateUnitPayload> CreateUnitAsync(int processId, AgentGameApiCreateUnitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiKillUnitPayload> KillUnitAsync(int processId, AgentGameApiKillUnitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiCopyForMePayload> CopyForMeAsync(int processId, AgentGameApiCopyForMeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiGetMeBasePayload> GetMeBaseAsync(int processId, AgentGameApiGetMeBaseRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiWeNeedBackPayload> WeNeedBackAsync(int processId, AgentGameApiWeNeedBackRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSetUnitStatePayload> SetUnitStateAsync(int processId, AgentGameApiSetUnitStateRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiGetCurrentPlayerPayload> GetCurrentPlayerAsync(int processId, AgentGameApiGetCurrentPlayerRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiLookupScienceByHashPayload> LookupScienceByHashAsync(int processId, AgentGameApiLookupScienceByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiGrantPlayerTechPayload> GrantPlayerTechAsync(int processId, AgentGameApiGrantPlayerTechRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiGrantUpgradeToPlayerPayload> GrantUpgradeToPlayerAsync(int processId, AgentGameApiGrantUpgradeToPlayerRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiHasUpgradePayload> HasUpgradeAsync(int processId, AgentGameApiHasUpgradeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiLookupTemplateByHashPayload> LookupTemplateByHashAsync(int processId, AgentGameApiLookupTemplateByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiLookupUpgradeByHashPayload> LookupUpgradeByHashAsync(int processId, AgentGameApiLookupUpgradeByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiGrantSecretProtocolPayload> GrantSecretProtocolAsync(int processId, AgentGameApiGrantSecretProtocolRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiGrantSelectedUpgradePayload> GrantSelectedUpgradeAsync(int processId, AgentGameApiGrantSelectedUpgradeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiClearPlayerTechLocksPayload> ClearPlayerTechLocksAsync(int processId, AgentGameApiClearPlayerTechLocksRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSecretProtocolBindingProbePayload> SecretProtocolBindingProbeAsync(int processId, AgentGameApiSecretProtocolBindingProbeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiReplaceTemplateModelPayload> ReplaceTemplateModelAsync(int processId, AgentGameApiReplaceTemplateModelRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiReplaceTemplateWeaponPayload> ReplaceTemplateWeaponAsync(int processId, AgentGameApiReplaceTemplateWeaponRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedStatusBitPayload> SetSelectedStatusBitAsync(int processId, AgentGameApiSetSelectedStatusBitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitHealthPayload> SetSelectedUnitHealthAsync(int processId, AgentGameApiSetSelectedUnitHealthRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiExpandProductionQueuePayload> ExpandProductionQueueAsync(int processId, AgentGameApiExpandProductionQueueRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiTeleportSelectedUnitsToMousePayload> TeleportSelectedUnitsToMouseAsync(int processId, AgentGameApiTeleportSelectedUnitsToMouseRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitSpeedPayload> SetSelectedUnitSpeedAsync(int processId, AgentGameApiSetSelectedUnitSpeedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiCaptureSelectedUnitsPayload> CaptureSelectedUnitsAsync(int processId, AgentGameApiCaptureSelectedUnitsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitAmmoPayload> SetSelectedUnitAmmoAsync(int processId, AgentGameApiSetSelectedUnitAmmoRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiToggleSelectedAttackSpeedPayload> ToggleSelectedAttackSpeedAsync(int processId, AgentGameApiToggleSelectedAttackSpeedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiToggleSelectedAttackRangePayload> ToggleSelectedAttackRangeAsync(int processId, AgentGameApiToggleSelectedAttackRangeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiClearSelectedAttackSpeedEffectsPayload> ClearSelectedAttackSpeedEffectsAsync(int processId, AgentGameApiClearSelectedAttackSpeedEffectsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiClearSelectedAttackRangeEffectsPayload> ClearSelectedAttackRangeEffectsAsync(int processId, AgentGameApiClearSelectedAttackRangeEffectsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiSelectedUnitUpgradesPayload> GetSelectedUnitUpgradesAsync(int processId, AgentGameApiGetSelectedUnitUpgradesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload> GrantObjectUpgradeOnSelectedSameTypeAsync(int processId, AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Fake client that records SetFeatureStates requests and returns
    /// configurable GetFeatureStates responses.
    /// </summary>
    private sealed class RefreshableFakeClient : StubAgentClient
    {
        public List<(uint StateId, uint Value)>? LastWrittenStates { get; private set; }
        public FeatureStatesResponse NextGetFeatureStatesResponse { get; set; } = new(
            AgentStatusCode.Ok, AgentProtocol.Version, Array.Empty<FeatureStateEntry>());

        public override Task<AgentCommandResultPayload> SetFeatureStatesAsync(
            int processId, SetFeatureStatesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastWrittenStates = request.States.ToList();
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public override Task<FeatureStatesResponse> GetFeatureStatesAsync(
            int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextGetFeatureStatesResponse);
        }
    }

    private static AgentFeatureController CreateController(RefreshableFakeClient client)
    {
        return new AgentFeatureController(client, 1234, AgentStatus);
    }

    private static TrainerFeature GetToggleFeature(string rawName)
    {
        return TrainerFeatureCatalog.CreateGridFeatures(TestAssets.LoadManifest().Features)
            .First(f => f.RawName == rawName);
    }

    [Fact]
    public void ReadToggleState_ReturnsNull_BeforeFirstRefreshOrWrite()
    {
        var client = new RefreshableFakeClient();
        var controller = CreateController(client);
        var feature = GetToggleFeature("Power");

        Assert.Null(controller.ReadToggleState(feature));
    }

    [Fact]
    public async Task RefreshRuntimeStateAsync_PopulatesObservedCache()
    {
        var client = new RefreshableFakeClient();
        client.NextGetFeatureStatesResponse = new FeatureStatesResponse(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            new List<FeatureStateEntry>
            {
                new((uint)NativeFeatureStateId.Power, 1),
                new((uint)NativeFeatureStateId.FastBuild, 0)
            });

        var controller = CreateController(client);
        var powerFeature = GetToggleFeature("Power");
        var fastBuildFeature = GetToggleFeature("FAST BUILD");

        var response = await controller.RefreshRuntimeStateAsync();

        Assert.Equal(AgentStatusCode.Ok, response.StatusCode);
        Assert.Equal(2, response.Entries.Count);

        Assert.True(controller.ReadToggleState(powerFeature));
        Assert.False(controller.ReadToggleState(fastBuildFeature));
    }

    [Fact]
    public void ReadToggleState_Throws_ForPulseFeature()
    {
        var client = new RefreshableFakeClient();
        var controller = CreateController(client);

        // Money is a pulse feature (MoneyPulse = 1)
        var pulseFeature = GetToggleFeature("Money");

        // Write first to populate cache
        controller.SetToggle(pulseFeature, true);

        Assert.Throws<InvalidOperationException>(() => controller.ReadToggleState(pulseFeature));
    }

    [Fact]
    public async Task ReadPulseFired_ReturnsBool_ForPulseFeature()
    {
        var client = new RefreshableFakeClient();
        client.NextGetFeatureStatesResponse = new FeatureStatesResponse(
            AgentStatusCode.Ok,
            AgentProtocol.Version,
            new List<FeatureStateEntry>
            {
                new((uint)NativeFeatureStateId.MoneyPulse, 1)
            });

        var controller = CreateController(client);
        var pulseFeature = GetToggleFeature("Money");

        await controller.RefreshRuntimeStateAsync();

        var fired = controller.ReadPulseFired(pulseFeature);
        Assert.True(fired);
    }

    [Fact]
    public async Task ReadPulseFired_ReturnsNull_ForNonPulseFeature()
    {
        var client = new RefreshableFakeClient();
        var controller = CreateController(client);
        var toggleFeature = GetToggleFeature("Power");

        await controller.RefreshRuntimeStateAsync();

        Assert.Null(controller.ReadPulseFired(toggleFeature));
    }

    [Fact]
    public async Task SetFeatureStates_WiresCorrectly()
    {
        var client = new RefreshableFakeClient();
        var controller = CreateController(client);

        var powerFeature = GetToggleFeature("Power");
        controller.SetToggle(powerFeature, true);

        Assert.NotNull(client.LastWrittenStates);
        Assert.Single(client.LastWrittenStates);
        Assert.Equal((uint)NativeFeatureStateId.Power, client.LastWrittenStates[0].StateId);
        Assert.Equal(1u, client.LastWrittenStates[0].Value);

        // Local cache should be updated
        Assert.True(controller.ReadToggleState(powerFeature));
    }
}
