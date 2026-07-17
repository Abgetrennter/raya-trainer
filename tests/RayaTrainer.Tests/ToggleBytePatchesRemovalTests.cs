using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ToggleBytePatchesRemovalTests
{
    /// <summary>
    /// Compile-time check: TrainerFeature no longer has a ToggleBytePatches property.
    /// </summary>
    [Fact]
    public void TrainerFeature_HasNoToggleBytePatchesProperty()
    {
        var prop = typeof(TrainerFeature).GetProperty("ToggleBytePatches");
        Assert.Null(prop);
    }

    /// <summary>
    /// Compile-time check: TrainerFeatureBytePatch type no longer exists.
    /// </summary>
    [Fact]
    public void TrainerFeatureBytePatchTypeDoesNotExist()
    {
        var type = typeof(TrainerManifest).Assembly
            .GetType("RayaTrainer.Core.Manifest.TrainerFeatureBytePatch");
        Assert.Null(type);
    }

    /// <summary>
    /// IsToggle no longer considers byte patches — only EnableFlags.
    /// Feature with empty EnableFlags + null ValueHint returns false.
    /// </summary>
    [Fact]
    public void FeatureDispatchDefaults_IsToggle_NoLongerConsidersBytePatches()
    {
        // Feature with empty EnableFlags — was true pre-migration if ToggleBytePatches had entries.
        var feature = new TrainerFeature(
            "Test Feature",
            "Test",
            null,
            [],
            null,
            null);

        Assert.False(FeatureDispatchDefaults.IsToggle(feature));
    }

    /// <summary>
    /// Feature with non-empty EnableFlags + null ValueHint — still a toggle.
    /// </summary>
    [Fact]
    public void FeatureDispatchDefaults_IsToggle_StillReturnsTrueForEnableFlagsOnly()
    {
        var feature = new TrainerFeature(
            "Player God Mode",
            "无敌",
            null,
            ["Player God Mode"],
            null,
            null);

        Assert.True(FeatureDispatchDefaults.IsToggle(feature));
    }

    /// <summary>
    /// SetToggle for FrameRateUnlock calls SetRuntimePatchSet via catalog-driven lookup.
    /// </summary>
    [Fact]
    public void AgentFeatureController_SetToggle_WithPatchSetBinding_CallsSetRuntimePatchSet()
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

        Assert.Equal(AgentCommand.SetFeatureStates, client.LastWriteCommand);
        Assert.Equal((uint)NativeRuntimePatchSetId.FrameRateUnlock, client.LastSetRuntimePatchSetId);
        Assert.True(client.LastSetRuntimePatchSetEnable!.Value);

        controller.SetToggle(feature, false);

        Assert.Equal((uint)NativeRuntimePatchSetId.FrameRateUnlock, client.LastSetRuntimePatchSetId);
        Assert.False(client.LastSetRuntimePatchSetEnable!.Value);
    }

    /// <summary>
    /// SetToggle for a feature without PatchSetId binding does NOT call SetRuntimePatchSet.
    /// </summary>
    [Fact]
    public void AgentFeatureController_SetToggle_WithoutPatchSetBinding_NoSetRuntimePatchSetCall()
    {
        var client = new FakeAgentClient();
        var controller = new AgentFeatureController(client, 1234, AgentStatus);
        var feature = new TrainerFeature(
            "Power",
            "无限电力",
            null,
            ["Power"],
            null,
            null);

        controller.SetToggle(feature, true);

        Assert.Equal(AgentCommand.SetFeatureStates, client.LastWriteCommand);
        Assert.Null(client.LastSetRuntimePatchSetId);
    }

    private static readonly AgentStatusPayload AgentStatus = new(
        AgentStatusCode.Ok,
        AgentProtocol.Version,
        ProcessId: 1234,
        ModuleBase: 0x400000,
        InstalledHookCount: 24);

    private sealed class FakeAgentClient : IAgentClient
    {
        public AgentCommand? LastWriteCommand { get; private set; }
        public SetFeatureStatesRequest? LastSetFeatureStatesRequest { get; private set; }
        public uint? LastSetRuntimePatchSetId { get; private set; }
        public bool? LastSetRuntimePatchSetEnable { get; private set; }

        public Task<AgentCommandResultPayload> SetFeatureStatesAsync(
            int processId,
            SetFeatureStatesRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastWriteCommand = AgentCommand.SetFeatureStates;
            LastSetFeatureStatesRequest = request;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentCommandResultPayload> SetRuntimePatchSetAsync(
            int processId,
            uint patchSetId,
            bool enable,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastSetRuntimePatchSetId = patchSetId;
            LastSetRuntimePatchSetEnable = enable;
            return Task.FromResult(new AgentCommandResultPayload(AgentStatusCode.Ok, AgentProtocol.Version, 0));
        }

        public Task<AgentPingPayload> PingAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentStatusPayload> GetStatusAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentCommandResultPayload> InstallPatchesAsync(int processId, AgentInstallPatchesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentCommandResultPayload> RestorePatchesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentMemoryReadPayload> ReadMemoryAsync(int processId, AgentMemoryReadRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentCommandResultPayload> SetNativeCatalogAsync(int processId, IReadOnlyList<uint> rvas, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentMismatchDiagnosticsPayload> GetMismatchDiagnosticsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentSignatureScanPayload> ScanSignaturesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameModePayload> GetGameModeAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FeatureStatesResponse> GetFeatureStatesAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // GameApi methods — not used in these tests
        public Task<AgentGameApiGetThingClassPayload> SmokeGetThingClassAsync(
            int processId, AgentGameApiGetThingClassRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSelectedUnitSnapshotPayload> ReadSelectedUnitSnapshotViaGameApiAsync(
            int processId, AgentGameApiReadSelectedUnitCodeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLevelUpSelectedPayload> LevelUpSelectedAsync(
            int processId, AgentGameApiLevelUpSelectedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiCreateUnitPayload> CreateUnitAsync(
            int processId, AgentGameApiCreateUnitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiKillUnitPayload> KillUnitAsync(
            int processId, AgentGameApiKillUnitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiCopyForMePayload> CopyForMeAsync(
            int processId, AgentGameApiCopyForMeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGetMeBasePayload> GetMeBaseAsync(
            int processId, AgentGameApiGetMeBaseRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiWeNeedBackPayload> WeNeedBackAsync(
            int processId, AgentGameApiWeNeedBackRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetUnitStatePayload> SetUnitStateAsync(
            int processId, AgentGameApiSetUnitStateRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGetCurrentPlayerPayload> GetCurrentPlayerAsync(
            int processId, AgentGameApiGetCurrentPlayerRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLookupScienceByHashPayload> LookupScienceByHashAsync(
            int processId, AgentGameApiLookupScienceByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantPlayerTechPayload> GrantPlayerTechAsync(
            int processId, AgentGameApiGrantPlayerTechRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantUpgradeToPlayerPayload> GrantUpgradeToPlayerAsync(
            int processId, AgentGameApiGrantUpgradeToPlayerRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiHasUpgradePayload> HasUpgradeAsync(
            int processId, AgentGameApiHasUpgradeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLookupTemplateByHashPayload> LookupTemplateByHashAsync(
            int processId, AgentGameApiLookupTemplateByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiLookupUpgradeByHashPayload> LookupUpgradeByHashAsync(
            int processId, AgentGameApiLookupUpgradeByHashRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantSecretProtocolPayload> GrantSecretProtocolAsync(
            int processId, AgentGameApiGrantSecretProtocolRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantSelectedUpgradePayload> GrantSelectedUpgradeAsync(
            int processId, AgentGameApiGrantSelectedUpgradeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiClearPlayerTechLocksPayload> ClearPlayerTechLocksAsync(
            int processId, AgentGameApiClearPlayerTechLocksRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSecretProtocolBindingProbePayload> SecretProtocolBindingProbeAsync(
            int processId, AgentGameApiSecretProtocolBindingProbeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiReplaceTemplateModelPayload> ReplaceTemplateModelAsync(
            int processId, AgentGameApiReplaceTemplateModelRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiReplaceTemplateWeaponPayload> ReplaceTemplateWeaponAsync(
            int processId, AgentGameApiReplaceTemplateWeaponRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedStatusBitPayload> SetSelectedStatusBitAsync(
            int processId, AgentGameApiSetSelectedStatusBitRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitHealthPayload> SetSelectedUnitHealthAsync(
            int processId, AgentGameApiSetSelectedUnitHealthRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiExpandProductionQueuePayload> ExpandProductionQueueAsync(
            int processId, AgentGameApiExpandProductionQueueRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitSpeedPayload> SetSelectedUnitSpeedAsync(
            int processId, AgentGameApiSetSelectedUnitSpeedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiCaptureSelectedUnitsPayload> CaptureSelectedUnitsAsync(
            int processId, AgentGameApiCaptureSelectedUnitsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSetSelectedUnitAmmoPayload> SetSelectedUnitAmmoAsync(
            int processId, AgentGameApiSetSelectedUnitAmmoRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiToggleSelectedAttackSpeedPayload> ToggleSelectedAttackSpeedAsync(
            int processId, AgentGameApiToggleSelectedAttackSpeedRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiToggleSelectedAttackRangePayload> ToggleSelectedAttackRangeAsync(
            int processId, AgentGameApiToggleSelectedAttackRangeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiClearSelectedAttackSpeedEffectsPayload> ClearSelectedAttackSpeedEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackSpeedEffectsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiClearSelectedAttackRangeEffectsPayload> ClearSelectedAttackRangeEffectsAsync(
            int processId, AgentGameApiClearSelectedAttackRangeEffectsRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiTeleportSelectedUnitsToMousePayload> TeleportSelectedUnitsToMouseAsync(
            int processId, AgentGameApiTeleportSelectedUnitsToMouseRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiSelectedUnitUpgradesPayload> GetSelectedUnitUpgradesAsync(
            int processId, AgentGameApiGetSelectedUnitUpgradesRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentGameApiGrantObjectUpgradeOnSelectedSameTypePayload> GrantObjectUpgradeOnSelectedSameTypeAsync(
            int processId, AgentGameApiGrantObjectUpgradeOnSelectedSameTypeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
